using System.Text.Json.Nodes;

namespace Filetranslate;

public partial class Form1
{
    private sealed record JobResult(string Name, List<string> Messages, int Errors);
    private sealed record TranslateResult(List<string> Values, int Errors, HashSet<int>? FailedIndices = null);
    private sealed record CoverageItem(string Source, string Status, int Strings, int Unchanged, string? Detail = null);
    private sealed record TextSpan(int Start, int Length, string Field, string Value, char Quote, string Context = "");
    private sealed record AiTranslatorConfig(TranslatorProvider Provider, Uri BaseUrl, string ApiKey, string Model, bool FallbackToGoogle);

    private enum TranslatorProvider
    {
        Google,
        Gemini,
        OpenAiCompatible,
        AnthropicCompatible,
        Ollama
    }

    private sealed class TranslatorSettingsFile
    {
        public TranslatorProvider Provider { get; set; } = TranslatorProvider.Gemini;
        public string? BaseUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? Model { get; set; }
        public bool FallbackToGoogle { get; set; } = true;
    }

    private sealed class JsonStringRef(JsonValue node, string? propertyName, string value)
    {
        public string? PropertyName { get; } = propertyName;
        public string Value { get; private set; } = value;

        public void Set(string value)
        {
            Value = value;
            node.ReplaceWith(JsonValue.Create(value)!);
        }
    }
}
