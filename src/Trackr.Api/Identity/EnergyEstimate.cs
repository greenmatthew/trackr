using Trackr.Shared.Auth;

namespace Trackr.Api.Identity;

/// <summary>
/// What an account's body measurements imply about a day's energy.
/// </summary>
/// <remarks>
/// CLAUDE.md section 9.13: "Body metrics feed the goal maths. Goals can suggest a calorie target
/// from BMR/TDEE instead of asking the user to invent a number." This is that arithmetic, and
/// section 9.12 is equally clear about its standing - <strong>a hand-typed target must always
/// work</strong>, so nothing here is ever required and an account with no measurements is a
/// complete account with no estimate.
/// <para>
/// On the server rather than in <c>Trackr.Shared</c>, for the reason goal progress is measured
/// here: one answer. A phone and a browser computing their own would eventually disagree about
/// somebody's maintenance calories, and the disagreement would be silent.
/// </para>
/// <para>
/// <strong>Mifflin-St Jeor</strong> rather than Harris-Benedict, which it superseded, or
/// Katch-McArdle, which needs a body-fat percentage nobody has to hand. It is the equation clinical
/// guidance uses for people without a metabolic cart, and it is still an estimate: two people
/// matching on every input here can differ by a couple of hundred kilocalories a day. The screen
/// says so, and the number is offered rather than applied.
/// </para>
/// </remarks>
public static class EnergyEstimate
{
    /// <summary>The oldest birth date that is taken seriously, as an age in years.</summary>
    /// <remarks>
    /// Not a real bound on human life - a guard on a mistyped year. An age of 1 924 produces a
    /// negative basal rate, and a negative calorie target is worse than none.
    /// </remarks>
    private const int MaximumAge = 120;

    /// <summary>Below this the equation is not the right one - children have their own.</summary>
    private const int MinimumAge = 15;

    /// <summary>
    /// Basal and maintenance energy, or null if a needed measurement is missing.
    /// </summary>
    /// <remarks>
    /// Null is the ordinary case and never an error. A refusal to guess: an estimate built on a
    /// height nobody entered would be an invented number wearing the authority of a calculation,
    /// which is the one thing this project will not put in front of somebody's day.
    /// </remarks>
    /// <param name="today">
    /// The account's local today, so a birthday turns over on the account's own midnight - the same
    /// day boundary every total in Trackr uses.
    /// </param>
    public static EnergyEstimateResponse? For(
        BiologicalSex sex,
        DateOnly? birthDate,
        decimal? heightCm,
        decimal? weightKg,
        ActivityLevel activity,
        DateOnly today)
    {
        if (sex is BiologicalSex.Unspecified
            || birthDate is not { } born
            || heightCm is not { } height
            || weightKg is not { } weight)
        {
            return null;
        }

        var age = AgeOn(born, today);

        if (age is null)
        {
            return null;
        }

        // Mifflin-St Jeor. The two constants are the whole of what the sex term does, which is why
        // BiologicalSex is documented as a coefficient rather than as a profile field.
        var basal =
            (10m * weight)
            + (6.25m * height)
            - (5m * age.Value)
            + (sex is BiologicalSex.Male ? 5m : -161m);

        if (basal <= 0m)
        {
            return null;
        }

        // Whole kilocalories. The equation's own error bar is in the hundreds, so decimal places
        // here would be precision the number does not have.
        return new EnergyEstimateResponse(
            decimal.Round(basal, 0, MidpointRounding.AwayFromZero),
            decimal.Round(basal * MultiplierFor(activity), 0, MidpointRounding.AwayFromZero));
    }

    /// <summary>The usual five-step activity factors, applied to the basal rate.</summary>
    public static decimal MultiplierFor(ActivityLevel activity) => activity switch
    {
        ActivityLevel.Light => 1.375m,
        ActivityLevel.Moderate => 1.55m,
        ActivityLevel.Active => 1.725m,
        ActivityLevel.VeryActive => 1.9m,
        _ => 1.2m
    };

    /// <summary>Whole years, or null when the date is not one a person could have been born on.</summary>
    private static int? AgeOn(DateOnly born, DateOnly today)
    {
        if (born > today)
        {
            return null;
        }

        var age = today.Year - born.Year;

        if (born > today.AddYears(-age))
        {
            age--;
        }

        return age is < MinimumAge or > MaximumAge ? null : age;
    }
}
