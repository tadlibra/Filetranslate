using System.Text.RegularExpressions;

namespace Filetranslate;

// Line-based guide pages that live next to their translations instead of in a lang map:
// - Immersive Engineering manual and Citadel books: assets/<ns>/{manual|book}/.../en_us/<page>.txt -> .../<lang>/<page>.txt
// - GuideME (AE2 and addons): assets/<ns>/ae2guide/<page>.md -> assets/<ns>/ae2guide/_<lang>/<page>.md
public partial class Form1
{
    private static readonly Regex LocaleTextPageRegex = new(@"^(assets/[^/]+/(?:manual|book)/(?:[^/]+/)*)en_us/([^/]+\.txt)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GuideMePageRegex = new(@"^(assets/[^/]+/ae2guide/)(?!_[a-z]{2,3}_[a-z]{2}/)(.+\.md)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MarkdownPrefixRegex = new(@"^\s*(?:#{1,6}\s+|[-*+]\s+(?:\[[ xX]\]\s+)?|\d+[.)]\s+|>\s*)*", RegexOptions.Compiled);

    private static string? PlainTextOutputRelative(string path, string langFile)
    {
        path = NormalizePath(path);
        var page = LocaleTextPageRegex.Match(path);
        if (page.Success) return page.Groups[1].Value + langFile + "/" + page.Groups[2].Value;
        var guide = GuideMePageRegex.Match(path);
        return guide.Success ? guide.Groups[1].Value + "_" + langFile + "/" + guide.Groups[2].Value : null;
    }

    private static bool IsPlainTextPage(string path) => PlainTextOutputRelative(path, "vi_vn") is not null;

    // Quote '\0' marks a raw span: ReplaceTextSpans inserts the value without quoting or escaping.
    private List<TextSpan> SelectPlainTextLines(string path, string raw)
    {
        var markdown = path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        var spans = new List<TextSpan>();
        var inFrontMatter = false;
        var inFence = false;
        var start = 0;
        for (var lineNo = 0; start <= raw.Length; lineNo++)
        {
            var end = raw.IndexOf('\n', start);
            if (end < 0) end = raw.Length;
            var lineEnd = end > start && raw[end - 1] == '\r' ? end - 1 : end;
            var line = raw[start..lineEnd];
            var trimmed = line.Trim();
            var offset = 0;
            var take = true;
            if (markdown)
            {
                if (trimmed == "---" && (lineNo == 0 || inFrontMatter)) { inFrontMatter = lineNo == 0; take = false; }
                else if (inFrontMatter)
                {
                    var title = Regex.Match(line, @"^\s*title:\s*");
                    take = title.Success;
                    offset = title.Length;
                }
                else if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) { inFence = !inFence; take = false; }
                else if (inFence || trimmed.StartsWith('<') || trimmed.StartsWith("![") || trimmed.StartsWith('|') && trimmed.Replace("|", "").Replace("-", "").Replace(":", "").Trim().Length == 0)
                    take = false;
                else offset = MarkdownPrefixRegex.Match(line).Length;
            }
            else if (trimmed.Equals("<NEWLINE>", StringComparison.OrdinalIgnoreCase)) take = false;

            var value = line[offset..];
            if (take && IsTranslatable(value) && !IsTranslationKey(value.Trim()))
                spans.Add(new(start + offset, value.Length, "line", value, '\0', path + ":" + lineNo));
            if (end == raw.Length) break;
            start = end + 1;
        }
        return spans;
    }

    // A target page that already exists and was not written by this app (official or hand-made translation) is kept.
    private async Task<bool> IsForeignTranslationAsync(string statePath, string? existingTarget, CancellationToken ct)
    {
        if (existingTarget is null || _resetTranslations) return false;
        if (!File.Exists(statePath)) return true;
        try
        {
            var state = System.Text.Json.JsonSerializer.Deserialize<DocumentCheckpoint>(await File.ReadAllTextAsync(statePath, ct));
            return state is null || state.OutputHash != ContentHash(existingTarget);
        }
        catch (System.Text.Json.JsonException) { return true; }
    }
}
