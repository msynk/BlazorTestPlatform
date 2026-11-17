namespace BlazorTestPlatform;

/// <summary>Time zones of stories (<c>TimeZone(...)</c>): validation and the <see cref="TimeProvider"/> that applies one.</summary>
public static class StoryTimeZone
{
    /// <summary>
    /// The IANA id of <paramref name="timeZoneId"/> (an IANA or Windows id), as browsers need it. Throws
    /// <see cref="ArgumentException"/> for a zone this machine does not know.
    /// </summary>
    public static string Normalize(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        var id = timeZoneId.Trim();
        if (string.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "Etc/UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "UTC";
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            throw new ArgumentException($"Unknown time zone '{timeZoneId}'. Use an IANA id such as 'Europe/Berlin'.", nameof(timeZoneId));
        }

        if (zone.HasIanaId)
        {
            return zone.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana)
            ? iana
            : throw new ArgumentException($"Time zone '{timeZoneId}' has no IANA id, which browsers need.", nameof(timeZoneId));
    }

    /// <summary>The system clock with another local time zone.</summary>
    internal sealed class ZonedTimeProvider(TimeZoneInfo zone) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => zone;
    }
}
