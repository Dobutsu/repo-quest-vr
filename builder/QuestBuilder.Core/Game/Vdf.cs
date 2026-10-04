using System.Text;

namespace RepoQuest.Builder.Game;

/// <summary>
/// Valve's text KeyValues format (libraryfolders.vdf, appmanifest_*.acf): "key" "value" pairs and "key" { ... } blocks.
/// Keys are case-insensitive, as in Steam.
/// </summary>
public sealed class VdfNode
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Value(string key) => Values.TryGetValue(key, out var value) ? value : null;
    public VdfNode? Child(string key) => Children.TryGetValue(key, out var child) ? child : null;

    public static VdfNode Parse(string text)
    {
        var tokens = Tokenize(text).GetEnumerator();
        var root = new VdfNode();
        ParseInto(root, tokens, topLevel: true);
        return root;
    }

    private static void ParseInto(VdfNode node, IEnumerator<string> tokens, bool topLevel)
    {
        while (tokens.MoveNext())
        {
            var key = tokens.Current;
            if (key == "}")
            {
                if (topLevel)
                    throw new FormatException("unexpected }");
                return;
            }
            if (!tokens.MoveNext())
                throw new FormatException($"no value for {key}");
            if (tokens.Current == "{")
            {
                var child = new VdfNode();
                ParseInto(child, tokens, topLevel: false);
                node.Children[key] = child;
            }
            else
            {
                node.Values[key] = tokens.Current;
            }
        }
        if (!topLevel)
            throw new FormatException("missing }");
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                    i++;
            }
            else if (c is '{' or '}')
            {
                i++;
                yield return c.ToString();
            }
            else if (c == '"')
            {
                var value = new StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        value.Append(text[i] switch { 'n' => '\n', 't' => '\t', var other => other });
                    }
                    else
                    {
                        value.Append(text[i]);
                    }
                    i++;
                }
                i++;
                yield return value.ToString();
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"'))
                    i++;
                yield return text[start..i];
            }
        }
    }
}
