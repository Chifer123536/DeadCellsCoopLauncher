using System.Text;

namespace CoopLauncher.Core.Steam;

public sealed class VdfNode
{
    readonly Dictionary<string, VdfNode> _children = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, VdfNode> Children => _children;
    public IReadOnlyDictionary<string, string> Values => _values;

    public VdfNode? Child(string key) => _children.TryGetValue(key, out var node) ? node : null;
    public string? Value(string key) => _values.TryGetValue(key, out var value) ? value : null;

    internal void SetChild(string key, VdfNode node) => _children[key] = node;
    internal void SetValue(string key, string value) => _values[key] = value;
}

public static class VdfReader
{
    public static VdfNode Parse(string text)
    {
        var pos = 0;
        var root = new VdfNode();
        ParseBody(text, ref pos, root, topLevel: true);
        return root;
    }

    public static VdfNode ParseFile(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    static void ParseBody(string text, ref int pos, VdfNode node, bool topLevel)
    {
        while (true)
        {
            SkipTrivia(text, ref pos);
            if (pos >= text.Length)
            {
                if (!topLevel)
                    throw new FormatException("Unexpected end of VDF inside a block");
                return;
            }

            if (text[pos] == '}')
            {
                if (topLevel)
                    throw new FormatException("Unexpected '}' at top level");
                pos++;
                return;
            }

            var key = ReadToken(text, ref pos);
            SkipTrivia(text, ref pos);
            if (pos >= text.Length)
                throw new FormatException($"Missing value for key '{key}'");

            if (text[pos] == '{')
            {
                pos++;
                var child = new VdfNode();
                ParseBody(text, ref pos, child, topLevel: false);
                node.SetChild(key, child);
            }
            else
            {
                node.SetValue(key, ReadToken(text, ref pos));
            }
        }
    }

    static string ReadToken(string text, ref int pos)
    {
        if (text[pos] != '"')
        {
            var start = pos;
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] != '{' && text[pos] != '}' && text[pos] != '"')
                pos++;
            if (pos == start)
                throw new FormatException($"Unexpected character '{text[pos]}' at {pos}");
            return text[start..pos];
        }

        pos++;
        var sb = new StringBuilder();
        while (pos < text.Length)
        {
            var c = text[pos++];
            if (c == '"')
                return sb.ToString();
            if (c == '\\' && pos < text.Length)
            {
                var next = text[pos++];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    _ => next
                });
                continue;
            }
            sb.Append(c);
        }

        throw new FormatException("Unterminated string in VDF");
    }

    static void SkipTrivia(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            if (char.IsWhiteSpace(text[pos]))
            {
                pos++;
                continue;
            }

            if (text[pos] == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n')
                    pos++;
                continue;
            }

            if (text[pos] == '[')
            {
                while (pos < text.Length && text[pos] != ']')
                    pos++;
                if (pos < text.Length)
                    pos++;
                continue;
            }

            return;
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  VDF READER — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Текстовый KeyValues Steam (libraryfolders.vdf, appmanifest_*.acf) → дерево VdfNode.
 *  Ключи без учёта регистра. Escape: \n \t \\ \". Комментарии // и условия [$WIN] пропускаются.
 *  Битый файл → FormatException (вызывающий решает, что делать).
 * ═══════════════════════════════════════
*/
