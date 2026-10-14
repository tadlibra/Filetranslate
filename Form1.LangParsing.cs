using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Filetranslate;

public partial class Form1
{
    private bool IsTranslatable(string value)
    {
        var stripped = value.Trim();
        if (string.IsNullOrEmpty(stripped) || _mcIdRegex.IsMatch(stripped) || (_hexRegex.IsMatch(stripped) && stripped.Any(char.IsDigit))) return false;
        if (_pathLikeRegex.IsMatch(stripped) && Regex.IsMatch(stripped, @"^(?:assets|textures|models)/|\.(?:json|png|jpg|ogg|cfg|snbt|mcmeta|zip|jar)$", RegexOptions.IgnoreCase)) return false;
        if (stripped.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || stripped.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        var plain = _colorRegex.Replace(stripped, "");
        plain = _formatTokenRegex.Replace(plain, "");
        return _letterRegex.IsMatch(plain);
    }

    private static bool IsTranslationKey(string value) => Regex.IsMatch(value.Trim(), @"^\{?[a-zA-Z_][\w-]*(?:\.[\w-]+)+\}?$");

    private static Dictionary<string, string> ParseLegacyLang(string raw)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in raw.TrimStart('\uFEFF').Split('\n'))
        {
            var t = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(t) || t.TrimStart().StartsWith('#')) continue;
            var idx = t.IndexOf('=');
            if (idx <= 0) continue;
            map[t[..idx].Trim()] = t[(idx + 1)..];
        }
        return map;
    }

    private static Dictionary<string, string> ParseLangJson(string raw)
    {
        using var doc = JsonDocument.Parse(raw.TrimStart('\uFEFF'), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("Lang JSON must be an object.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                if (property.Name.StartsWith('_')) continue;
                throw new FormatException($"Lang value is not a string: {property.Name}");
            }
            result[property.Name] = property.Value.GetString()!;
        }
        return result;
    }

    private static Dictionary<string, string> LoadExistingJsonMap(string file)
    {
        try { return ParseLangJson(File.ReadAllText(file)); }
        catch (JsonException) { return new(StringComparer.Ordinal); }
        catch (FormatException) { return new(StringComparer.Ordinal); }
    }

    private static Dictionary<string, string> ParseSourceLangJson(string raw, out bool recovered)
    {
        recovered = false;
        try { return ParseLangJson(raw); }
        catch (JsonException)
        {
            // Recover complete flat string maps with comments/missing commas/raw line breaks.
            // Never invent missing braces, values or keys.
            var trimmed = raw.TrimStart('\uFEFF').Trim();
            if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}')) throw;
            var reader = new StructuredTextReader(trimmed);
            var strings = reader.Read();
            if (reader.HasNonStringLeaf || strings.Count == 0 || strings.Any(s => s.Context != "/" + s.Field)) throw;
            recovered = true;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var span in strings) result[span.Field] = span.Value;
            return result;
        }
    }

    private bool IsPatchouliTextField(string? propertyName, string value) => propertyName is not null
        && JsonTextFields.Contains(propertyName) && IsTranslatable(value) && !IsTranslationKey(value);

    private void CollectJsonStrings(JsonNode node, string? propertyName, List<JsonStringRef> strings)
    {
        if (node is JsonObject obj)
        {
            foreach (var kv in obj.ToArray())
                if (kv.Value is not null) CollectJsonStrings(kv.Value, kv.Key, strings);
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
                if (item is not null) CollectJsonStrings(item, propertyName, strings);
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out var text))
            strings.Add(new JsonStringRef(value, propertyName, text));
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string SerializeJsonMap(Dictionary<string, string> map) => JsonSerializer.Serialize(
        map.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value), JsonOptions(true));

    private static JsonSerializerOptions JsonOptions(bool indented) => new()
    {
        WriteIndented = indented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static IEnumerable<T[]> Chunk<T>(IReadOnlyList<T> values, int size)
    {
        for (var i = 0; i < values.Count; i += size)
        {
            var len = Math.Min(size, values.Count - i);
            var arr = new T[len];
            for (var j = 0; j < len; j++) arr[j] = values[i + j];
            yield return arr;
        }
    }
}

