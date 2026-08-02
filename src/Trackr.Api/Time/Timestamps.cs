namespace Trackr.Api.Time;

/// <summary>
/// Timestamp handling that every table has to get right the same way.
/// </summary>
public static class Timestamps
{
    private const long TicksPerMicrosecond = 10;

    /// <summary>
    /// Rounds down to the precision Postgres will actually keep.
    /// </summary>
    /// <remarks>
    /// A .NET tick is 100ns and a Postgres <c>timestamptz</c> holds microseconds, so a value
    /// written straight from <c>UtcNow</c> comes back one digit shorter than it went in. That
    /// matters wherever a client keeps the value a write returned and later compares it against
    /// one a read reports: without this the two never match, and every comparison looks like a
    /// change.
    /// <para>
    /// It was found by running the avatar upload, not by reading it (see
    /// <c>docs/decisions/06-mobile-ux.md</c>), and it lives here rather than beside that one
    /// endpoint because <c>LogEntry.LoggedUtc</c> has exactly the same exposure: the phone caches
    /// what a POST returned and a later GET disagrees in the last digit.
    /// </para>
    /// <para>
    /// Truncating rather than rounding, so a stored value is never ahead of the real one.
    /// </para>
    /// </remarks>
    public static DateTimeOffset ToStorablePrecision(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TicksPerMicrosecond), value.Offset);

    /// <summary>The current instant, already truncated to what the database will keep.</summary>
    public static DateTimeOffset UtcNow() => ToStorablePrecision(DateTimeOffset.UtcNow);
}
