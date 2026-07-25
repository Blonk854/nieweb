using System.Globalization;

namespace Nieweb.Pdf;

/// <summary>
/// Shared timestamp / subtitle helpers for every Nieweb PDF renderer.
/// Renderers used to hard-code UTC and format via
/// <c>PanelYieldPdfRenderer.FormatUtc</c>; that coupling made the
/// per-tenant time-zone story impossible. Every timestamp in a
/// generated PDF (header, subtitle, meta table, body) now goes
/// through <see cref="FormatInstant"/>.
/// </summary>
public static class NiewebPdfTimestamps
{
    /// <summary>
    /// Format a UTC instant in <paramref name="timeZone"/> as
    /// <c>yyyy-MM-dd HH:mm</c> followed by a zone suffix
    /// (<c>UTC</c> when the zone is UTC, otherwise the signed
    /// <c>UTC±hh:mm</c> offset in force at that instant). Invariant
    /// culture always; PDFs must be stable regardless of the
    /// server's <c>CurrentCulture</c>.
    /// </summary>
    public static string FormatInstant(DateTimeOffset instant, TimeZoneInfo? timeZone)
    {
        var tz = timeZone ?? TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(instant, tz);
        var suffix = FormatZoneSuffix(tz, local);
        return string.Create(CultureInfo.InvariantCulture,
            $"{local:yyyy-MM-dd HH:mm} {suffix}");
    }

    /// <summary>
    /// Returns the zone suffix used by <see cref="FormatInstant"/> in
    /// isolation, so callers that want to attach the suffix once to
    /// a compact range (<c>… → … UTC-05:00</c>) can avoid printing
    /// it twice.
    /// </summary>
    public static string FormatZoneSuffix(TimeZoneInfo? timeZone, DateTimeOffset at)
    {
        var tz = timeZone ?? TimeZoneInfo.Utc;
        if (tz.Equals(TimeZoneInfo.Utc) || tz.BaseUtcOffset == TimeSpan.Zero)
        {
            return "UTC";
        }
        var offset = tz.GetUtcOffset(at);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture,
            $"UTC{sign}{abs.Hours:00}:{abs.Minutes:00}");
    }

    /// <summary>
    /// <c>yyyy-MM-dd HH:mm</c> (no zone suffix). Use for the two
    /// endpoints of a range where a single trailing suffix is
    /// preferred (see <see cref="FormatRange"/>).
    /// </summary>
    public static string FormatWallClock(DateTimeOffset instant, TimeZoneInfo? timeZone)
    {
        var tz = timeZone ?? TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(instant, tz);
        return string.Create(CultureInfo.InvariantCulture, $"{local:yyyy-MM-dd HH:mm}");
    }

    /// <summary>
    /// Compact range: <c>yyyy-MM-dd HH:mm → yyyy-MM-dd HH:mm ZONE</c>.
    /// The zone suffix is taken from <paramref name="endInstant"/>
    /// (matches the human reading of "the window ends at X".)
    /// </summary>
    public static string FormatRange(
        DateTimeOffset startInstant,
        DateTimeOffset endInstant,
        TimeZoneInfo? timeZone)
    {
        var tz = timeZone ?? TimeZoneInfo.Utc;
        var start = FormatWallClock(startInstant, tz);
        var end = FormatWallClock(endInstant, tz);
        var suffix = FormatZoneSuffix(tz, endInstant);
        return string.Create(CultureInfo.InvariantCulture, $"{start} → {end} {suffix}");
    }

    /// <summary>
    /// Joins non-empty subtitle parts with the corporate mid-dot
    /// separator used across every renderer.
    /// </summary>
    public static string FormatSubtitle(params string[] parts)
        => string.Join("   ·   ", parts.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>
    /// Resolves an optional IANA (<c>Europe/Paris</c>) or Windows
    /// (<c>Romance Standard Time</c>) time-zone id to a
    /// <see cref="TimeZoneInfo"/>. Returns <see cref="TimeZoneInfo.Utc"/>
    /// when <paramref name="id"/> is null, empty, or does not resolve
    /// — PDF rendering must never fail on a bad zone id.
    /// </summary>
    public static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Utc;
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
