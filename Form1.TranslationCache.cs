using System.Text.Json;

namespace Filetranslate;

public partial class Form1
{
    private async Task<TranslateResult> TranslateCachedAsync(string identity, string staging, List<string> texts,
        List<string> fields, string target, CancellationToken ct)
    {
        var cacheFile = SafeChildPath(staging, "cache-v3/" + ContentHash(identity + "\n" + target) + ".json");
        var cache = !_resetTranslations && File.Exists(cacheFile) ? LoadExistingJsonMap(cacheFile) : new Dictionary<string, string>(StringComparer.Ordinal);
        var keys = texts.Select((t, i) => ContentHash(fields[i] + "\n" + t)).ToArray();
        var values = texts.ToList();
        var failed = new HashSet<int>();
        var missing = new List<int>();
        var firstByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < texts.Count; i++)
        {
            if (cache.TryGetValue(keys[i], out var translated)) values[i] = translated;
            else if (firstByKey.TryAdd(keys[i], i)) missing.Add(i);
        }
        // Bound both count and characters: a long book page must not make a huge API request.
        for (var offset = 0; offset < missing.Count;)
        {
            ct.ThrowIfCancellationRequested();
            var batch = new List<int>();
            var characters = 0;
            while (offset < missing.Count && batch.Count < 32)
            {
                var index = missing[offset];
                if (batch.Count > 0 && characters + texts[index].Length > 6000) break;
                batch.Add(index);
                characters += texts[index].Length;
                offset++;
            }
            var result = await TranslateBatchAsync(batch.Select(i => texts[i]).ToList(), target, "auto", ct,
                string.Join(" | ", batch.Select(i => fields[i])));
            for (var i = 0; i < batch.Count; i++)
            {
                var index = batch[i];
                values[index] = result.Values[i];
                if (result.FailedIndices?.Contains(i) == true || (result.Errors > 0 && result.FailedIndices is null)) failed.Add(index);
                else cache[keys[index]] = values[index];
            }
            await WriteAtomicAsync(cacheFile, SerializeJsonMap(cache), false, ct);
        }
        for (var i = 0; i < texts.Count; i++)
        {
            if (!firstByKey.TryGetValue(keys[i], out var first) || first == i) continue;
            values[i] = values[first];
            if (failed.Contains(first)) failed.Add(i);
        }
        var unchanged = texts.Where((t, i) => t == values[i]).Count();
        _coverage.Enqueue(new(identity, failed.Count > 0 ? "translation-errors" : unchanged > 0 ? "unchanged-review" : "translated",
            texts.Count, unchanged, failed.Count > 0 ? $"{failed.Count} strings retained for retry." : null));
        return new(values, failed.Count, failed);
    }
}
