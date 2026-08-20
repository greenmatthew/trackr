namespace Trackr.Mobile.Tests;

/// <summary>
/// A clock that does not move, so "2 days ago" is a thing a test can assert.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from Microsoft.Extensions.TimeProvider.Testing: one overridden
/// method is a smaller thing to own than a package reference, and nothing here needs a timer.
/// </remarks>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
