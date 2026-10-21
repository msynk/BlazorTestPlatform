namespace BlazorTestPlatform;

/// <summary>A browser viewport used for visual snapshots and responsive interaction tests.</summary>
/// <param name="Name">Stable name used in snapshot file names (e.g. <c>desktop</c>).</param>
/// <param name="Width">CSS pixel width.</param>
/// <param name="Height">CSS pixel height.</param>
/// <param name="DeviceScaleFactor">Device pixel ratio. Keep at 1 for small, stable baselines unless you test HiDPI assets.</param>
/// <param name="IsMobile">Emulates the mobile meta-viewport behaviour.</param>
/// <param name="HasTouch">Enables touch events.</param>
public sealed record Viewport(string Name, int Width, int Height, double DeviceScaleFactor = 1, bool IsMobile = false, bool HasTouch = false)
{
    /// <summary>Small phone (360 x 740).</summary>
    public static Viewport MobileSmall { get; } = new("mobile-small", 360, 740, IsMobile: true, HasTouch: true);

    /// <summary>Typical phone (390 x 844).</summary>
    public static Viewport Mobile { get; } = new("mobile", 390, 844, IsMobile: true, HasTouch: true);

    /// <summary>Tablet portrait (768 x 1024).</summary>
    public static Viewport Tablet { get; } = new("tablet", 768, 1024, HasTouch: true);

    /// <summary>Laptop / desktop (1280 x 800).</summary>
    public static Viewport Desktop { get; } = new("desktop", 1280, 800);

    /// <summary>Full HD (1920 x 1080).</summary>
    public static Viewport Wide { get; } = new("wide", 1920, 1080);

    /// <summary>All built-in presets.</summary>
    public static IReadOnlyList<Viewport> Presets { get; } = [MobileSmall, Mobile, Tablet, Desktop, Wide];

    /// <summary>Finds a preset by name (case-insensitive).</summary>
    public static Viewport? FindPreset(string name) =>
        Presets.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Width}x{Height})";
}
