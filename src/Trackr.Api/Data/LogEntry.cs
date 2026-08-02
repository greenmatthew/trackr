using Trackr.Api.Identity;

namespace Trackr.Api.Data;

/// <summary>
/// One logging occasion - in the finished app, one confirmed chat card.
/// </summary>
/// <remarks>
/// Always personal: unlike <see cref="FoodItem"/> there is no shared form of this, so
/// <see cref="UserId"/> is required and cascades on delete.
/// </remarks>
public class LogEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public TrackrUser? User { get; set; }

    /// <summary>
    /// When the food was eaten. This is what the stats views aggregate on.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CreatedUtc"/> deliberately: milestone 14 lets someone correct "I
    /// actually ate this at 8am", which has to move the entry between days without lying about
    /// when the row was written.
    /// <para>
    /// Stored UTC. Which local day it falls in is decided by <c>Time/DayBoundary</c> and nowhere
    /// else, so that CLAUDE.md section 9.13's per-user time zone is one change rather than a
    /// rewrite of every aggregate.
    /// </para>
    /// </remarks>
    public DateTimeOffset LoggedUtc { get; set; }

    /// <summary>Whatever the user typed, kept as written.</summary>
    public string? Note { get; set; }

    /// <summary>Never empty: an entry with no items has nothing to say.</summary>
    public List<LogItem> Items { get; set; } = [];

    /// <summary>The photos that came with it, if any.</summary>
    public List<MealImage> Images { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
