using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.ViewModels.Chat;

/// <summary>
/// One analysed food on the confirmation card, with the numbers the user is allowed to correct.
/// </summary>
/// <remarks>
/// <strong>Every editable number is exposed as text, not as a decimal.</strong> Binding an
/// <c>Entry</c> straight to a <c>decimal</c> makes a half-typed value ("1." on the way to "1.5")
/// either an exception or a silent revert, and it gives no way to say a field is wrong. Text in,
/// parsed here, and <see cref="IsValid"/> is what the card's confirm button depends on - so a
/// number that could not be read stops the save rather than being quietly replaced with zero.
/// <para>
/// <strong>The values are per serving, and <see cref="QuantityText"/> is how many servings.</strong>
/// That is the shape <see cref="MealAnalysisItem"/> arrives in and the shape
/// <see cref="SaveLogItemRequest"/> wants, and the server does the multiplication. The totals below
/// are for reading only; nothing computed here is ever sent.
/// </para>
/// </remarks>
public sealed partial class ConfirmableItem : ObservableObject
{
    private readonly MealAnalysisItem analysed;

    public ConfirmableItem(
        MealAnalysisItem analysed,
        IReadOnlyDictionary<string, NutrientResponse>? nutrientCatalog)
    {
        this.analysed = analysed;

        Name = analysed.Name;
        QuantityText = Format(analysed.Quantity);
        EnergyText = Format(analysed.EnergyKcal);
        FatText = Format(analysed.FatG);
        CarbohydrateText = Format(analysed.CarbohydrateG);
        ProteinText = Format(analysed.ProteinG);

        Nutrients = BuildNutrientRows(analysed.Nutrients, nutrientCatalog);
    }

    public string Name { get; }

    public string? Brand => analysed.Brand;

    public bool HasBrand => !string.IsNullOrWhiteSpace(analysed.Brand);

    /// <summary>"per 30 g", or null when nobody could say what one serving is.</summary>
    /// <remarks>
    /// Null is normal for a plate of food rather than a packet, and is shown as nothing at all -
    /// inventing "per 1 serving" would read as a measurement that was never taken.
    /// </remarks>
    public string? ServingDescription =>
        analysed is { ServingSize: { } size, ServingUnit: { } unit }
            ? $"per {Format(size)} {unit}"
            : null;

    public bool HasServingDescription => ServingDescription is not null;

    /// <summary>
    /// Whether the server's cross-checks found something that did not add up.
    /// </summary>
    /// <remarks>
    /// The card must draw this differently from a normal item rather than as a footnote - milestone
    /// 8's validator is the main thing standing between a small model and a wrong number in the
    /// database, and rendering its verdict as decoration would waste it.
    /// </remarks>
    public bool IsLowConfidence => analysed.Confidence is AnalysisConfidence.Low;

    /// <summary>Where these numbers came from, in words - "Open Food Facts", "estimated from a photo".</summary>
    public string SourceDescription => analysed.Source switch
    {
        AnalyzedItemSource.Database => "from Open Food Facts",
        AnalyzedItemSource.DatabaseAndModel => "Open Food Facts, gaps estimated",
        AnalyzedItemSource.PreviouslyLogged => "logged before",
        _ => "estimated"
    };

    /// <summary>What happened to this item specifically. Shown without a tap when there is any.</summary>
    public IReadOnlyList<string> Warnings => analysed.Warnings;

    public bool HasWarnings => analysed.Warnings.Count > 0;

    /// <summary>
    /// Micronutrients this item actually reported, in label order.
    /// </summary>
    /// <remarks>
    /// Only what the source provided. A card padded with "-" rows for the two dozen nutrients
    /// nobody measured is worse than a short one, and would quietly teach the reader that absent
    /// means zero - see wiki/Nutrient-Reference.md.
    /// </remarks>
    public IReadOnlyList<NutrientRow> Nutrients { get; }

    public bool HasNutrients => Nutrients.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(TotalDescription))]
    public partial string QuantityText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(TotalDescription))]
    public partial string EnergyText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string FatText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string CarbohydrateText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string ProteinText { get; set; }

    /// <summary>
    /// Whether every edited field reads as a number the log would accept.
    /// </summary>
    /// <remarks>
    /// Quantity must be above zero because zero servings is not a thing that was eaten, and the
    /// server rejects it; the rest may be zero, because "known to be zero" is a real measurement.
    /// Negatives are refused throughout.
    /// </remarks>
    public bool IsValid =>
        TryRead(QuantityText, out var quantity)
        && quantity > 0
        && TryRead(EnergyText, out _)
        && TryRead(FatText, out _)
        && TryRead(CarbohydrateText, out _)
        && TryRead(ProteinText, out _);

    /// <summary>"2 x 78 kcal = 156 kcal", or null while the numbers do not read.</summary>
    public string? TotalDescription =>
        TryRead(QuantityText, out var quantity) && TryRead(EnergyText, out var energy) && quantity > 0
            ? $"{Format(quantity)} x {Format(energy)} kcal = {Format(quantity * energy)} kcal"
            : null;

    /// <summary>
    /// The analysed item with the user's corrections applied, ready to be copied onto a save request.
    /// </summary>
    /// <remarks>
    /// A <c>with</c> on the original rather than a new object: everything the user did not touch -
    /// the nutrient map, the barcode, the photo it came from - carries through untouched, which is
    /// what keeps <c>ToSaveLogItemRequest</c> a copy rather than a reconstruction.
    /// </remarks>
    public MealAnalysisItem Corrected => analysed with
    {
        Quantity = Read(QuantityText),
        EnergyKcal = Read(EnergyText),
        FatG = Read(FatText),
        CarbohydrateG = Read(CarbohydrateText),
        ProteinG = Read(ProteinText)
    };

    private static IReadOnlyList<NutrientRow> BuildNutrientRows(
        IReadOnlyDictionary<string, decimal> amounts,
        IReadOnlyDictionary<string, NutrientResponse>? catalog)
    {
        if (catalog is null)
        {
            // No names and no units to render against. Showing the raw keys would be worse than
            // showing nothing, and the calories and macros above do not need the catalog.
            return [];
        }

        return
        [
            .. amounts
                .Where(amount => catalog.ContainsKey(amount.Key))
                .Select(amount => (Nutrient: catalog[amount.Key], amount.Value))
                .Where(pair => !pair.Nutrient.IsCore)
                .OrderBy(pair => pair.Nutrient.SortOrder)
                .Select(pair => new NutrientRow(
                    pair.Nutrient.DisplayName,
                    $"{Format(pair.Value)} {UnitSymbol(pair.Nutrient.Unit)}"))
        ];
    }

    private static string UnitSymbol(NutrientUnit unit) => unit switch
    {
        NutrientUnit.Gram => "g",
        NutrientUnit.Milligram => "mg",
        NutrientUnit.Microgram => "µg",
        _ => "kcal"
    };

    /// <summary>
    /// Trailing zeros trimmed, so 78.00 reads as 78.
    /// </summary>
    /// <remarks>
    /// The current culture, because these are read by a person. Parsing accepts both this and the
    /// invariant form, so a value that round-trips through a keyboard laid out for another decimal
    /// separator still reads.
    /// </remarks>
    private static string Format(decimal value) =>
        value.ToString("0.####", CultureInfo.CurrentCulture);

    private static decimal Read(string? text) => TryRead(text, out var value) ? value : 0m;

    private static bool TryRead(string? text, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

        if (!decimal.TryParse(text, styles, CultureInfo.CurrentCulture, out value)
            && !decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        // No leading sign is allowed above, so this catches nothing a keyboard can produce - it is
        // here for a value arriving from anywhere else, since a negative gram would pass every other
        // check and then be summed into a day's total.
        return value >= 0m;
    }
}

/// <summary>One micronutrient line on a card: "Vitamin C", "12 mg".</summary>
/// <remarks>
/// Formatted here rather than in XAML because the unit belongs to the nutrient rather than to the
/// amount, and a template that formatted its own would need the catalog to reach the view.
/// </remarks>
public sealed record NutrientRow(string DisplayName, string Amount);
