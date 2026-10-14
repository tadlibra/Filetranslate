using System.Text.Json;
using System.Text.Json.Nodes;

namespace Filetranslate;

public partial class Form1
{
    private sealed record LangCheckpoint(Dictionary<string, string> Sources, Dictionary<string, string> Values);
    private sealed record DocumentCheckpoint(string Original, string OutputHash);

    private async Task<(string Content, int Errors, string StatePath, string StateContent)> TranslateLangGroupAsync(string identity, string staging,
        IReadOnlyList<(string Name, string Content)> sources, string? existingContent, string targetPath, string langCode, CancellationToken ct)
    {
        var legacy = targetPath.EndsWith(".lang", StringComparison.OrdinalIgnoreCase);
        Dictionary<string, string> Parse(string raw) => legacy ? ParseLegacyLang(raw) : ParseLangJson(raw);
        var sourceMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources.OrderBy(x => SourceRank(x.Name)).ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            try
            {
                var recovered = false;
                var map = legacy ? ParseLegacyLang(source.Content) : ParseSourceLangJson(source.Content, out recovered);
                foreach (var pair in map) sourceMap.TryAdd(pair.Key, pair.Value);
                if (recovered) _coverage.Enqueue(new(identity + "!" + source.Name, "source-recovered", map.Count, 0, "Recovered a complete flat string map from nonstandard JSON."));
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
            {
                Interlocked.Increment(ref _errorCount);
                _coverage.Enqueue(new(identity + "!" + source.Name, "source-error", 0, 0, ex.Message));
            }
        }
        if (sourceMap.Count == 0) throw new FormatException("No usable language strings in this source group.");

        var existing = existingContent is null ? new Dictionary<string, string>(StringComparer.Ordinal) : Parse(existingContent);
        var checkpointPath = SafeChildPath(staging, "lang-state/" + ContentHash(identity) + ".json");
        LangCheckpoint? checkpoint = null;
        if (File.Exists(checkpointPath))
        {
            try { checkpoint = JsonSerializer.Deserialize<LangCheckpoint>(await File.ReadAllTextAsync(checkpointPath, ct)); }
            catch (JsonException) { /* Rebuild an interrupted/old checkpoint from source. */ }
        }
        var translated = new Dictionary<string, string>(existing, StringComparer.Ordinal);
        var missing = new List<KeyValuePair<string, string>>();
        foreach (var pair in sourceMap)
        {
            if (!IsTranslatable(pair.Value)) { translated[pair.Key] = pair.Value; continue; }
            if (!_resetTranslations && existing.TryGetValue(pair.Key, out var value) && value != pair.Value
                && (checkpoint is null || !checkpoint.Values.TryGetValue(pair.Key, out var old) || old != value
                    || checkpoint.Sources.GetValueOrDefault(pair.Key) == pair.Value))
                continue;
            missing.Add(pair);
        }
        var result = await TranslateCachedAsync(identity, staging, missing.Select(x => x.Value).ToList(),
            missing.Select(x => x.Key).ToList(), langCode, ct);
        for (var i = 0; i < missing.Count; i++) translated[missing[i].Key] = result.Values[i];
        // Failed strings are never put in the successful checkpoint.
        var failedKeys = missing.Where((_, i) => result.FailedIndices?.Contains(i) == true).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var successful = translated.Where(x => !failedKeys.Contains(x.Key))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var stateContent = JsonSerializer.Serialize(new LangCheckpoint(sourceMap, successful), JsonOptions(false));
        var content = legacy ? string.Join("\n", translated.Select(x => x.Key + "=" + x.Value.Replace("\r", "\\r").Replace("\n", "\\n"))) + "\n"
            : SerializeJsonMap(translated);
        return (content, result.Errors, checkpointPath, stateContent);
    }

    private List<TextSpan> SelectDocumentStrings(string path, string raw)
    {
        var spans = new StructuredTextReader(raw).Read();
        var lower = NormalizePath(path).ToLowerInvariant();
        if (IsFtbLocale(path)) return spans.Where(s => IsTranslatable(s.Value) && !IsTranslationKey(s.Value)).ToList();
        if (lower.Contains("ftbquests/")) return spans.Where(s =>
            s.Field is "title" or "subtitle" or "description" or "text" or "hover" or "tooltip" && IsTranslatable(s.Value) && !IsTranslationKey(s.Value)).ToList();
        if (lower.Contains("betterquesting/")) return spans.Where(s =>
            s.Field is "name:8" or "desc:8" && IsTranslatable(s.Value) && !IsTranslationKey(s.Value)).ToList();
        if (lower.Contains("patchouli_books/")) return spans.Where(s => IsPatchouliTextField(s.Field, s.Value)).ToList();

        // Only advancement display components are text; criteria and commands are gameplay data.
        var node = JsonNode.Parse(raw.TrimStart('\uFEFF'), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var display = node?["display"];
        if (display is null) return [];
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        void AddComponent(JsonNode? component)
        {
            if (component is JsonValue value && value.TryGetValue<string>(out var text)) allowed.Add(text);
            else if (component is JsonArray array) { foreach (var item in array) AddComponent(item); }
            else if (component is JsonObject obj)
            {
                if (obj["text"] is JsonValue literal && literal.TryGetValue<string>(out var literalText)) allowed.Add(literalText);
                AddComponent(obj["extra"]);
                AddComponent(obj["with"]);
            }
        }
        AddComponent(display["title"]);
        AddComponent(display["description"]);
        return spans.Where(s => (s.Context == "/display/title" || s.Context == "/display/description"
                || s.Context.StartsWith("/display/title/", StringComparison.Ordinal) || s.Context.StartsWith("/display/title[]", StringComparison.Ordinal)
                || s.Context.StartsWith("/display/description/", StringComparison.Ordinal) || s.Context.StartsWith("/display/description[]", StringComparison.Ordinal))
            && s.Field is "title" or "description" or "text" or "extra" or "with"
            && allowed.Contains(s.Value) && IsTranslatable(s.Value) && !IsTranslationKey(s.Value)).ToList();
    }

    private async Task<(string Content, int Errors)> TranslateDocumentAsync(string identity, string path, string raw,
        string staging, string langCode, CancellationToken ct)
    {
        var triumph = NormalizePath(path).Contains("triumph/script/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        var script = path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || triumph;
        var allTargets = triumph ? SelectTriumphStrings(raw) : script ? SelectScriptStrings(raw, "vi_vn") : SelectDocumentStrings(path, raw);
        var targets = allTargets.Where(t => t.Field != "__target_locale").ToList();
        var values = targets.Select(x => x.Value).ToList();
        var errors = 0;
        var plain = new List<int>();
        for (var i = 0; i < targets.Count; i++)
        {
            var value = targets[i].Value.Trim();
            if (value.StartsWith('{') || value.StartsWith('['))
            {
                try
                {
                    using var parsed = JsonDocument.Parse(value);
                    var embedded = new StructuredTextReader(targets[i].Value).Read()
                        .Where(s => s.Field is "text" or "extra" or "with" && !s.Context.Contains("/clickEvent/")
                            && IsTranslatable(s.Value) && !IsTranslationKey(s.Value)).ToList();
                    var translated = await TranslateCachedAsync(identity + ":component:" + i, staging,
                        embedded.Select(s => s.Value).ToList(), embedded.Select(s => s.Context).ToList(), langCode, ct);
                    values[i] = ReplaceTextSpans(targets[i].Value, embedded, translated.Values);
                    errors += translated.Errors;
                    continue;
                }
                catch (JsonException) { }
            }
            plain.Add(i);
        }
        var result = await TranslateCachedAsync(identity, staging, plain.Select(i => targets[i].Value).ToList(),
            plain.Select(i => path + ":" + targets[i].Field).ToList(), langCode, ct);
        for (var i = 0; i < plain.Count; i++) values[plain[i]] = result.Values[i];
        var replacements = targets.Select((t, i) => (Span: t, Value: values[i]))
            .Concat(allTargets.Where(t => t.Field == "__target_locale").Select(t => (Span: t, Value: "vi_vn")))
            .OrderBy(x => x.Span.Start).ToList();
        var content = ReplaceTextSpans(raw, replacements.Select(x => x.Span).ToList(), replacements.Select(x => x.Value).ToList());
        if (script)
        {
            _ = ReadScriptTokens(content);
            _coverage.Enqueue(new(identity, "manual-review", 0, 0, "Script: only supported literal text calls were handled; dynamic expressions need review."));
        }
        else _ = new StructuredTextReader(content).Read();
        return (content, errors + result.Errors);
    }

    private async Task<(string Original, string StatePath)> ReadDocumentOriginalAsync(string identity, string staging, string current, CancellationToken ct)
    {
        var statePath = SafeChildPath(staging, "document-state/" + ContentHash(identity) + ".json");
        if (File.Exists(statePath))
        {
            try
            {
                var state = JsonSerializer.Deserialize<DocumentCheckpoint>(await File.ReadAllTextAsync(statePath, ct));
                if (state is not null && state.OutputHash == ContentHash(current)) return (state.Original, statePath);
            }
            catch (JsonException) { }
        }
        return (current, statePath);
    }

    private static Task SaveDocumentStateAsync(string path, string original, string output, CancellationToken ct) =>
        WriteAtomicAsync(path, JsonSerializer.Serialize(new DocumentCheckpoint(original, ContentHash(output))), false, ct);
}

