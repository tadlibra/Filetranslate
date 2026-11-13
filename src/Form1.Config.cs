using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Filetranslate;

public partial class Form1
{
    private static AiTranslatorConfig? LoadAiTranslatorConfig()
    {
        // An explicit Google selection must not fall through to unrelated credentials.
        if (File.Exists(UserTranslatorConfigPath) || File.Exists(LocalTranslatorConfigPath))
            return LoadUserTranslatorConfig();
        return LoadClaudeSettingsConfig() ?? DetectLocalOllama() ?? InstalledBuiltInConfig();
    }

    // No saved choice: a running Ollama is free and unlimited, so prefer it over the rate-limited Google endpoint.
    private static AiTranslatorConfig? DetectLocalOllama(string baseUrl = "http://localhost:11434")
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var json = client.GetStringAsync(baseUrl.TrimEnd('/') + "/api/tags").GetAwaiter().GetResult();
            var model = PickOllamaModel(json);
            return model is null ? null : new AiTranslatorConfig(TranslatorProvider.Ollama, new Uri(baseUrl), "", model, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException) { return null; }
    }

    private static string? PickOllamaModel(string tagsJson)
    {
        using var doc = JsonDocument.Parse(tagsJson);
        var names = doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array
            ? models.EnumerateArray().Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null).OfType<string>().ToList()
            : [];
        // Embedding models cannot chat; Qwen handles Vietnamese well among common local models.
        names = names.Where(n => !n.Contains("embed", StringComparison.OrdinalIgnoreCase)).ToList();
        return names.FirstOrDefault(n => n.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault();
    }

    private static AiTranslatorConfig? InstalledBuiltInConfig() =>
        LocalModelPacks.FirstOrDefault(IsLocalModelInstalled) is { } pack ? BuiltInConfig(pack) : null;

    private static AiTranslatorConfig BuiltInConfig(LocalModelPack pack) =>
        new(TranslatorProvider.BuiltIn, new Uri(DefaultBaseUrl(TranslatorProvider.BuiltIn)), "", pack.Alias, true);

    private sealed record ProviderPreset(string Name, TranslatorProvider Provider, string BaseUrl, string Model, string? KeyUrl, string Note);

    private static readonly ProviderPreset[] ProviderPresets =
    [
        new("Built-in - AI miễn phí chạy trên máy, không cần key", TranslatorProvider.BuiltIn, "", "", null,
            "Tự tải llama.cpp và model Qwen3.5 (1,3-2,7 GB) một lần rồi dịch offline, không giới hạn. Tốc độ tùy GPU/CPU."),
        new("Ollama - chạy trên máy, không cần key", TranslatorProvider.Ollama, "http://localhost:11434", "qwen2.5:7b", "https://ollama.com/download",
            "Cài Ollama rồi chạy: ollama pull qwen2.5:7b. Miễn phí, không giới hạn, tốc độ tùy máy."),
        new("Qwen - Alibaba DashScope (quota free)", TranslatorProvider.OpenAiCompatible, "https://dashscope-intl.aliyuncs.com/compatible-mode/v1", "qwen-plus", "https://modelstudio.console.alibabacloud.com/",
            "Tài khoản mới có quota token miễn phí. Tạo API key trong Model Studio."),
        new("Groq (key free)", TranslatorProvider.OpenAiCompatible, "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", "https://console.groq.com/keys",
            "Key miễn phí, nhanh; có giới hạn request mỗi phút/ngày."),
        new("OpenRouter - model :free (key free)", TranslatorProvider.OpenAiCompatible, "https://openrouter.ai/api/v1", "google/gemma-4-31b-it:free", "https://openrouter.ai/keys",
            "Key miễn phí; model đuôi :free có giới hạn request mỗi ngày. Danh sách model free thay đổi theo thời gian."),
        new("Gemini (key free)", TranslatorProvider.Gemini, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", "https://aistudio.google.com/apikey",
            "Key miễn phí từ Google AI Studio; có giới hạn request mỗi phút/ngày."),
        new("Pollinations - không cần key (rất chậm)", TranslatorProvider.OpenAiCompatible, "https://text.pollinations.ai/openai", "openai", null,
            "Không cần key nhưng chỉ 1 lần trả lời mỗi 45 giây; modpack lớn mất nhiều giờ."),
    ];

    private static AiTranslatorConfig? LoadUserTranslatorConfig()
    {
        foreach (var file in new[] { UserTranslatorConfigPath, LocalTranslatorConfigPath })
        {
            if (!File.Exists(file)) continue;
            try
            {
                var config = ReadTranslatorSettingsFile(file);
                if (config is null || config.Provider == TranslatorProvider.Google) return null;
                var normalized = NormalizeTranslatorConfig(config);
                if (normalized is not null) return normalized;
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private static TranslatorSettingsFile LoadEditableTranslatorSettings()
    {
        foreach (var file in new[] { UserTranslatorConfigPath, LocalTranslatorConfigPath })
        {
            if (!File.Exists(file)) continue;
            try
            {
                return ReadTranslatorSettingsFile(file) ?? new TranslatorSettingsFile();
            }
            catch
            {
                break;
            }
        }

        var claude = LoadClaudeSettingsConfig();
        if (claude is not null)
        {
            return new TranslatorSettingsFile
            {
                Provider = claude.Provider,
                BaseUrl = claude.BaseUrl.ToString().TrimEnd('/'),
                Model = claude.Model,
                ApiKey = claude.ApiKey,
                FallbackToGoogle = claude.FallbackToGoogle
            };
        }

        return new TranslatorSettingsFile
        {
            Provider = TranslatorProvider.Gemini,
            BaseUrl = DefaultBaseUrl(TranslatorProvider.Gemini),
            Model = DefaultModel(TranslatorProvider.Gemini),
            FallbackToGoogle = true
        };
    }

    private static void SaveTranslatorSettings(TranslatorSettingsFile settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserTranslatorConfigPath)!);
        var stored = new TranslatorSettingsFile
        {
            Provider = settings.Provider,
            BaseUrl = settings.BaseUrl,
            ApiKey = ProtectApiKey(settings.ApiKey),
            Model = settings.Model,
            FallbackToGoogle = settings.FallbackToGoogle
        };
        File.WriteAllText(UserTranslatorConfigPath, JsonSerializer.Serialize(stored, JsonOptions(true)), Encoding.UTF8);
    }

    private static TranslatorSettingsFile? ReadTranslatorSettingsFile(string file)
    {
        var config = JsonSerializer.Deserialize<TranslatorSettingsFile>(File.ReadAllText(file));
        if (config is not null) config.ApiKey = UnprotectApiKey(config.ApiKey);
        return config;
    }

    private const string ProtectedKeyPrefix = "dpapi:";

    // DPAPI CurrentUser: config file copied to another user/machine cannot reveal the key.
    private static string? ProtectApiKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.StartsWith(ProtectedKeyPrefix, StringComparison.Ordinal)) return key;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
        return ProtectedKeyPrefix + Convert.ToBase64String(bytes);
    }

    // Plain keys from older versions are still accepted and get encrypted on next save.
    private static string? UnprotectApiKey(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(ProtectedKeyPrefix, StringComparison.Ordinal)) return stored;
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored[ProtectedKeyPrefix.Length..]), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private static AiTranslatorConfig? NormalizeTranslatorConfig(TranslatorSettingsFile config)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? DefaultBaseUrl(config.Provider)
            : config.BaseUrl;
        var model = string.IsNullOrWhiteSpace(config.Model)
            ? DefaultModel(config.Provider)
            : config.Model;

        if (string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(model)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        if (config.Provider is TranslatorProvider.Gemini or TranslatorProvider.AnthropicCompatible
            && string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return null;
        }

        return new AiTranslatorConfig(config.Provider, uri, config.ApiKey ?? "", model, config.FallbackToGoogle);
    }

    private static AiTranslatorConfig? LoadClaudeSettingsConfig()
    {
        if (!File.Exists(ClaudeSettingsPath)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ClaudeSettingsPath));
            if (!doc.RootElement.TryGetProperty("env", out var env)) return null;

            var baseUrl = ReadString(env, "ANTHROPIC_BASE_URL");
            var apiKey = ReadString(env, "ANTHROPIC_AUTH_TOKEN")
                ?? ReadString(env, "ANTHROPIC_API_KEY")
                ?? ReadString(env, "CLAUDIBLE_API_KEY");
            var model = ReadString(env, "ANTHROPIC_DEFAULT_SONNET_MODEL")
                ?? ReadString(env, "ANTHROPIC_DEFAULT_HAIKU_MODEL")
                ?? ReadString(env, "ANTHROPIC_DEFAULT_OPUS_MODEL");

            if (doc.RootElement.TryGetProperty("model", out var modelAlias))
            {
                model = modelAlias.GetString() switch
                {
                    "haiku" => ReadString(env, "ANTHROPIC_DEFAULT_HAIKU_MODEL") ?? model,
                    "opus" => ReadString(env, "ANTHROPIC_DEFAULT_OPUS_MODEL") ?? model,
                    "sonnet" => ReadString(env, "ANTHROPIC_DEFAULT_SONNET_MODEL") ?? model,
                    _ => model
                };
            }

            if (string.IsNullOrWhiteSpace(baseUrl)
                || string.IsNullOrWhiteSpace(apiKey)
                || string.IsNullOrWhiteSpace(model)
                || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                return null;
            }

            return new AiTranslatorConfig(TranslatorProvider.AnthropicCompatible, uri, apiKey, model, true);
        }
        catch
        {
            return null;
        }
    }

    private static string DefaultBaseUrl(TranslatorProvider provider) => provider switch
    {
        TranslatorProvider.Gemini => "https://generativelanguage.googleapis.com",
        TranslatorProvider.OpenAiCompatible => "https://api.openai.com",
        TranslatorProvider.AnthropicCompatible => "https://api.anthropic.com",
        TranslatorProvider.Ollama => "http://localhost:11434",
        TranslatorProvider.BuiltIn => "http://127.0.0.1/v1",
        _ => ""
    };

    private static string DefaultModel(TranslatorProvider provider) => provider switch
    {
        TranslatorProvider.Gemini => "gemini-2.5-flash",
        TranslatorProvider.OpenAiCompatible => "gpt-4o-mini",
        TranslatorProvider.AnthropicCompatible => "claude-3-5-haiku-latest",
        TranslatorProvider.Ollama => "qwen2.5:7b",
        TranslatorProvider.BuiltIn => PickLocalModelPack((long)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes).Alias,
        _ => ""
    };

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
    }
}
