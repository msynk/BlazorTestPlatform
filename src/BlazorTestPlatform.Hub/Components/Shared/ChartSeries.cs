namespace BlazorTestPlatform.Hub.Components.Shared;

/// <summary>One line of a <see cref="TrendChart"/>.</summary>
/// <param name="Name">Legend name.</param>
/// <param name="Css">Tone: <c>pass</c>, <c>fail</c>, <c>flaky</c> or <c>accent</c>.</param>
/// <param name="Values">Values, one per label.</param>
public sealed record ChartSeries(string Name, string Css, IReadOnlyList<double> Values);
