using System.Text.RegularExpressions;

namespace Filetranslate;

public partial class Form1
{
    private sealed record ScriptToken(string Value, int Start, int Length, bool IsString = false);

    private static List<ScriptToken> ReadScriptTokens(string code)
    {
        var tokens = new List<ScriptToken>();
        for (var i = 0; i < code.Length;)
        {
            if (char.IsWhiteSpace(code[i]) || code[i] == '\uFEFF') { i++; continue; }
            if (code.AsSpan(i).StartsWith("//")) { while (i < code.Length && code[i] != '\n') i++; continue; }
            if (code.AsSpan(i).StartsWith("/*"))
            {
                var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) throw new FormatException("Unclosed script comment.");
                i = end + 2;
                continue;
            }
            if (code[i] == '`') throw new NotSupportedException("Template literals require a JavaScript AST adapter.");
            var start = i;
            if (code[i] is '\"' or '\'')
            {
                var quote = code[i++];
                var closed = false;
                while (i < code.Length)
                {
                    if (code[i] == '\\') { i += 2; continue; }
                    if (code[i++] == quote) { closed = true; break; }
                }
                if (!closed) throw new FormatException("Unclosed script string.");
                var literal = code[start..i];
                var value = new StructuredTextReader(literal).Read().Single().Value;
                tokens.Add(new(value, start, i - start, true));
                continue;
            }
            if (code[i] == '/' && (tokens.Count == 0 || tokens[^1].Value is "(" or "=" or ":" or "," or "[" or "return" or ">"))
            {
                var inClass = false;
                i++;
                while (i < code.Length)
                {
                    if (code[i] == '\\') { i += 2; continue; }
                    if (code[i] == '[') inClass = true;
                    if (code[i] == ']') inClass = false;
                    if (code[i++] == '/' && !inClass) break;
                }
                while (i < code.Length && char.IsLetter(code[i])) i++;
                tokens.Add(new("<regex>", start, i - start));
                continue;
            }
            if (char.IsLetterOrDigit(code[i]) || code[i] is '_' or '$')
            {
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] is '_' or '$')) i++;
                tokens.Add(new(code[start..i], start, i - start));
            }
            else { tokens.Add(new(code[i].ToString(), i, 1)); i++; }
        }
        return tokens;
    }

    private List<TextSpan> SelectScriptStrings(string code, string langFile)
    {
        var tokens = ReadScriptTokens(code);
        var selected = new Dictionary<int, TextSpan>();
        int Close(int open)
        {
            var stack = new Stack<string>();
            for (var i = open; i < tokens.Count; i++)
            {
                if (tokens[i].IsString) continue;
                var t = tokens[i].Value;
                if (t is "(" or "[" or "{") stack.Push(t);
                else if (t is ")" or "]" or "}")
                {
                    if (stack.Count == 0) throw new FormatException("Unbalanced script.");
                    var expected = stack.Pop() switch { "(" => ")", "[" => "]", _ => "}" };
                    if (t != expected) throw new FormatException("Unbalanced script.");
                    if (stack.Count == 0) return i;
                }
            }
            throw new FormatException("Unclosed script expression.");
        }
        List<(int Start, int End)> Arguments(int open, int close)
        {
            var args = new List<(int, int)>();
            var start = open + 1;
            for (var i = start; i < close; i++)
            {
                if (!tokens[i].IsString && tokens[i].Value is "(" or "[" or "{") i = Close(i);
                else if (!tokens[i].IsString && tokens[i].Value == ",") { args.Add((start, i)); start = i + 1; }
            }
            if (start < close) args.Add((start, close));
            return args;
        }
        void AddLiteral((int Start, int End) arg, string field)
        {
            if (arg.End - arg.Start == 1 && tokens[arg.Start].IsString)
            {
                var token = tokens[arg.Start];
                if (IsTranslatable(token.Value) && !IsTranslationKey(token.Value))
                    selected[token.Start] = new(token.Start, token.Length, field, token.Value, code[token.Start]);
            }
            else if (tokens[arg.Start].Value == "[" && Close(arg.Start) == arg.End - 1)
            {
                foreach (var element in Arguments(arg.Start, arg.End - 1)) AddLiteral(element, field);
            }
        }

        var scopes = new List<(int Start, int End, string Variable, bool Lang)>();
        for (var i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].IsString || tokens[i + 1].Value != "." || tokens[i + 3].Value != "(") continue;
            var isLang = tokens[i].Value == "ClientEvents" && tokens[i + 2].Value == "lang";
            var isTooltip = tokens[i].Value == "ItemEvents" && tokens[i + 2].Value == "tooltip";
            if (!isLang && !isTooltip) continue;
            var end = Close(i + 3);
            var args = Arguments(i + 3, end);
            if (isLang && (args.Count != 2 || args[0].End - args[0].Start != 1 || tokens[args[0].Start].Value != "en_us")) continue;
            var callback = args.LastOrDefault();
            // Only a simple event => { ... } callback is unambiguous without a complete JS AST.
            var first = callback.Start;
            if (first + 3 >= tokens.Count || tokens[first + 1].Value != "=" || tokens[first + 2].Value != ">" || tokens[first + 3].Value != "{") continue;
            var bodyEnd = Close(first + 3);
            scopes.Add((first + 3, bodyEnd, tokens[first].Value, isLang));
            if (isLang)
            {
                var locale = tokens[args[0].Start];
                selected[locale.Start] = new(locale.Start, locale.Length, "__target_locale", locale.Value, code[locale.Start]);
            }
        }
        for (var i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].IsString || tokens[i + 1].Value != "." || tokens[i + 3].Value != "(") continue;
            var method = tokens[i + 2].Value;
            var args = Arguments(i + 3, Close(i + 3));
            if (tokens[i].Value == "Text" && method is "of" or "literal" && args.Count == 1) AddLiteral(args[0], method);
            if (method == "displayName" && args.Count == 1) AddLiteral(args[0], method);
            foreach (var scope in scopes.Where(s => i > s.Start && i < s.End && s.Variable == tokens[i].Value))
            {
                if (args.Count == 2 && (scope.Lang && method is "add" or "renameItem" or "renameBlock" or "renameEntity"
                    || !scope.Lang && method == "add")) AddLiteral(args[1], method);
            }
        }
        return selected.Values.OrderBy(s => s.Start).ToList();
    }

    private List<TextSpan> SelectTriumphStrings(string code)
    {
        var tokens = ReadScriptTokens(code);
        var strings = new List<TextSpan>();
        for (var i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].IsString || tokens[i].Value is not ("setTitle" or "setDescription")) continue;
            if (i > 0 && tokens[i - 1].Value == ".") continue;
            if (tokens[i + 1].Value != "(" || !tokens[i + 2].IsString || tokens[i + 3].Value != ")") continue;
            var text = tokens[i + 2];
            if (IsTranslatable(text.Value) && !IsTranslationKey(text.Value))
                strings.Add(new(text.Start, text.Length, tokens[i].Value, text.Value, code[text.Start]));
        }
        return strings;
    }
}
