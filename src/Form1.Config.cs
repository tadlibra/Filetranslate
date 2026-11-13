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
        return LoadClaudeSettingsConfig();
    }

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
        _ => ""
    };

    private static string DefaultModel(TranslatorProvider provider) => provider switch
    {
        TranslatorProvider.Gemini => "gemini-2.5-flash",
        TranslatorProvider.OpenAiCompatible => "gpt-4o-mini",
        TranslatorProvider.AnthropicCompatible => "claude-3-5-haiku-latest",
        TranslatorProvider.Ollama => "qwen2.5:7b",
        _ => ""
    };

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
    }
}
