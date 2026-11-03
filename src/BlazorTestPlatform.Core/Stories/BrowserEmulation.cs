namespace BlazorTestPlatform;

/// <summary>A geographic position reported by the emulated <c>navigator.geolocation</c>.</summary>
/// <param name="Latitude">Latitude in degrees, between -90 and 90.</param>
/// <param name="Longitude">Longitude in degrees, between -180 and 180.</param>
/// <param name="Accuracy">Accuracy in meters (0 or more).</param>
public sealed record GeoPosition(double Latitude, double Longitude, double Accuracy = 0)
{
    /// <summary>Throws when a coordinate is out of range.</summary>
    internal GeoPosition Validated()
    {
        if (double.IsNaN(Latitude) || Latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(Latitude), Latitude, "Latitude must be between -90 and 90.");
        }

        if (double.IsNaN(Longitude) || Longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(Longitude), Longitude, "Longitude must be between -180 and 180.");
        }

        return double.IsNaN(Accuracy) || Accuracy < 0
            ? throw new ArgumentOutOfRangeException(nameof(Accuracy), Accuracy, "Accuracy must be 0 or more.")
            : this;
    }
}

/// <summary>
/// Browser APIs and network conditions a story emulates in a real browser (<see cref="Drivers.DriverCapabilities.BrowserEmulation"/>):
/// the geolocation position, granted permissions and added network latency. Drivers without the capability (bUnit, the
/// in-app driver) report such stories as skipped. Offline mode is switched from play with <c>ctx.SetOfflineAsync</c>,
/// because the page must load first.
/// </summary>
public sealed record BrowserEmulation
{
    /// <summary>No emulation.</summary>
    public static BrowserEmulation None { get; } = new();

    /// <summary>The position <c>navigator.geolocation</c> reports (the <c>geolocation</c> permission is granted with it).</summary>
    public GeoPosition? Geolocation { get; init; }

    /// <summary>
    /// Permissions granted to the page, by their Permissions API names (<c>geolocation</c>, <c>notifications</c>,
    /// <c>clipboard-read</c>...). Browsers support different sets; an unsupported name fails the run.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>Latency added to every HTTP request the page makes (not to established WebSocket connections).</summary>
    public TimeSpan Latency { get; init; }

    /// <summary>True when nothing is emulated.</summary>
    public bool IsEmpty => Geolocation is null && Permissions.Count == 0 && Latency == TimeSpan.Zero;
}
