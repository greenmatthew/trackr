using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Shared.Auth;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// The optional body measurements, and what they estimate a day costs.
/// </summary>
/// <remarks>
/// CLAUDE.md section 9.13's second load-bearing half: measurements exist so that goals can suggest
/// a calorie target instead of asking somebody to invent one. Section 9.12 is equally clear that a
/// hand-typed target must work regardless, so <strong>every field here is optional and an empty
/// screen is a complete one</strong>.
/// <para>
/// Metric on the wire whatever is on screen. Feet, inches and pounds are converted here, because
/// two units for one column is how a weight becomes ambiguous - see
/// <see cref="Trackr.Shared.Nutrition.MeasurementSystem"/>.
/// </para>
/// <para>
/// The estimate is the server's. A phone computing its own would eventually disagree with the one
/// the goals screen offers, and the disagreement would be silent.
/// </para>
/// </remarks>
public sealed partial class BodyMetricsViewModel(ITrackrApiClient api, DisplayUnits units)
    : ObservableObject
{
    /// <summary>The choices, in the order the pickers show them.</summary>
    private static readonly BiologicalSex[] Sexes =
        [BiologicalSex.Unspecified, BiologicalSex.Female, BiologicalSex.Male];

    private static readonly ActivityLevel[] Activities =
    [
        ActivityLevel.Sedentary,
        ActivityLevel.Light,
        ActivityLevel.Moderate,
        ActivityLevel.Active,
        ActivityLevel.VeryActive
    ];

    public IReadOnlyList<string> SexNames { get; } = ["Prefer not to say", "Female", "Male"];

    public IReadOnlyList<string> ActivityNames { get; } =
    [
        "Not very active",
        "Lightly active (1-3 days a week)",
        "Moderately active (3-5 days)",
        "Very active (6-7 days)",
        "Extremely active (daily, or a physical job)"
    ];

    /// <summary>Whether height and weight are typed in feet and pounds.</summary>
    public bool IsImperial => units.IsImperial;

    public bool IsMetric => !units.IsImperial;

    [ObservableProperty]
    public partial int SelectedSex { get; set; }

    [ObservableProperty]
    public partial int SelectedActivity { get; set; }

    /// <summary>
    /// Whether a birth date has been given at all.
    /// </summary>
    /// <remarks>
    /// Separate from the date because a date picker always has a value, and "today" is not an
    /// answer anybody meant to give. Nothing is sent unless this is on.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool HasBirthDate { get; set; }

    [ObservableProperty]
    public partial DateTime BirthDate { get; set; } = new(1990, 1, 1);

    /// <summary>Centimetres. Shown only when the account reads metric.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string HeightCmText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string HeightFeetText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string HeightInchesText { get; set; } = string.Empty;

    /// <summary>Kilograms or pounds, depending on the account's preference.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string WeightText { get; set; } = string.Empty;

    public string WeightLabel => IsImperial ? "Weight (lb)" : "Weight (kg)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    [ObservableProperty]
    public partial bool IsSaved { get; set; }

    /// <summary>
    /// What the measurements estimate, or null when there is not enough to estimate from.
    /// </summary>
    /// <remarks>
    /// Null is the ordinary state and never an error. Declining to guess is the point: a figure
    /// built on a height nobody entered would be an invented number wearing the authority of a
    /// calculation.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEstimate))]
    [NotifyPropertyChangedFor(nameof(BasalDescription))]
    [NotifyPropertyChangedFor(nameof(MaintenanceDescription))]
    public partial EnergyEstimateResponse? Estimate { get; set; }

    public bool HasEstimate => Estimate is not null;

    /// <summary>Drawn in the account's own energy unit, like every other figure in the app.</summary>
    public string BasalDescription =>
        Estimate is null ? string.Empty : units.FormatEnergy(Estimate.BasalKcal);

    public string MaintenanceDescription =>
        Estimate is null ? string.Empty : units.FormatEnergy(Estimate.MaintenanceKcal);

    /// <summary>Whether every filled-in field reads as a number.</summary>
    /// <remarks>
    /// Blank is always allowed - it means "I am not telling you" - but "72kg" with the unit typed
    /// in is a mistake worth pointing at rather than a field to quietly clear.
    /// </remarks>
    public bool CanSave =>
        !IsBusy
        && Numbers.TryReadOptional(HeightCmText, out _)
        && Numbers.TryReadOptional(HeightFeetText, out _)
        && Numbers.TryReadOptional(HeightInchesText, out _)
        && Numbers.TryReadOptional(WeightText, out _);

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        Problem = null;

        try
        {
            var profile = await api.GetProfileAsync();

            if (profile is null)
            {
                Problem = "Could not reach the server, so your measurements are not known.";

                return;
            }

            Show(profile);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (!CanSave)
        {
            return;
        }

        IsBusy = true;
        Problem = null;
        IsSaved = false;

        try
        {
            var saved = await api.SaveBodyMetricsAsync(new SaveBodyMetricsRequest
            {
                Sex = Sexes[Math.Clamp(SelectedSex, 0, Sexes.Length - 1)],
                BirthDate = HasBirthDate ? DateOnly.FromDateTime(BirthDate) : null,
                HeightCm = ReadHeightCm(),
                WeightKg = ReadWeightKg(),
                ActivityLevel = Activities[Math.Clamp(SelectedActivity, 0, Activities.Length - 1)]
            });

            if (saved is null)
            {
                Problem = "That could not be saved, so nothing has changed.";

                return;
            }

            // Redrawn from what the server stored rather than from what was typed, so a rounded
            // height shows as the height that will be used from now on.
            Show(saved);

            IsSaved = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Removes every measurement.
    /// </summary>
    /// <remarks>
    /// The reason the write is wholesale rather than a merge. This is the most personal data the
    /// application holds, and being able to take it back out is not a feature to leave for later.
    /// </remarks>
    [RelayCommand]
    private async Task ClearAsync()
    {
        SelectedSex = 0;
        SelectedActivity = 0;
        HasBirthDate = false;
        HeightCmText = string.Empty;
        HeightFeetText = string.Empty;
        HeightInchesText = string.Empty;
        WeightText = string.Empty;

        await SaveAsync();
    }

    private void Show(ProfileResponse profile)
    {
        var body = profile.Body;

        SelectedSex = Math.Max(0, Array.IndexOf(Sexes, body.Sex));
        SelectedActivity = Math.Max(0, Array.IndexOf(Activities, body.ActivityLevel));

        HasBirthDate = body.BirthDate is not null;

        if (body.BirthDate is { } born)
        {
            BirthDate = born.ToDateTime(TimeOnly.MinValue);
        }

        ShowHeight(body.HeightCm);
        ShowWeight(body.WeightKg);

        Estimate = profile.Energy;

        OnPropertyChanged(nameof(IsImperial));
        OnPropertyChanged(nameof(IsMetric));
        OnPropertyChanged(nameof(WeightLabel));
        OnPropertyChanged(nameof(CanSave));
    }

    private void ShowHeight(decimal? heightCm)
    {
        HeightCmText = string.Empty;
        HeightFeetText = string.Empty;
        HeightInchesText = string.Empty;

        if (heightCm is not { } height)
        {
            return;
        }

        if (!IsImperial)
        {
            HeightCmText = Text(height);

            return;
        }

        var (feet, inches) = UnitConversion.CentimetresToFeetAndInches(height);

        // The inches are rounded to draw, and rolled up when that produces twelve - which is the
        // classic way "5 ft 12 in" reaches a screen.
        var whole = decimal.Round(inches, 0, MidpointRounding.AwayFromZero);

        if (whole >= UnitConversion.InchesPerFoot)
        {
            feet++;
            whole = 0m;
        }

        HeightFeetText = Text(feet);
        HeightInchesText = Text(whole);
    }

    private void ShowWeight(decimal? weightKg)
    {
        WeightText = weightKg is { } weight
            ? Text(IsImperial
                ? decimal.Round(UnitConversion.KilogramsToPounds(weight), 1, MidpointRounding.AwayFromZero)
                : weight)
            : string.Empty;
    }

    private decimal? ReadHeightCm()
    {
        if (!IsImperial)
        {
            return Numbers.TryReadOptional(HeightCmText, out var centimetres) ? centimetres : null;
        }

        Numbers.TryReadOptional(HeightFeetText, out var feet);
        Numbers.TryReadOptional(HeightInchesText, out var inches);

        if (feet is null && inches is null)
        {
            return null;
        }

        return decimal.Round(
            UnitConversion.FeetAndInchesToCentimetres((int)(feet ?? 0m), inches ?? 0m),
            1,
            MidpointRounding.AwayFromZero);
    }

    private decimal? ReadWeightKg()
    {
        if (!Numbers.TryReadOptional(WeightText, out var weight) || weight is not { } value)
        {
            return null;
        }

        return IsImperial
            ? decimal.Round(UnitConversion.PoundsToKilograms(value), 1, MidpointRounding.AwayFromZero)
            : value;
    }

    private static string Text(decimal value) => value.ToString("0.####", CultureInfo.CurrentCulture);

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSave));

    partial void OnHeightCmTextChanged(string value) => IsSaved = false;

    partial void OnWeightTextChanged(string value) => IsSaved = false;
}
