using System.Globalization;
using System.Text;

namespace BlazorTestPlatform;

/// <summary>
/// Small, dependency-free, deterministic fake-data generator for stories and fixtures.
/// Every value derives from a <see cref="DeterministicRandom"/>, so screenshots and assertions stay stable.
/// Bring your own generator (Bogus, AutoFixture...) for richer data; seed it from <see cref="DeterministicRandom.Seed"/>.
/// </summary>
public sealed class FakeData
{
    private static readonly string[] FirstNames =
    [
        "Ada", "Alan", "Grace", "Linus", "Margaret", "Dennis", "Barbara", "Ken", "Frances", "Edsger",
        "Radia", "Tim", "Hedy", "Donald", "Katherine", "Niklaus", "Sophie", "Bjarne", "Annie", "Anders",
    ];

    private static readonly string[] LastNames =
    [
        "Lovelace", "Turing", "Hopper", "Torvalds", "Hamilton", "Ritchie", "Liskov", "Thompson", "Allen", "Dijkstra",
        "Perlman", "Berners-Lee", "Lamarr", "Knuth", "Johnson", "Wirth", "Wilson", "Stroustrup", "Easley", "Hejlsberg",
    ];

    private static readonly string[] Companies =
    [
        "Contoso", "Fabrikam", "Northwind Traders", "Adventure Works", "Tailspin Toys", "Wide World Importers",
        "Litware", "Proseware", "Woodgrove Bank", "Alpine Ski House",
    ];

    private static readonly string[] Cities =
    [
        "Amsterdam", "Berlin", "Copenhagen", "Dublin", "Helsinki", "Lisbon", "Madrid", "Oslo", "Seattle", "Toronto",
        "Sydney", "Tokyo", "Nairobi", "Santiago", "Vancouver",
    ];

    private static readonly string[] Words =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "do", "eiusmod",
        "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua", "enim", "minim", "veniam",
        "quis", "nostrud", "exercitation", "ullamco", "laboris", "nisi", "aliquip", "commodo", "consequat",
    ];

    // RFC 2606 / RFC 6761 names that can never belong to anyone.
    private static readonly string[] Domains = ["example.com", "example.org", "example.net", "mail.example"];

    /// <summary>Creates a generator for the given seed.</summary>
    public FakeData(int seed)
        : this(new DeterministicRandom(seed))
    {
    }

    /// <summary>Creates a generator backed by an existing random source.</summary>
    public FakeData(DeterministicRandom random)
    {
        Random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>The underlying random source.</summary>
    public DeterministicRandom Random { get; }

    /// <summary>A first name.</summary>
    public string FirstName() => Random.Pick(FirstNames);

    /// <summary>A last name.</summary>
    public string LastName() => Random.Pick(LastNames);

    /// <summary>A full name.</summary>
    public string FullName() => $"{FirstName()} {LastName()}";

    /// <summary>A user name derived from a name.</summary>
    public string UserName(string? fullName = null)
    {
        fullName ??= FullName();
        return StoryId.Slugify(fullName).Replace("-", ".", StringComparison.Ordinal) + Random.Next(10, 99).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>An e-mail address on a reserved example domain (never deliverable).</summary>
    public string Email(string? fullName = null) => $"{UserName(fullName)}@{Random.Pick(Domains)}";

    /// <summary>A company name.</summary>
    public string Company() => Random.Pick(Companies);

    /// <summary>A city name.</summary>
    public string City() => Random.Pick(Cities);

    /// <summary>A North American phone number in the 555-0100 to 555-0199 block reserved for fiction, e.g. <c>+1-415-555-0142</c>.</summary>
    public string Phone() => string.Create(CultureInfo.InvariantCulture, $"+1-{Random.Next(201, 1000)}-555-01{Random.Next(0, 100):00}");

    /// <summary>A single lorem-ipsum word.</summary>
    public string Word() => Random.Pick(Words);

    /// <summary>A capitalized sentence with the given number of words.</summary>
    public string Sentence(int words = 8)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(words);
        var builder = new StringBuilder();
        for (var i = 0; i < words; i++)
        {
            var word = Word();
            if (i == 0)
            {
                word = char.ToUpperInvariant(word[0]) + word[1..];
            }
            else
            {
                builder.Append(' ');
            }

            builder.Append(word);
        }

        return builder.Append('.').ToString();
    }

    /// <summary>A paragraph made of sentences.</summary>
    public string Paragraph(int sentences = 4) =>
        string.Join(' ', Enumerable.Range(0, sentences).Select(_ => Sentence(Random.Next(5, 12))));

    /// <summary>A price in <c>[min, max)</c> with two decimals.</summary>
    public decimal Price(decimal min = 1, decimal max = 1000) => Random.NextDecimal(min, max);

    /// <summary>A date between <paramref name="from"/> and <paramref name="to"/>.</summary>
    public DateTimeOffset Date(DateTimeOffset from, DateTimeOffset to) => Random.NextDateTime(from, to);

    /// <summary>A deterministic identifier.</summary>
    public Guid Id() => Random.NextGuid();

    /// <summary>Creates <paramref name="count"/> items with an indexed factory.</summary>
    public IReadOnlyList<T> Many<T>(int count, Func<FakeData, int, T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var items = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(factory(this, i));
        }

        return items;
    }
}
