using System.Text;

namespace BlazorTestPlatform;

/// <summary>
/// A boolean filter over story tags, as Vitest's <c>--tags-filter</c>, pytest's <c>-m</c> or NUnit's category
/// expressions: <c>smoke &amp;&amp; !visual</c>, <c>(forms || dialogs) and not slow</c>, <c>a11y-*</c>. Operators are
/// <c>!</c>/<c>not</c>, <c>&amp;&amp;</c>/<c>and</c>, <c>||</c>/<c>or</c>/<c>,</c> (in that precedence) and parentheses;
/// <c>*</c> in a tag matches any characters. Tags compare case-insensitively, like <see cref="Story.Tags"/>.
/// </summary>
public sealed class TagExpression
{
    private readonly Node _root;

    private TagExpression(string text, Node root)
    {
        Text = text;
        _root = root;
    }

    /// <summary>The expression as written.</summary>
    public string Text { get; }

    /// <summary>Parses an expression; throws <see cref="FormatException"/> with the position of a syntax error.</summary>
    public static TagExpression Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var parser = new Parser(expression);
        var root = parser.ParseOr();
        if (!parser.AtEnd)
        {
            throw parser.Error("expected an operator or the end of the expression");
        }

        return new TagExpression(expression.Trim(), root);
    }

    /// <summary>True when <paramref name="tags"/> satisfy the expression.</summary>
    public bool Matches(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var set = tags as IReadOnlySet<string> ?? tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _root.Evaluate(set);
    }

    /// <summary>True when the story's tags satisfy the expression.</summary>
    public bool Matches(Story story) => Matches((story ?? throw new ArgumentNullException(nameof(story))).Tags);

    /// <inheritdoc />
    public override string ToString() => Text;

    private abstract class Node
    {
        public abstract bool Evaluate(IReadOnlySet<string> tags);
    }

    private sealed class TagNode(string tag) : Node
    {
        private readonly System.Text.RegularExpressions.Regex? _pattern = tag.Contains('*', StringComparison.Ordinal)
            ? new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(tag).Replace("\\*", ".*", StringComparison.Ordinal) + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            : null;

        public override bool Evaluate(IReadOnlySet<string> tags) =>
            _pattern is null
                ? tags.Contains(tag) || tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
                : tags.Any(_pattern.IsMatch);
    }

    private sealed class NotNode(Node operand) : Node
    {
        public override bool Evaluate(IReadOnlySet<string> tags) => !operand.Evaluate(tags);
    }

    private sealed class AndNode(Node left, Node right) : Node
    {
        public override bool Evaluate(IReadOnlySet<string> tags) => left.Evaluate(tags) && right.Evaluate(tags);
    }

    private sealed class OrNode(Node left, Node right) : Node
    {
        public override bool Evaluate(IReadOnlySet<string> tags) => left.Evaluate(tags) || right.Evaluate(tags);
    }

    private sealed class Parser(string text)
    {
        private int _position;

        public bool AtEnd
        {
            get
            {
                SkipWhitespace();
                return _position >= text.Length;
            }
        }

        public Node ParseOr()
        {
            var left = ParseAnd();
            while (TryOperator("||") || TryOperator(",") || TryWord("or"))
            {
                left = new OrNode(left, ParseAnd());
            }

            return left;
        }

        public FormatException Error(string problem) =>
            new($"Invalid tag expression '{text}' at position {_position + 1}: {problem}. Use tags with !/not, &&/and, ||/or/, and parentheses, such as 'smoke && !visual'.");

        private Node ParseAnd()
        {
            var left = ParseNot();
            while (TryOperator("&&") || TryWord("and"))
            {
                left = new AndNode(left, ParseNot());
            }

            return left;
        }

        private Node ParseNot()
        {
            if (TryOperator("!") || TryWord("not"))
            {
                return new NotNode(ParseNot());
            }

            if (TryOperator("("))
            {
                var inner = ParseOr();
                if (!TryOperator(")"))
                {
                    throw Error("expected ')'");
                }

                return inner;
            }

            SkipWhitespace();
            var tag = new StringBuilder();
            while (_position < text.Length && IsTagCharacter(text[_position]))
            {
                tag.Append(text[_position++]);
            }

            if (tag.Length == 0 || tag.ToString() is "and" or "or" or "not")
            {
                throw Error(_position >= text.Length ? "expected a tag, but the expression ended" : $"expected a tag, found '{text[_position]}'");
            }

            return new TagNode(tag.ToString());
        }

        private static bool IsTagCharacter(char c) => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '/' or '@' or '*' or '+' or '#';

        private bool TryOperator(string op)
        {
            SkipWhitespace();
            if (string.CompareOrdinal(text, _position, op, 0, op.Length) != 0)
            {
                return false;
            }

            _position += op.Length;
            return true;
        }

        // A keyword only when it is a whole word: "android" is a tag, not "and" + "roid".
        private bool TryWord(string word)
        {
            SkipWhitespace();
            if (_position + word.Length > text.Length
                || string.Compare(text, _position, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0
                || (_position + word.Length < text.Length && IsTagCharacter(text[_position + word.Length])))
            {
                return false;
            }

            _position += word.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }
    }
}
