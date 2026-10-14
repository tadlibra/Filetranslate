using System.Globalization;
using System.Text;

namespace Filetranslate;

public partial class Form1
{
    // Keep exact source offsets: replacing only string values preserves SNBT types and comments.
    private sealed class StructuredTextReader(string source)
    {
        private int _position;
        private readonly List<TextSpan> _strings = [];
        public bool HasNonStringLeaf { get; private set; }

        public List<TextSpan> Read()
        {
            SkipTrivia();
            ReadValue("", 0);
            SkipTrivia();
            if (_position != source.Length) throw new FormatException($"Unexpected content at {_position}.");
            return _strings;
        }

        private void ReadValue(string field, int depth, string context = "")
        {
            if (depth > 128) throw new FormatException("Document nesting exceeds 128.");
            SkipTrivia();
            if (_position >= source.Length) throw new FormatException("Unexpected end of document.");
            var ch = source[_position];
            if (ch is '\"' or '\'')
            {
                _strings.Add(ReadString(field) with { Context = context });
                return;
            }
            if (ch is '{' or '[')
            {
                _position++;
                var close = ch == '{' ? '}' : ']';
                SkipTrivia();
                // SNBT typed numeric arrays have a B;/I;/L; prefix.
                if (ch == '[' && _position + 1 < source.Length && source[_position + 1] == ';') _position += 2;
                while (true)
                {
                    SkipTrivia();
                    if (_position >= source.Length) throw new FormatException("Unclosed object or array.");
                    if (source[_position] == close) { _position++; return; }
                    var childField = field;
                    if (ch == '{')
                    {
                        childField = source[_position] is '\"' or '\'' ? ReadString("").Value : ReadBare(true);
                        SkipTrivia();
                        if (_position >= source.Length || source[_position++] != ':') throw new FormatException("Missing colon.");
                    }
                    ReadValue(childField, depth + 1, ch == '{' ? context + "/" + childField : context + "[]");
                    SkipTrivia();
                    if (_position < source.Length && source[_position] == ',') _position++;
                }
            }
            ReadBare(false);
            HasNonStringLeaf = true;
        }

        private string ReadBare(bool key)
        {
            var start = _position;
            while (_position < source.Length)
            {
                var ch = source[_position];
                if (char.IsWhiteSpace(ch) || ch is ',' or ']' or '}' || (key && ch == ':')) break;
                if (ch is '{' or '[' or '\"' or '\'') throw new FormatException($"Invalid token at {_position}.");
                _position++;
            }
            if (_position == start) throw new FormatException($"Expected value at {_position}.");
            return source[start.._position];
        }

        private TextSpan ReadString(string field)
        {
            var start = _position;
            var quote = source[_position++];
            var value = new StringBuilder();
            while (_position < source.Length)
            {
                var ch = source[_position++];
                if (ch == quote) return new TextSpan(start, _position - start, field, value.ToString(), quote);
                if (ch != '\\') { value.Append(ch); continue; }
                if (_position >= source.Length) break;
                ch = source[_position++];
                switch (ch)
                {
                    case 'n': value.Append('\n'); break;
                    case 'r': value.Append('\r'); break;
                    case 't': value.Append('\t'); break;
                    case 'b': value.Append('\b'); break;
                    case 'f': value.Append('\f'); break;
                    case '\n': break;
                    case '\r': if (_position < source.Length && source[_position] == '\n') _position++; break;
                    case 'u':
                    case 'x':
                        var length = ch == 'u' ? 4 : 2;
                        if (_position + length > source.Length || !ushort.TryParse(source.AsSpan(_position, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            throw new FormatException("Invalid unicode escape.");
                        value.Append((char)code);
                        _position += length;
                        break;
                    case '\\': case '\"': case '\'': case '/': value.Append(ch); break;
                    default: throw new FormatException($"Unsupported escape \\{ch}.");
                }
            }
            throw new FormatException("Unclosed string.");
        }

        private void SkipTrivia()
        {
            while (_position < source.Length)
            {
                if (char.IsWhiteSpace(source[_position]) || source[_position] == '\uFEFF') { _position++; continue; }
                if (source[_position] == '#' || source.AsSpan(_position).StartsWith("//"))
                {
                    while (_position < source.Length && source[_position] != '\n') _position++;
                    continue;
                }
                if (source.AsSpan(_position).StartsWith("/*"))
                {
                    var end = source.IndexOf("*/", _position + 2, StringComparison.Ordinal);
                    if (end < 0) throw new FormatException("Unclosed comment.");
                    _position = end + 2;
                    continue;
                }
                break;
            }
        }
    }

    private static string ReplaceTextSpans(string content, IReadOnlyList<TextSpan> spans, IReadOnlyList<string> values)
    {
        var output = new StringBuilder(content);
        for (var i = spans.Count - 1; i >= 0; i--)
        {
            if (values[i] == spans[i].Value) continue;
            var quote = spans[i].Quote;
            var escaped = values[i].Replace("\\", "\\\\").Replace(quote.ToString(), "\\" + quote)
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\b", "\\b").Replace("\f", "\\f");
            output.Remove(spans[i].Start, spans[i].Length).Insert(spans[i].Start, quote + escaped + quote);
        }
        return output.ToString();
    }
}
