namespace Trackr.Api.Data;

/// <summary>
/// Rounds a number to what its column will actually keep, before it is written.
/// </summary>
/// <remarks>
/// The same class of bug as <see cref="Time.Timestamps.ToStorablePrecision"/>, in a different
/// currency. A nutrient amount is <c>numeric(12,4)</c>: hand Postgres 12.34567 and it stores
/// 12.3457, so a handler that echoed the request's own value back would report a number the next
/// GET disagrees with. Rounding here means the response to a write is literally what was written.
/// <para>
/// Away from zero, which is what Postgres' <c>numeric</c> does - banker's rounding would agree
/// with the database on most values and quietly differ on the halfway ones.
/// </para>
/// <para>
/// This matters most for the log, where quantity multiplication routinely produces more decimal
/// places than either operand had: 2.5 servings of 3.3333 g is 8.33325 g, which stores as 8.3333.
/// </para>
/// </remarks>
public static class StoredPrecision
{
    /// <summary>A nutrient amount or an energy value - <c>numeric(12,4)</c>.</summary>
    public static decimal Amount(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    /// <summary>A serving size or a quantity - <c>numeric(10,3)</c>.</summary>
    public static decimal Measure(decimal value) => decimal.Round(value, 3, MidpointRounding.AwayFromZero);
}
