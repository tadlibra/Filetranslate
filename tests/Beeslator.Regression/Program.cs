using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Filetranslate;

internal static class RegressionProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { Form1.RunChecksAsync(args).GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

public partial class Form1
{
    private void InitializeComponent() { }
    private void BuildUi() { }
    private void UpdateProgress() { }
    private void SetStatus(string value) { }
    private void Log(string value) { }
    private Form1(HttpClient client) { _httpClient.Dispose(); _httpClient = client; }

    private sealed class FakeTranslator : HttpMessageHandler
    {
        public int Calls;
        public int TextCount;
        public Action? BeforeResponse;
        public bool Fail;
        public bool CorruptTokens;
        public string? FailText;
        public int RateLimitResponses;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            BeforeResponse?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (Fail) return new(HttpStatusCode.BadRequest);
            if (RateLimitResponses > 0)
            {
                RateLimitResponses--;
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return limited;
            }
            if (request.Method == HttpMethod.Get)
            {
                var query = request.RequestUri!.Query.Split('&').Single(q => q.StartsWith("q="))[2..];
                var text = Uri.UnescapeDataString(query);
                TextCount++;
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new object[] { new object[] { new object[] { "VI " + text } } })) };
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var prompt = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            var json = prompt[(prompt.IndexOf("Input JSON array:", StringComparison.Ordinal) + "Input JSON array:".Length)..].Trim();
            var input = JsonSerializer.Deserialize<List<string>>(json)!;
            TextCount += input.Count;
            if (FailText is not null && input.Any(t => t.Contains(FailText))) return new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"not json\"}}]}") };
            var output = input.Select(t => CorruptTokens ? "BROKEN" : "VI " + t).ToList();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = JsonSerializer.Serialize(output) } } } })) };
        }
    }

    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _checks++;
    }

    internal static async Task RunChecksAsync(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--live") { await RunLiveSampleAsync(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--pack") { await RunPackSampleAsync(args[1], args[2..]); return; }
        var root = Path.GetFullPath(Path.Combine("obj", "regression", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        using var fake = new FakeTranslator();
        using var form = new Form1(new HttpClient(fake));
        SynchronizationContext.SetSynchronizationContext(null);
        form._aiConfig = new(TranslatorProvider.OpenAiCompatible, new Uri("https://test.invalid/gateway/v1"), "test", "test", false);
        var ct = CancellationToken.None;
        var input = "§6Sword %1$s $(l:test/page)Click$() ${player} {count} minecraft:stone https://example.org\nNext";
        var protectedText = form.ProtectTokens(input, out var tokens);
        Check(RestoreTokens(protectedText, tokens) == input, "token round-trip");
        try { RestoreTokens("broken", tokens); Check(false, "missing tokens rejected"); } catch (FormatException) { _checks++; }
        var protectedKey = ProtectApiKey("sk-test-123");
        Check(protectedKey != "sk-test-123" && UnprotectApiKey(protectedKey) == "sk-test-123" && UnprotectApiKey("plain-old") == "plain-old" && ProtectApiKey(protectedKey) == protectedKey, "API key DPAPI round-trip and legacy plain key");
        Check(ApiEndpoint(new Uri("https://test.invalid/gateway/v1"), "v1/chat/completions").AbsolutePath == "/gateway/v1/chat/completions", "proxy endpoint prefix");
        Check(form.IsTranslatable("Facade") && form.IsTranslatable("On/Off"), "ordinary display labels are not misclassified as hex or paths");
        var longText = string.Concat(Enumerable.Repeat("Hello world. ", 150)) + "$(l:" + new string('a', 900) + ")" + new string('語', 1000);
        var longParts = SplitLongText(longText);
        Check(longParts.Count > 1 && string.Concat(longParts) == longText && longParts.Any(p => p.Contains("$(l:" + new string('a', 900) + ")")), "long text split preserves every character and entire macro");
        Check(DetectSourceLanguage(["日本語の文章"]) == "ja", "Japanese detected before Han characters");
        Check(ParseLegacyLang("# comment\na= value = x \r\n") ["a"] == " value = x ", "legacy spaces and equals preserved");
        Check(ParseLangJson("\uFEFF{\"a\":\"Hello\",/*comment*/}")["a"] == "Hello", "JSON comments/BOM/trailing comma");
        Check(ParseSourceLangJson("{\"a\":\"Hello\nWorld\"\n\"b\":\"Second\"}", out var recovered)["a"] == "Hello\nWorld" && recovered, "complete nonstandard source map recovery");
        try { SafeChildPath(root, "assets/../../escape"); Check(false, "traversal rejected"); } catch (IOException) { _checks++; }
        Check(LangOutputRelative("assets/demo/lang/en_US.lang", "vi_vn") == "assets/demo/lang/vi_VN.lang", "legacy extension and case");

        var snbt = "{id:\"ABC123\" tasks:[{title:'Kill \\\'boss\\\'' type:\"kill\" count:10L}] description:[\"One\" \"Two\"] hover:[\"Hover\"] command:\"say One\"}";
        var spans = form.SelectDocumentStrings("config/ftbquests/quests/chapters/test.snbt", snbt);
        Check(spans.Count == 4 && spans[0].Value == "Kill 'boss'", "nested task titles, arrays and single quotes");
        var rewritten = ReplaceTextSpans(snbt, spans, spans.Select(s => "VI " + s.Value).ToList());
        Check(rewritten.Contains("count:10L") && rewritten.Contains("command:\"say One\""), "SNBT game data unchanged");
        Check(new StructuredTextReader(rewritten).Read().Count == new StructuredTextReader(snbt).Read().Count, "SNBT structure survives replacement");
        var ftbLang = "{quest.ABC.title:'Hello' quest.ABC.quest_desc:['First' 'Second']}";
        Check(form.SelectDocumentStrings("config/ftbquests/quests/lang/en_us.snbt", ftbLang).Count == 3, "FTB native language keys");
        Check(DocumentOutputRelative("config/ftbquests/quests/lang/en_us/chapters/a.json5", "vi_vn") == "config/ftbquests/quests/lang/vi_vn/chapters/a.json5", "split JSON5 locale path");
        var advancement = "{\"display\":{\"title\":\"Hello\",\"description\":{\"text\":\"World\"}},\"criteria\":{\"text\":\"World\"}}";
        Check(form.SelectDocumentStrings("data/demo/advancement/a.json", advancement).Count == 2, "advancement criteria excluded by path");
        var book = "{\"name\":\"Book\",\"book_texture\":\"fancy_texture\",\"text\":\"$(l:test)Read$()\"}";
        Check(form.SelectDocumentStrings("data/demo/patchouli_books/a/book.json", book).Count == 2, "book texture excluded");

        var script = "// Text.of('Ignore')\nClientEvents.lang(\"en_us\", event => { event.renameItem('demo:item', 'New Item') })\nItemEvents.tooltip(event => { event.add(['demo:item'], 'Tooltip') })\nlet pattern = /Text.of('Ignore')/; Text.of('Hello');";
        var scriptSpans = form.SelectScriptStrings(script, "vi_vn");
        Check(scriptSpans.Count == 4 && !scriptSpans.Any(s => s.Value.Contains("Ignore")), "script comments/regex/IDs excluded");
        var triumphScript = "setTitle(\"A World\")\nsetTranslatedDescription(\"quest.description\")\nsetRewardFunction(\"test:reward\")\nsetDescription(\"Hello\")";
        Check(form.SelectTriumphStrings(triumphScript).Count == 2, "Triumph display text only");
        Check(TryKnownTranslation("§4§ktralala&r§4§lDISABLED ITEM, DO NOT USE.§ktralala&r", "vi", out var warning)
            && warning == "§4§ktralala&r§4§lVẬT PHẨM ĐÃ BỊ VÔ HIỆU HÓA, KHÔNG SỬ DỤNG.§ktralala&r", "known warning meaning and obfuscation preserved");
        var scriptResult = await form.TranslateDocumentAsync("script", "kubejs/client_scripts/test.js", script, root, "vi", ct);
        Check(scriptResult.Content.Contains("\"vi_vn\"") && scriptResult.Content.Contains("'demo:item'") && scriptResult.Content.Contains("VI New Item"), "KubeJS locale and literal translation");
        var componentRaw = "{description:[\"{\\\"text\\\":\\\"Click\\\",\\\"clickEvent\\\":{\\\"action\\\":\\\"run_command\\\",\\\"value\\\":\\\"/say Hello\\\"}}\"]}";
        var componentResult = await form.TranslateDocumentAsync("component", "config/ftbquests/quests/a.snbt", componentRaw, root, "vi", ct);
        Check(componentResult.Content.Contains("VI Click") && componentResult.Content.Contains("/say Hello"), "embedded text component preserves commands");

        var first = await form.TranslateCachedAsync("cache", root, [input], ["title"], "vi", ct);
        var calls = fake.Calls;
        var second = await form.TranslateCachedAsync("cache", root, [input], ["title"], "vi", ct);
        Check(first.Errors == 0 && first.Values[0] == "§6VI " + input[2..] && fake.Calls == calls, "validated cache hit and anchored leading color");
        await form.TranslateCachedAsync("cache", root, [input + " changed"], ["title"], "vi", ct);
        Check(fake.Calls == calls + 1, "source change invalidates cache");
        fake.Fail = true;
        var failed = await form.TranslateCachedAsync("failure", root, ["Retry me"], ["title"], "vi", ct);
        Check(failed.Errors == 1 && failed.Values[0] == "Retry me", "API failure retains original");
        fake.Fail = false;
        var retried = await form.TranslateCachedAsync("failure", root, ["Retry me"], ["title"], "vi", ct);
        Check(retried.Values[0] == "VI Retry me", "failed result is retried");
        calls = fake.Calls;
        var textCount = fake.TextCount;
        var duplicate = await form.TranslateCachedAsync("duplicates", root, ["Same", "Same"], ["title", "title"], "vi", ct);
        Check(fake.Calls == calls + 1 && fake.TextCount == textCount + 1 && duplicate.Values[0] == duplicate.Values[1], "duplicate strings translated once");
        fake.CorruptTokens = true;
        Check((await form.TranslateBatchAsync([input], "vi", "auto", ct)).Errors == 1, "corrupted AI placeholders rejected");
        form._aiConfig = form._aiConfig with { FallbackToGoogle = true };
        Check((await form.TranslateBatchAsync([input], "vi", "auto", ct)).Errors == 0, "invalid AI tokens recover through Google fallback");
        form._aiConfig = form._aiConfig with { FallbackToGoogle = false };
        fake.CorruptTokens = false;

        var firstLang = await form.TranslateLangGroupAsync("source-update", root, [("en_us.json", "{\"a\":\"First\"}")], null, "vi_vn.json", "vi", ct);
        await WriteAtomicAsync(firstLang.StatePath, firstLang.StateContent, false, ct);
        var updatedLang = await form.TranslateLangGroupAsync("source-update", root, [("en_us.json", "{\"a\":\"Second\"}")], firstLang.Content, "vi_vn.json", "vi", ct);
        Check(ParseLangJson(updatedLang.Content)["a"] == "VI Second", "changed source key updates an existing machine translation");
        fake.FailText = "Broken line";
        var partialLang = await form.TranslateLangGroupAsync("partial", root, [("en_us.json", "{\"ok\":\"Good line\",\"bad\":\"Broken line\"}")], null, "vi_vn.json", "vi", ct);
        var partialMap = ParseLangJson(partialLang.Content);
        Check(partialLang.Errors == 1 && partialMap["ok"] == "VI Good line" && partialMap["bad"] == "Broken line"
            && !partialLang.StateContent.Contains("Broken line\",\"bad", StringComparison.Ordinal), "one failed string keeps the rest of the file translated");
        await WriteAtomicAsync(partialLang.StatePath, partialLang.StateContent, false, ct);
        fake.FailText = null;
        calls = fake.Calls;
        var resumedLang = await form.TranslateLangGroupAsync("partial", root, [("en_us.json", "{\"ok\":\"Good line\",\"bad\":\"Broken line\"}")], partialLang.Content, "vi_vn.json", "vi", ct);
        Check(resumedLang.Errors == 0 && ParseLangJson(resumedLang.Content)["bad"] == "VI Broken line" && fake.Calls == calls + 1, "rerun retries only the failed string");
        fake.RateLimitResponses = 2;
        var limited = await form.TranslateBatchAsync(["Rate limited text"], "vi", "auto", ct);
        Check(limited.Errors == 0 && limited.Values[0] == "VI Rate limited text" && fake.RateLimitResponses == 0, "AI 429 waits for Retry-After and retries");
        var aiConfig = form._aiConfig;
        form._aiConfig = null;
        fake.RateLimitResponses = GoogleMaxStrikes;
        calls = fake.Calls;
        var blocked = await form.TranslateBatchAsync(["Blocked one", "Blocked two"], "vi", "auto", ct);
        Check(blocked.Errors == 2 && blocked.Values[1] == "Blocked two" && fake.Calls == calls + GoogleMaxStrikes, "repeated Google 429 stops further Google calls");
        form._googleStrikes = 0;
        Check((await form.TranslateBatchAsync(["Unblocked"], "vi", "auto", ct)).Values[0] == "VI Unblocked", "Google resumes after strike reset");
        form._aiConfig = aiConfig;
        var manualLang = await form.TranslateLangGroupAsync("source-update", root, [("en_us.json", "{\"a\":\"Second\"}")], "{\"a\":\"Bản dịch thủ công\"}", "vi_vn.json", "vi", ct);
        Check(ParseLangJson(manualLang.Content)["a"] == "Bản dịch thủ công", "manually edited target preserved");

        var instance = Path.Combine(root, "instance");
        Directory.CreateDirectory(Path.Combine(instance, "mods"));
        void Jar(string file, Dictionary<string, string> content)
        {
            using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
            foreach (var pair in content) { using var writer = new StreamWriter(zip.CreateEntry(pair.Key).Open(), Utf8NoBom); writer.Write(pair.Value); }
        }
        Jar(Path.Combine(instance, "mods", "one.jar"), new() { ["assets/shared/lang/en_us.json"] = "{\"same\":\"First\"}", ["assets/shared/lang/zh_cn.json"] = "{\"extra\":\"额外\"}", ["assets/shared/lang/ko_kr.json"] = "broken" });
        Jar(Path.Combine(instance, "mods", "two.jar"), new() { ["assets/shared/lang/en_us.json"] = "{\"same\":\"Second\"}" });
        Jar(Path.Combine(instance, "mods", "legacy.jar"), new() { ["assets/old/lang/en_US.lang"] = "a=Old text\nnum=123\n" });
        var nested = Path.Combine(root, "inner.jar");
        Jar(nested, new() { ["assets/nested/lang/en_us.json"] = "{\"a\":\"Nested\"}" });
        using (var outer = ZipFile.Open(Path.Combine(instance, "mods", "outer.jar"), ZipArchiveMode.Create)) outer.CreateEntryFromFile(nested, "META-INF/jarjar/inner.jar");
        Jar(Path.Combine(instance, "mods", "signed.jar"), new() { ["META-INF/TEST.SF"] = "signature", ["assets/signed/lang/en_us.json"] = "{\"a\":\"Signed\"}" });
        var guidePage = "---\nnavigation:\n  parent: index.md\n  title: Quantum Bridge\n---\n\n# The Bridge\n\nSee [the network](../net.md) for `ae2:cable` details.\n\n```\ncode stays\n```\n<GameScene zoom=\"4\" />\n";
        Jar(Path.Combine(instance, "mods", "books.jar"), new()
        {
            ["assets/immersiveengineering/manual/en_us/wire.txt"] = "Redstone Wire\nGetting started\n<&coil>Wires carry signals.<np>\n<NEWLINE>\n",
            ["assets/citadel/book/dict/en_us/turtle.txt"] = "A slow turtle.\n",
            ["assets/citadel/book/dict/vi_vn/turtle.txt"] = "Bản dịch chính thức.\n",
            ["assets/ae2/ae2guide/bridge.md"] = guidePage,
            ["assets/ae2/ae2guide/_es_es/bridge.md"] = "Puente\n",
        });
        var signedHash = await HashFileAsync(Path.Combine(instance, "mods", "signed.jar"), ct);
        var questPath = Path.Combine(instance, "config", "ftbquests", "quests", "chapters", "test.snbt");
        Directory.CreateDirectory(Path.GetDirectoryName(questPath)!);
        await File.WriteAllTextAsync(questPath, snbt, ct);
        await form.RunTranslateAsync(instance, "vi", "vi_vn", ct);
        string ReadJar(string name, string entry)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(instance, "mods", name));
            using var reader = new StreamReader(zip.GetEntry(entry)!.Open());
            return reader.ReadToEnd();
        }
        Check(ReadJar("one.jar", "assets/shared/lang/vi_vn.json").Contains("VI First") && ReadJar("two.jar", "assets/shared/lang/vi_vn.json").Contains("VI Second"), "same namespace jars remain isolated");
        Check(ReadJar("one.jar", "assets/shared/lang/vi_vn.json").Contains("extra"), "fallback-only language keys merged");
        Check(ReadJar("legacy.jar", "assets/old/lang/vi_VN.lang").Contains("num=123"), "legacy JAR output retained");
        Check(await HashFileAsync(Path.Combine(instance, "mods", "signed.jar"), ct) == signedHash, "signed archive unchanged");
        var manualPage = ReadJar("books.jar", "assets/immersiveengineering/manual/vi_vn/wire.txt");
        Check(manualPage == "VI Redstone Wire\nVI Getting started\nVI <&coil>Wires carry signals.<np>\n<NEWLINE>\n", "IE manual page translated per line with tags kept");
        Check(ReadJar("books.jar", "assets/citadel/book/dict/vi_vn/turtle.txt") == "Bản dịch chính thức.\n", "official book translation kept");
        var guideVi = ReadJar("books.jar", "assets/ae2/ae2guide/_vi_vn/bridge.md");
        Check(guideVi.Contains("  title: VI Quantum Bridge") && guideVi.Contains("  parent: index.md") && guideVi.Contains("# VI The Bridge")
            && guideVi.Contains("](../net.md)") && guideVi.Contains("`ae2:cable`") && guideVi.Contains("code stays") && guideVi.Contains("<GameScene zoom=\"4\" />")
            && !guideVi.Contains("VI code") && !guideVi.Contains("VI <Game"), "GuideME page: text translated, front matter keys, links, code and tags kept");
        Check(PlainTextOutputRelative("assets/ae2/ae2guide/_es_es/bridge.md", "vi_vn") is null, "GuideME translations are not used as sources");
        using (var outer = ZipFile.OpenRead(Path.Combine(instance, "mods", "outer.jar")))
        using (var stream = outer.GetEntry("META-INF/jarjar/inner.jar")!.Open())
        using (var inner = new ZipArchive(stream, ZipArchiveMode.Read)) Check(inner.GetEntry("assets/nested/lang/vi_vn.json") is not null, "nested archive translated");
        Check((await File.ReadAllTextAsync(questPath, ct)).Contains("VI Kill") && File.Exists(questPath + ".beeslator.bak"), "FTB literal values written with backup");
        var questHash = await HashFileAsync(questPath, ct);
        await form.RunTranslateAsync(instance, "vi", "vi_vn", ct);
        Check(await HashFileAsync(questPath, ct) == questHash, "in-place translation is idempotent");
        form._resetTranslations = true;
        await form.RunTranslateAsync(instance, "vi", "vi_vn", ct);
        Check(await HashFileAsync(questPath, ct) == questHash, "reset reuses original quest text");
        form._resetTranslations = false;
        var archiveHash = await HashFileAsync(Path.Combine(instance, "mods", "one.jar"), ct);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await form.ProcessArchiveAsync(Path.Combine(instance, "mods", "one.jar"), "cancel", root, "vi", "vi_vn", 0, canceled.Token); }
            catch (OperationCanceledException) { }
        }
        Check(await HashFileAsync(Path.Combine(instance, "mods", "one.jar"), ct) == archiveHash, "cancellation preserves original archive");
        var midCancel = Path.Combine(instance, "mods", "mid-cancel.jar");
        Jar(midCancel, new() { ["assets/cancel/lang/en_us.json"] = "{\"a\":\"Cancel while translating\"}" });
        var midHash = await HashFileAsync(midCancel, ct);
        using (var cancellation = new CancellationTokenSource())
        {
            fake.BeforeResponse = cancellation.Cancel;
            try { await form.ProcessArchiveAsync(midCancel, "mid-cancel", root, "vi", "vi_vn", 0, cancellation.Token); }
            catch (OperationCanceledException) { }
            finally { fake.BeforeResponse = null; }
        }
        Check(await HashFileAsync(midCancel, ct) == midHash && !File.Exists(midCancel + ".beeslator.bak"), "cancellation during translation leaves original bytes intact");
        Console.WriteLine($"PASS: {_checks} regression checks. Fixtures: {root}");

        if (args.Length > 0) await form.AuditPackAsync(args[0], root, ct);
    }

    // Copies the named mod jars into a scratch instance, runs a real translation (Google or the user's AI config)
    // and reports how many source lang keys got a target-language value. The source instance is never written.
    private static async Task RunPackSampleAsync(string sourceInstance, string[] jarNames)
    {
        using var form = new Form1(new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
        SynchronizationContext.SetSynchronizationContext(null);
        form._aiConfig = LoadAiTranslatorConfig();
        var root = Path.GetFullPath(Path.Combine("obj", "pack-sample", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        foreach (var name in jarNames) File.Copy(Path.Combine(sourceInstance, "mods", name), Path.Combine(root, "mods", name));
        Console.WriteLine($"Pack sample: {root} ({(form._aiConfig is null ? "Google" : form._aiConfig.Provider.ToString())})");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await form.RunTranslateAsync(root, "vi", "vi_vn", CancellationToken.None);
        Console.WriteLine($"Run: {watch.Elapsed:mm\\:ss}, errors {form._errorCount}");
        foreach (var group in form._coverage.ToArray().GroupBy(e => e.Status))
            Console.WriteLine($"  {group.Key}: {group.Count()} entries, {group.Sum(e => e.Strings)} strings, {group.Sum(e => e.Unchanged)} unchanged");
        foreach (var e in form._coverage.ToArray().Where(e => e.Status is "error" or "translation-errors").Take(10))
            Console.WriteLine($"  ! {e.Source}: {e.Detail}");
        foreach (var name in jarNames)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(root, "mods", name));
            var source = 0; var target = 0; var same = 0;
            foreach (var entry in zip.Entries.Where(e => LangOutputRelative(e.FullName, "vi_vn") is not null && e.FullName.Contains("/en_us.", StringComparison.OrdinalIgnoreCase)))
            {
                using var reader = new StreamReader(entry.Open());
                var src = ParseLangJson(await reader.ReadToEndAsync());
                var outEntry = zip.GetEntry(LangOutputRelative(entry.FullName, "vi_vn")!);
                Dictionary<string, string> vi = [];
                if (outEntry is not null) { using var r2 = new StreamReader(outEntry.Open()); vi = ParseLangJson(await r2.ReadToEndAsync()); }
                source += src.Count;
                target += src.Keys.Count(vi.ContainsKey);
                same += src.Count(p => vi.TryGetValue(p.Key, out var v) && v == p.Value && form.IsTranslatable(p.Value));
            }
            Console.WriteLine($"  {name}: {target}/{source} keys written, {same} still English");
        }
    }

    private static async Task RunLiveSampleAsync(string sourceInstance)
    {
        using var form = new Form1(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        SynchronizationContext.SetSynchronizationContext(null);
        var root = Path.GetFullPath(Path.Combine("obj", "live-sample", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var sourceFiles = new List<string>
        {
            Path.Combine(sourceInstance, "kubejs", "client_scripts", "lang.js"),
            Path.Combine(sourceInstance, "kubejs", "client_scripts", "tooltip_additions.js")
        };
        var chapterDir = Path.Combine(sourceInstance, "config", "ftbquests", "quests", "chapters");
        foreach (var file in Directory.EnumerateFiles(chapterDir, "*.snbt").OrderBy(f => new FileInfo(f).Length))
        {
            var relative = NormalizePath(Path.GetRelativePath(sourceInstance, file));
            var strings = form.SelectDocumentStrings(relative, await File.ReadAllTextAsync(file));
            if (strings.Count is >= 3 and <= 12) { sourceFiles.Add(file); break; }
        }
        var hashes = new Dictionary<string, string>();
        foreach (var file in sourceFiles)
        {
            hashes[file] = await HashFileAsync(file, CancellationToken.None);
            var target = SafeChildPath(root, NormalizePath(Path.GetRelativePath(sourceInstance, file)));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        Console.WriteLine("Live sample copy: " + root);
        await form.RunTranslateAsync(root, "vi", "vi_vn", CancellationToken.None);
        foreach (var file in sourceFiles) Check(hashes[file] == await HashFileAsync(file, CancellationToken.None), "original sample unchanged");
        var report = form._coverage.ToArray();
        Console.WriteLine($"LIVE: {sourceFiles.Count} copied files, {report.Sum(r => r.Strings)} text strings, {form._errorCount} errors. Source files unchanged.");
        if (form._errorCount > 0) throw new Exception("Live translation failed; inspect the coverage report.");
    }

    private async Task AuditPackAsync(string instance, string output, CancellationToken ct)
    {
        var files = File.Exists(instance) ? new[] { instance } : FindContentFiles(instance).ToArray();
        var report = new List<object>();
        var totalStrings = 0;
        var parseErrors = 0;
        foreach (var path in files.Where(f => !IsArchive(f)))
        {
            var relative = NormalizePath(Path.GetRelativePath(instance, path));
            if (DocumentOutputRelative(relative, "vi_vn") is null) continue;
            try
            {
                var raw = await File.ReadAllTextAsync(path, ct);
                var strings = path.EndsWith(".js") ? SelectScriptStrings(raw, "vi_vn")
                    : relative.Contains("triumph/script/") ? SelectTriumphStrings(raw) : SelectDocumentStrings(relative, raw);
                var roundTrip = ReplaceTextSpans(raw, strings, strings.Select(s => s.Value).ToList());
                Check(roundTrip == raw, "real document round-trip: " + relative);
                totalStrings += strings.Count;
                report.Add(new { File = relative, Strings = strings.Count, Status = "parsed" });
            }
            catch (Exception ex) { parseErrors++; report.Add(new { File = relative, Status = "unsupported-or-error", Error = ex.Message }); }
        }
        var jars = 0;
        var nestedJars = 0;
        var langFiles = 0;
        var langKeys = 0;
        void AuditArchive(ZipArchive zip, string source, int depth)
        {
            if (depth > 6) return;
            foreach (var entry in zip.Entries)
            {
                if (LangOutputRelative(entry.FullName, "vi_vn") is not null)
                {
                    try
                    {
                        using var reader = new StreamReader(entry.Open());
                        var raw = reader.ReadToEnd();
                        var map = entry.FullName.EndsWith(".lang", StringComparison.OrdinalIgnoreCase) ? ParseLegacyLang(raw) : ParseSourceLangJson(raw, out _);
                        langFiles++; langKeys += map.Count;
                    }
                    catch (Exception ex) { parseErrors++; report.Add(new { File = source + "!" + entry.FullName, Status = "parse-error", Error = ex.Message }); }
                }
                else if (IsArchive(entry.FullName) && entry.Length < MaxNestedArchiveBytes)
                {
                    try { using var stream = entry.Open(); using var nestedZip = new ZipArchive(stream); nestedJars++; AuditArchive(nestedZip, source + "!" + entry.FullName, depth + 1); }
                    catch (InvalidDataException) { parseErrors++; }
                }
                else if (DocumentOutputRelative(entry.FullName, "vi_vn") is not null && entry.Length < MaxTextBytes)
                {
                    try
                    {
                        using var reader = new StreamReader(entry.Open());
                        var raw = reader.ReadToEnd();
                        var strings = entry.FullName.Contains("triumph/script/") ? SelectTriumphStrings(raw)
                            : entry.FullName.EndsWith(".js") ? SelectScriptStrings(raw, "vi_vn") : SelectDocumentStrings(entry.FullName, raw);
                        totalStrings += strings.Count;
                        report.Add(new { File = source + "!" + entry.FullName, Strings = strings.Count, Status = "parsed" });
                    }
                    catch (Exception ex) { parseErrors++; report.Add(new { File = source + "!" + entry.FullName, Status = "parse-error", Error = ex.Message }); }
                }
            }
        }
        foreach (var path in files.Where(IsArchive))
        {
            try { using var zip = ZipFile.OpenRead(path); jars++; AuditArchive(zip, Path.GetFileName(path), 0); }
            catch (InvalidDataException) { parseErrors++; }
        }
        var summary = new { Instance = instance, Archives = jars, NestedArchives = nestedJars, LanguageFiles = langFiles, LanguageKeys = langKeys, DocumentStrings = totalStrings, ParseIssues = parseErrors, Results = report };
        var reportPath = Path.Combine(output, "real-pack-audit.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(summary, JsonOptions(true)), ct);
        Console.WriteLine($"REAL PACK: {jars} archives, {nestedJars} nested, {langFiles} lang files/{langKeys} keys, {totalStrings} document strings, {parseErrors} issues. Report: {reportPath}");
    }
}
