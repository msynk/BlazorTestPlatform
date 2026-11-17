using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorTestPlatform;

/// <summary>How <see cref="StoryContext.ExpectAriaSnapshotAsync"/> compares the accessibility tree with the expected one.</summary>
public enum AriaSnapshotMatch
{
    /// <summary>
    /// The trees are the same: every node, name and state, in order. Names may be patterns (<c>- link /Order \d+/</c>).
    /// </summary>
    Exact,

    /// <summary>
    /// The expected tree is contained in the actual one, as Playwright's <c>toMatchAriaSnapshot</c>: it may start at any
    /// depth, leave out nodes (the rest must keep their order), names (any name matches) and states (listed ones must
    /// hold). A <c>- /children: equal</c> line under a node requires exactly the listed children.
    /// </summary>
    Contains,
}

/// <summary>
/// Parses and compares accessibility tree snapshots (the text <c>AriaSnapshot.Of</c> and the browser drivers produce),
/// independently of the driver: <c>- role "name" [state] [state]:</c> per node, children indented by two spaces. In
/// expected trees a name can also be a pattern, <c>/regex/flags</c> (flags <c>i</c>, <c>m</c>, <c>s</c>), matched against the
/// whole accessible name.
/// </summary>
public static class AriaSnapshotTemplate
{
    /// <summary>True when <paramref name="actual"/> matches <paramref name="expected"/> under <paramref name="match"/>.</summary>
    /// <exception cref="FormatException">A line of either snapshot cannot be parsed.</exception>
    public static bool Matches(string actual, string expected, AriaSnapshotMatch match = AriaSnapshotMatch.Exact)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        var actualNodes = Parse(actual, allowPatterns: false);
        var expectedNodes = Parse(expected, allowPatterns: true);
        if (match == AriaSnapshotMatch.Exact)
        {
            return ListEquals(actualNodes, expectedNodes, partial: false);
        }

        return Anywhere(actualNodes, expectedNodes);
    }

    /// <summary>Checks that <paramref name="snapshot"/> can be used as an expected tree; throws <see cref="FormatException"/> otherwise.</summary>
    public static void Validate(string snapshot) => Parse(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), allowPatterns: true);

    private static bool Anywhere(IReadOnlyList<Node> actual, IReadOnlyList<Node> expected)
    {
        if (Contains(actual, expected))
        {
            return true;
        }

        foreach (var node in actual)
        {
            if (Anywhere(node.Children, expected))
            {
                return true;
            }
        }

        return false;
    }

    // In-order subsequence; matching each expected node to the leftmost actual node that fits is optimal.
    private static bool Contains(IReadOnlyList<Node> actual, IReadOnlyList<Node> expected)
    {
        var next = 0;
        foreach (var wanted in expected)
        {
            while (next < actual.Count && !NodeMatches(actual[next], wanted, partial: true))
            {
                next++;
            }

            if (next == actual.Count)
            {
                return false;
            }

            next++;
        }

        return true;
    }

    private static bool ListEquals(IReadOnlyList<Node> actual, IReadOnlyList<Node> expected, bool partial) =>
        actual.Count == expected.Count && actual.Zip(expected).All(pair => NodeMatches(pair.First, pair.Second, partial));

    private static bool NodeMatches(Node actual, Node expected, bool partial)
    {
        if (!string.Equals(actual.Role, expected.Role, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var nameOk = expected.Pattern is { } pattern
            ? pattern.IsMatch(actual.Name ?? string.Empty)
            : expected.Name is { } name
                ? string.Equals(actual.Name, name, StringComparison.Ordinal)
                : partial || actual.Name is null;
        if (!nameOk)
        {
            return false;
        }

        var statesOk = partial
            ? expected.States.All(actual.States.Contains)
            : expected.States.Count == actual.States.Count && expected.States.All(actual.States.Contains);
        if (!statesOk)
        {
            return false;
        }

        return (expected.ChildrenMode ?? (partial ? ChildrenMode.Contain : ChildrenMode.Equal)) switch
        {
            ChildrenMode.Equal => ListEquals(actual.Children, expected.Children, partial),
            _ => Contains(actual.Children, expected.Children),
        };
    }

    private static List<Node> Parse(string snapshot, bool allowPatterns)
    {
        var lines = StoryContext.NormalizeSnapshot(snapshot).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var roots = new List<Node>();
        var stack = new List<(int Indent, Node Node)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var indent = line.Length - line.TrimStart(' ').Length;
            var content = line.TrimStart(' ');
            if (!content.StartsWith("- ", StringComparison.Ordinal))
            {
                throw Error(i, line, "a node starts with \"- \"");
            }

            while (stack.Count > 0 && stack[^1].Indent >= indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var parent = stack.Count > 0 ? stack[^1].Node : null;
            var body = content[2..];
            if (body.StartsWith("/children:", StringComparison.Ordinal))
            {
                if (!allowPatterns || parent is null)
                {
                    throw Error(i, line, "/children belongs under a node of an expected tree");
                }

                parent.ChildrenMode = body["/children:".Length..].Trim() switch
                {
                    "equal" => ChildrenMode.Equal,
                    "contain" => ChildrenMode.Contain,
                    var other => throw Error(i, line, $"/children is 'equal' or 'contain', not '{other}'"),
                };
                continue;
            }

            var node = ParseNode(body, allowPatterns, i, line);
            (parent?.Children ?? roots).Add(node);
            stack.Add((indent, node));
        }

        return roots;
    }

    private static Node ParseNode(string body, bool allowPatterns, int index, string line)
    {
        var position = 0;
        var roleEnd = position;
        while (roleEnd < body.Length && body[roleEnd] is not (' ' or ':' or '['))
        {
            roleEnd++;
        }

        if (roleEnd == 0)
        {
            throw Error(index, line, "a node starts with its role");
        }

        var node = new Node { Role = body[..roleEnd] };
        position = SkipSpaces(body, roleEnd);
        if (position < body.Length && body[position] == '"')
        {
            var name = new StringBuilder();
            position++;
            while (true)
            {
                if (position >= body.Length)
                {
                    throw Error(index, line, "the name's closing quote is missing");
                }

                var ch = body[position++];
                if (ch == '"')
                {
                    break;
                }

                if (ch == '\\' && position < body.Length)
                {
                    ch = body[position++];
                }

                name.Append(ch);
            }

            node.Name = name.ToString();
        }
        else if (position < body.Length && body[position] == '/')
        {
            if (!allowPatterns)
            {
                throw Error(index, line, "a pattern can only appear in an expected tree");
            }

            var end = position + 1;
            var source = new StringBuilder();
            while (end < body.Length && body[end] != '/')
            {
                if (body[end] == '\\' && end + 1 < body.Length)
                {
                    source.Append(body[end]);
                    end++;
                }

                source.Append(body[end]);
                end++;
            }

            if (end >= body.Length)
            {
                throw Error(index, line, "the pattern's closing / is missing");
            }

            end++;
            var options = RegexOptions.CultureInvariant;
            while (end < body.Length && char.IsAsciiLetter(body[end]))
            {
                options |= body[end] switch
                {
                    'i' => RegexOptions.IgnoreCase,
                    'm' => RegexOptions.Multiline,
                    's' => RegexOptions.Singleline,
                    var flag => throw Error(index, line, $"unknown pattern flag '{flag}'"),
                };
                end++;
            }

            try
            {
                node.Pattern = new Regex(source.ToString(), options, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                throw Error(index, line, "invalid pattern: " + ex.Message);
            }

            position = end;
        }

        position = SkipSpaces(body, position);
        while (position < body.Length && body[position] == '[')
        {
            var close = body.IndexOf(']', position);
            if (close < 0)
            {
                throw Error(index, line, "a state's closing ] is missing");
            }

            node.States.Add(body[(position + 1)..close].Trim());
            position = SkipSpaces(body, close + 1);
        }

        if (position < body.Length && body[position] == ':')
        {
            position++;
        }

        if (SkipSpaces(body, position) != body.Length)
        {
            throw Error(index, line, $"unexpected text '{body[position..].Trim()}'");
        }

        return node;
    }

    private static int SkipSpaces(string text, int position)
    {
        while (position < text.Length && text[position] == ' ')
        {
            position++;
        }

        return position;
    }

    private static FormatException Error(int index, string line, string problem) =>
        new(string.Create(CultureInfo.InvariantCulture, $"Accessibility snapshot line {index + 1} ('{line.Trim()}'): {problem}."));

    private enum ChildrenMode
    {
        Contain,
        Equal,
    }

    private sealed class Node
    {
        public required string Role { get; init; }

        public string? Name { get; set; }

        public Regex? Pattern { get; set; }

        public HashSet<string> States { get; } = new(StringComparer.Ordinal);

        public List<Node> Children { get; } = [];

        public ChildrenMode? ChildrenMode { get; set; }
    }
}
