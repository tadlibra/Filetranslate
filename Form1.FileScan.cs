using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Filetranslate;

public partial class Form1
{
    private static readonly string[] ContentRoots =
    [
        "mods", "assets", "data", "resources", "resourcepacks", "datapacks", "kubejs",
        "config", "defaultconfigs", "patchouli_books", "openloader", "global_packs",
        "moonlight-global-datapacks", "paxi", "scripts"
    ];

    private IEnumerable<string> FindContentFiles(string instancePath)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        foreach (var name in ContentRoots)
        {
            var root = Path.Combine(instancePath, name);
            if (!Directory.Exists(root)) continue;
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                _coverage.Enqueue(new(root, "manual-review", 0, 0, "Linked content directory was not traversed."));
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                var relative = NormalizePath(Path.GetRelativePath(instancePath, file));
                if (relative.Split('/').Any(p => p.Equals(ResourcePackName, StringComparison.OrdinalIgnoreCase) || p.StartsWith(".beeslator", StringComparison.OrdinalIgnoreCase))) continue;
                if (file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                yield return file;
            }
        }
        var saves = Path.Combine(instancePath, "saves");
        if (!Directory.Exists(saves)) yield break;
        foreach (var world in Directory.EnumerateDirectories(saves))
        {
            if ((File.GetAttributes(world) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var folder in new[] { "datapacks", "serverconfig/ftbquests" })
            {
                var root = Path.Combine(world, folder);
                if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*", options))
                    if (!file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) yield return file;
            }
        }
    }

    private static bool IsArchive(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jar" or ".zip";

    private static int SourceRank(string path)
    {
        var match = Regex.Match(NormalizePath(path), @"/lang/([a-z]{2,3}_[a-z]{2})(?:/|\.)", RegexOptions.IgnoreCase);
        var name = (match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(path)).ToLowerInvariant();
        return name switch { "en_us" => 0, "en_gb" => 1, "zh_cn" => 2, "zh_tw" => 3, "ja_jp" => 4, "ko_kr" => 5, _ => 10 };
    }

    private static string? LangOutputRelative(string path, string langFile)
    {
        path = NormalizePath(path);
        var match = Regex.Match(path, @"^(.*(?:^|/)lang/)([a-z]{2,3}_[a-z]{2})(\.json|\.lang)$", RegexOptions.IgnoreCase);
        if (!match.Success || match.Groups[2].Value.Equals(langFile, StringComparison.OrdinalIgnoreCase)) return null;
        // FTB's own localization is handled as a structured document, not a flat Minecraft lang map.
        if (IsFtbLocale(path)) return null;
        var locale = match.Groups[3].Value.Equals(".lang", StringComparison.OrdinalIgnoreCase)
            && match.Groups[2].Value.Any(char.IsUpper) ? langFile[..3] + langFile[3..].ToUpperInvariant() : langFile;
        return match.Groups[1].Value + locale + match.Groups[3].Value;
    }

    private static bool IsFtbLocale(string path) => Regex.IsMatch(NormalizePath(path),
        @"(?:^|/)ftbquests/[^\r\n]*/lang/(?:[a-z]{2,3}_[a-z]{2}(?:/|\.))", RegexOptions.IgnoreCase);

    private static string? DocumentOutputRelative(string path, string langFile)
    {
        path = NormalizePath(path);
        var lower = path.ToLowerInvariant();
        if ((lower.StartsWith("kubejs/") || lower.Contains("/kubejs/")) && lower.EndsWith(".js")) return path;
        if (lower.Contains("triumph/script/") && lower.EndsWith(".txt")) return path;
        if (IsFtbLocale(path))
        {
            var match = Regex.Match(path, @"(/lang/)([a-z]{2,3}_[a-z]{2})(?=/|\.)", RegexOptions.IgnoreCase);
            if (!match.Success || match.Groups[2].Value.Equals(langFile, StringComparison.OrdinalIgnoreCase)) return null;
            return path[..match.Groups[2].Index] + langFile + path[(match.Groups[2].Index + match.Groups[2].Length)..];
        }
        if (lower.Contains("/patchouli_books/") || lower.StartsWith("patchouli_books/"))
        {
            if (!lower.EndsWith(".json")) return null;
            if (lower.EndsWith("/book.json")) return path;
            return Regex.IsMatch(path, @"/en_us/", RegexOptions.IgnoreCase)
                ? Regex.Replace(path, @"/en_us/", "/" + langFile + "/", RegexOptions.IgnoreCase) : null;
        }
        if (lower.Contains("ftbquests/") && !lower.Contains("/lang/") && Path.GetExtension(lower) is ".snbt" or ".json5") return path;
        if (lower.Contains("betterquesting/") && lower.EndsWith(".json")) return path;
        if ((lower.Contains("/advancements/") || lower.Contains("/advancement/") || lower.Contains("triumph/")) && lower.EndsWith(".json")) return path;
        return null;
    }

    private static bool IsReviewCandidate(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".js" or ".zs" or ".json5" or ".snbt" or ".json" or ".txt" or ".cfg" or ".toml" or ".yml" or ".yaml" or ".md";
    }

    private static bool HasJarSignature(ZipArchive zip) => zip.Entries.Any(e =>
        e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)
        && Path.GetExtension(e.FullName).ToUpperInvariant() is ".SF" or ".RSA" or ".DSA" or ".EC");
}

