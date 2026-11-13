using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Filetranslate;

// Official Minecraft terminology, read at run time from the player's own Minecraft install (never shipped
// with the app: Mojang's language files may not be redistributed). Exact matches are used directly;
// terms found inside a string are listed in the AI prompt so the model uses the in-game wording.
public partial class Form1
{
    private Dictionary<string, string>? _glossary;
    private bool _loadVanillaGlossary = true; // regression tests turn this off to stay independent of the machine
    private const int MaxPromptTerms = 30;

    private static IEnumerable<string> MinecraftDataRoots(string instancePath)
    {
        // Instances usually live under the launcher's data folder, so walk up from the instance first.
        for (var dir = new DirectoryInfo(instancePath); dir is not null; dir = dir.Parent)
            yield return dir.FullName;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in new[]
                 {
                     Path.Combine(appData, ".minecraft"), Path.Combine(appData, "PrismLauncher"), Path.Combine(appData, "ModrinthApp", "meta"),
                     Path.Combine(appData, "com.modrinth.theseus", "meta"), Path.Combine(home, "curseforge", "minecraft", "Install"),
                     Path.Combine(appData, "ATLauncher"), Path.Combine(appData, "gdlauncher_next"), Path.Combine(appData, "MultiMC"),
                 })
            yield return root;
    }

    // Newest asset index that lists the target language and whose object file is present.
    private static string? FindVanillaTargetLang(IEnumerable<string> roots, string langFile)
    {
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var indexes = Path.Combine(root, "assets", "indexes");
            if (!Directory.Exists(indexes)) continue;
            foreach (var index in new DirectoryInfo(indexes).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(index.FullName));
                    if (!doc.RootElement.TryGetProperty("objects", out var objects)
                        || !objects.TryGetProperty($"minecraft/lang/{langFile}.json", out var entry)) continue;
                    var hash = entry.GetProperty("hash").GetString();
                    if (hash is null || hash.Length < 2) continue;
                    var file = Path.Combine(root, "assets", "objects", hash[..2], hash);
                    if (File.Exists(file)) return File.ReadAllText(file);
                }
                catch (Exception ex) when (ex is JsonException or IOException or KeyNotFoundException or InvalidOperationException) { }
            }
        }
        return null;
    }

    // en_us ships inside the client jar: versions/<v>/<v>.jar (official, Modrinth, CurseForge) or Prism's libraries path.
    private static string? FindVanillaSourceLang(IEnumerable<string> roots)
    {
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var jars = new List<FileInfo>();
            var versions = Path.Combine(root, "versions");
            if (Directory.Exists(versions))
                foreach (var dir in Directory.EnumerateDirectories(versions))
                {
                    var jar = new FileInfo(Path.Combine(dir, Path.GetFileName(dir) + ".jar"));
                    if (jar.Exists && jar.Length > 5_000_000) jars.Add(jar);
                }
            var prism = Path.Combine(root, "libraries", "com", "mojang", "minecraft");
            if (Directory.Exists(prism))
                jars.AddRange(new DirectoryInfo(prism).EnumerateFiles("minecraft-*-client.jar", SearchOption.AllDirectories));
            foreach (var jar in jars.OrderByDescending(j => j.LastWriteTimeUtc))
            {
                try
                {
                    using var zip = ZipFile.OpenRead(jar.FullName);
                    if (zip.GetEntry("assets/minecraft/lang/en_us.json") is not { } entry) continue;
                    using var reader = new StreamReader(entry.Open());
                    return reader.ReadToEnd();
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
            }
        }
        return null;
    }

    private static Dictionary<string, string> BuildGlossary(string sourceJson, string targetJson)
    {
        var source = ParseLangJson(sourceJson);
        var target = ParseLangJson(targetJson);
        var glossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, english) in source)
        {
            if (!target.TryGetValue(key, out var translated) || translated == english) continue;
            // Only plain names/phrases: placeholders and formatting belong to their own key's grammar.
            if (english.Length > 60 || english.Contains('%') || english.Contains('§') || english.Contains('\n')) continue;
            if (!english.Any(char.IsLetter)) continue;
            // An English word with two different official translations (e.g. menus vs items) is ambiguous: drop it.
            if (glossary.TryGetValue(english, out var existing) && existing != translated) { conflicts.Add(english); continue; }
            glossary[english] = translated;
        }
        foreach (var term in conflicts) glossary.Remove(term);
        return glossary;
    }

    private Dictionary<string, string>? LoadVanillaGlossary(string instancePath, string langFile)
    {
        try
        {
            var roots = MinecraftDataRoots(instancePath).ToList();
            var target = FindVanillaTargetLang(roots, langFile);
            var source = target is null ? null : FindVanillaSourceLang(roots);
            if (source is null || target is null)
            {
                Log("Không tìm thấy file ngôn ngữ Minecraft gốc trên máy: dịch không có từ điển thuật ngữ chính thức. Mở game một lần với launcher để tải về.");
                return null;
            }
            var glossary = BuildGlossary(source, target);
            Log($"Từ điển thuật ngữ Minecraft chính thức: {glossary.Count} mục.");
            return glossary;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log($"Không đọc được từ điển Minecraft: {ex.Message}");
            return null;
        }
    }

    private bool TryGlossaryTranslation(string text, out string translated)
    {
        translated = text;
        if (_glossary is null) return false;
        var envelope = SplitFormatting(text);
        if (envelope.Body.Length == 0 || !_glossary.TryGetValue(envelope.Body, out var value)) return false;
        if (envelope.Body.Any(char.IsLetter) && envelope.Body == envelope.Body.ToUpperInvariant()) value = value.ToUpperInvariant();
        translated = envelope.Prefix + value + envelope.Suffix;
        return true;
    }

    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}'’-]+", RegexOptions.Compiled);

    // Official terms (1-4 words) that occur in the batch, longest first, so "Tube Coral" wins over "Coral".
    private string? GlossaryHint(IEnumerable<string> texts)
    {
        if (_glossary is null || _glossary.Count == 0) return null;
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in texts)
        {
            var words = WordRegex.Matches(text).Select(m => m.Value).ToArray();
            for (var size = 4; size >= 1; size--)
                for (var i = 0; i + size <= words.Length; i++)
                {
                    var phrase = string.Join(' ', words, i, size);
                    if (phrase.Length < 3 || found.ContainsKey(phrase)) continue;
                    if (_glossary.TryGetValue(phrase, out var vi)) found[phrase] = vi;
                }
        }
        if (found.Count == 0) return null;
        return string.Join("; ", found.OrderByDescending(p => p.Key.Length).Take(MaxPromptTerms).Select(p => $"{p.Key} = {p.Value}"));
    }
}
