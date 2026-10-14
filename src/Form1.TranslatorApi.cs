using System.Net.Http;
using System.Net.Http.Json;

namespace Filetranslate;

public partial class Form1
{
    private async Task<TranslateResult> TranslateBatchAsync(List<string> texts, string target, string source, CancellationToken ct, string? context = null)
    {
        ct.ThrowIfCancellationRequested();
        var splits = texts.Select(SplitLongText).ToArray();
        if (splits.Any(p => p.Count > 1))
        {
            var values = new List<string>();
            var failures = new HashSet<int>();
            for (var i = 0; i < texts.Count; i++)
            {
                var result = await TranslateBatchAsync(splits[i], target, source, ct, context);
                values.Add(result.Errors == 0 ? string.Concat(result.Values) : texts[i]);
                if (result.Errors > 0) failures.Add(i);
            }
            return new(values, failures.Count, failures);
        }
        var knownValues = texts.ToList();
        var unresolved = new List<int>();
        for (var i = 0; i < texts.Count; i++)
        {
            if (TryKnownTranslation(texts[i], target, out var known)) knownValues[i] = known;
            else unresolved.Add(i);
        }
        if (unresolved.Count != texts.Count)
        {
            if (unresolved.Count == 0) return new(knownValues, 0);
            var remainder = await TranslateBatchAsync(unresolved.Select(i => texts[i]).ToList(), target, source, ct, context);
            var failures = new HashSet<int>();
            for (var i = 0; i < unresolved.Count; i++)
            {
                knownValues[unresolved[i]] = remainder.Values[i];
                if (remainder.FailedIndices?.Contains(i) == true) failures.Add(unresolved[i]);
            }
            return new(knownValues, failures.Count, failures);
        }
        try { return await TranslateBatchCoreAsync(texts, target, source, ct, context); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or FormatException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // Smaller batches recover truncated/malformed model responses without losing unrelated strings.
            if (texts.Count > 1 && ex is not HttpRequestException)
            {
                var middle = texts.Count / 2;
                var left = await TranslateBatchAsync(texts.Take(middle).ToList(), target, source, ct, context);
                var right = await TranslateBatchAsync(texts.Skip(middle).ToList(), target, source, ct, context);
                var failures = new HashSet<int>(left.FailedIndices ?? []);
                foreach (var i in right.FailedIndices ?? []) failures.Add(i + middle);
                return new(left.Values.Concat(right.Values).ToList(), left.Errors + right.Errors, failures);
            }
            return new(texts.ToList(), texts.Count, Enumerable.Range(0, texts.Count).ToHashSet());
        }
    }

    private async Task<TranslateResult> TranslateBatchCoreAsync(List<string> texts, string target, string source, CancellationToken ct, string? context)
    {
        if (texts.Count == 0) return new TranslateResult([], 0);

        if (_aiConfig is not null)
        {
            try
            {
                if (Interlocked.Exchange(ref _aiUseLogged, 1) == 0)
                {
                    Log($"Đang dịch bằng AI: {_aiConfig.Provider} / {_aiConfig.Model} @ {_aiConfig.BaseUrl.Host}");
                }
                for (var attempt = 0; ; attempt++)
                {
                    try { return await TranslateBatchWithAiAsync(texts, target, source, _aiConfig, ct, context); }
                    catch (Exception ex) when (attempt < 2 && !ct.IsCancellationRequested && IsTransient(ex))
                    { await Task.Delay(TimeSpan.FromSeconds(1 << attempt), ct); }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (!_aiConfig.FallbackToGoogle) throw;
                if (Interlocked.Exchange(ref _aiFallbackLogged, 1) == 0)
                {
                    Log($"AI lỗi ({ex.GetType().Name}), fallback Google Translate.");
                }
                return await TranslateBatchWithGoogleAsync(texts, target, source, ct);
            }
        }

        if (Interlocked.Exchange(ref _aiFallbackLogged, 1) == 0)
        {
            Log("Không có cấu hình AI, dùng Google Translate.");
        }
        return await TranslateBatchWithGoogleAsync(texts, target, source, ct);
    }

    private async Task<TranslateResult> TranslateBatchWithAiAsync(
        List<string> texts,
        string target,
        string source,
        AiTranslatorConfig config,
        CancellationToken ct, string? context = null)
    {
        var protectedTexts = new List<string>(texts.Count);
        var tokenMaps = new List<Dictionary<string, string>>(texts.Count);
        var envelopes = texts.Select(SplitFormatting).ToList();
        foreach (var envelope in envelopes)
        {
            protectedTexts.Add(ProtectTokens(envelope.Body, out var tokens));
            tokenMaps.Add(tokens);
        }

        var prompt = BuildAiTranslatePrompt(protectedTexts, target, source, context);
        var translated = config.Provider switch
        {
            TranslatorProvider.AnthropicCompatible => await TranslateWithAnthropicCompatibleAsync(prompt, config, ct),
            TranslatorProvider.OpenAiCompatible => await TranslateWithOpenAiCompatibleAsync(prompt, config, ct),
            TranslatorProvider.Gemini => await TranslateWithGeminiAsync(prompt, config, ct),
            TranslatorProvider.Ollama => await TranslateWithOllamaAsync(prompt, config, ct),
            _ => throw new InvalidOperationException("Unsupported AI provider.")
        };

        if (string.IsNullOrWhiteSpace(translated)) throw new InvalidOperationException("AI response is empty.");

        var values = ParseJsonStringArray(translated);
        if (values.Count != texts.Count) throw new InvalidOperationException("AI response item count mismatch.");

        for (var i = 0; i < values.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i]) && !string.IsNullOrWhiteSpace(envelopes[i].Body)) throw new FormatException("AI returned empty text.");
            values[i] = envelopes[i].Prefix + RestoreTokens(ApplyMinecraftTerminology(envelopes[i].Body, values[i], target), tokenMaps[i]) + envelopes[i].Suffix;
        }

        return new TranslateResult(values, 0);
    }

    private async Task<string?> TranslateWithAnthropicCompatibleAsync(string prompt, AiTranslatorConfig config, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint(config.BaseUrl, "v1/messages"));
        request.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.ApiKey}");
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new
        {
            model = config.Model,
            max_tokens = Math.Clamp(prompt.Length / 2 + 1024, 1024, 8192),
            temperature = 0,
            system = "You are a professional Minecraft modpack localization engine. Return valid JSON only.",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        });

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return ParseAnthropicText(responseJson);
    }

    private async Task<string?> TranslateWithOpenAiCompatibleAsync(string prompt, AiTranslatorConfig config, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint(config.BaseUrl, "v1/chat/completions"));
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.ApiKey}");
        }

        request.Content = JsonContent.Create(new
        {
            model = config.Model,
            temperature = 0,
            messages = new[]
            {
                new { role = "system", content = "You are a professional Minecraft modpack localization engine. Return valid JSON only." },
                new { role = "user", content = prompt }
            }
        });

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseOpenAiText(json);
    }

    private async Task<string?> TranslateWithGeminiAsync(string prompt, AiTranslatorConfig config, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(config.Model) ? "gemini-2.5-flash" : config.Model;
        var url = ApiEndpoint(config.BaseUrl, $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", config.ApiKey);
        request.Content = JsonContent.Create(new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0
            }
        });

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseGeminiText(json);
    }

    private async Task<string?> TranslateWithOllamaAsync(string prompt, AiTranslatorConfig config, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint(config.BaseUrl, "api/chat"));
        request.Content = JsonContent.Create(new
        {
            model = config.Model,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = "You are a professional Minecraft modpack localization engine. Return valid JSON only." },
                new { role = "user", content = prompt }
            }
        });

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseOllamaText(json);
    }

    private async Task<TranslateResult> TranslateBatchWithGoogleAsync(List<string> texts, string target, string source, CancellationToken ct)
    {
        var outList = new List<string>(texts.Count);
        var errors = 0;
        var failed = new HashSet<int>();
        foreach (var text in texts)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var envelope = SplitFormatting(text);
                if (string.IsNullOrWhiteSpace(envelope.Body)) { outList.Add(text); continue; }
                var protectedText = ProtectTokens(envelope.Body, out var tokens);
                var q = Uri.EscapeDataString(protectedText);
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={source}&tl={target}&dt=t&q={q}";
                string json;
                for (var attempt = 0; ; attempt++)
                {
                    try { json = await _httpClient.GetStringAsync(url, ct); break; }
                    catch (Exception ex) when (attempt < 2 && !ct.IsCancellationRequested && IsTransient(ex))
                    { await Task.Delay(TimeSpan.FromSeconds(1 << attempt), ct); }
                }
                var translated = ParseGoogleTranslate(json);
                outList.Add(translated is null ? text : envelope.Prefix + RestoreTokens(ApplyMinecraftTerminology(envelope.Body, translated, target), tokens) + envelope.Suffix);
                if (translated is null) { errors++; failed.Add(outList.Count - 1); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                outList.Add(text);
                errors++;
                failed.Add(outList.Count - 1);
            }
        }

        return new TranslateResult(outList, errors, failed);
    }

    private static bool IsTransient(Exception ex) => ex is OperationCanceledException
        || ex is HttpRequestException request && (request.StatusCode is null || (int)request.StatusCode == 429 || (int)request.StatusCode >= 500);

    private static Uri ApiEndpoint(Uri baseUrl, string route)
    {
        var root = baseUrl.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var version = route.Split('/')[0];
        if (root.EndsWith("/" + version, StringComparison.OrdinalIgnoreCase)) route = route[(version.Length + 1)..];
        return new Uri(root + "/" + route);
    }
}
