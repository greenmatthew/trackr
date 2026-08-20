using Trackr.Api.Cascade;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The validator CLAUDE.md section 5 marks REQUIRED, which is the main thing standing between the
/// model and a wrong number in somebody's health record.
/// </summary>
/// <remarks>
/// Pure: no container, no model, no network. Every reply here is written by hand rather than
/// captured, because the interesting ones are the replies a working model does not produce - and
/// the whole point of the class is what happens when the model is not working.
/// <para>
/// The distinction these tests exist to protect is the one running through the whole project: a
/// dropped value is <em>not measured</em> and a zero is a claim somebody made. Several tests below
/// look like they are checking a number and are really checking which of those two happened.
/// </para>
/// </remarks>
public sealed class MealAnalysisReaderTests
{
    private readonly NutrientCatalog _catalog = new();

    private static readonly HashSet<string> Products = new(["p1"], StringComparer.Ordinal);

    // A plain, internally consistent item: 15 g carbohydrate and 3 g protein is 72 kcal, plus 1 g of
    // fat is 81, which is inside the tolerance of the 80 declared.
    private const string Toast =
        """{"productRef":"none","name":"Toast","quantity":1,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3}""";

    [Fact]
    public void A_reply_that_is_not_json_fails()
    {
        var reading = Read("I think that's about 300 calories?");

        Assert.False(reading.Succeeded);
        Assert.Contains("not valid JSON", reading.Failure, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Deliberately strict. Finding the first <c>{</c> in a reply wrapped in prose is the obvious
    /// robustness improvement, and it is the same class of change that manufactured checksum-valid
    /// barcodes out of ingredient paragraphs in milestone 7: it turns a visible failure into an
    /// invisible one. If the grammar is working this never happens; if it is not, that is worth
    /// finding out.
    /// </remarks>
    [Fact]
    public void Prose_around_the_json_fails_rather_than_being_salvaged()
    {
        var reading = Read($"Here is the JSON you asked for:\n{Reply(Toast)}");

        Assert.False(reading.Succeeded);
    }

    /// <remarks>
    /// A distinct message from "not valid JSON", because the fix is different: this one is a setting
    /// on this server, not a model that cannot do the job.
    /// </remarks>
    [Fact]
    public void A_truncated_reply_says_the_model_ran_out_of_room()
    {
        var reading = Read(Reply(Toast), doneReason: "length");

        Assert.False(reading.Succeeded);
        Assert.Contains("ran out of room", reading.Failure, StringComparison.Ordinal);
    }

    /// <remarks>
    /// The honest path, and the reason the schema makes <c>note</c> required. The model's own
    /// sentence is far more use to the person reading it than this server reporting that it got
    /// nothing.
    /// </remarks>
    [Fact]
    public void A_reply_with_no_items_fails_carrying_the_models_own_note()
    {
        var reading = Read("""{"note":"The photo is too blurry to read the label.","items":[]}""");

        Assert.False(reading.Succeeded);
        Assert.Equal("The photo is too blurry to read the label.", reading.Note);
    }

    /// <remarks>
    /// A required field being absent means the grammar was not applied at all, so the rest of the
    /// reply is unconstrained too - including the parts that happen to look right. That is a fault
    /// in this server rather than a bad reading, so nothing is salvaged from it.
    /// </remarks>
    [Fact]
    public void A_missing_core_value_fails_the_whole_analysis()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Toast","quantity":1,"fatG":1,"carbohydrateG":15,"proteinG":3}"""));

        Assert.False(reading.Succeeded);
        Assert.Contains("required to provide", reading.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_with_no_name_is_dropped()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"","quantity":1,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3}"""));

        Assert.False(reading.Succeeded);
    }

    /// <remarks>
    /// Dropped rather than clamped. The core four are non-nullable columns, so a negative one cannot
    /// be stored - and zeroing it would turn "the model produced nonsense" into "this contains no
    /// protein", which is a claim, and a plausible-looking one.
    /// </remarks>
    [Fact]
    public void A_negative_core_value_drops_the_item()
    {
        var reading = Read(Reply(
            Toast,
            """{"productRef":"none","name":"Butter","quantity":1,"energyKcal":100,"fatG":-5,"carbohydrateG":0,"proteinG":0}"""));

        Assert.True(reading.Succeeded);
        Assert.Equal(["Toast"], reading.Items.Select(item => item.Name));
        Assert.Contains(reading.Warnings, warning => warning.Contains("Butter", StringComparison.Ordinal));
    }

    /// <remarks>
    /// The one deliberate substitution in the reader. They ate something, the nutrition may be
    /// perfectly good, and the quantity is the easiest field on the card to correct - so "1, please
    /// check" beats discarding the item.
    /// </remarks>
    [Fact]
    public void A_quantity_of_zero_becomes_one_with_a_warning()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Toast","quantity":0,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3}"""));

        var item = Assert.Single(reading.Items);

        Assert.Equal(1m, item.Quantity);
        Assert.Contains(item.Warnings, warning => warning.Contains("one serving was assumed", StringComparison.Ordinal));
    }

    [Fact]
    public void An_absurd_quantity_is_reduced_and_flagged()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Toast","quantity":90000,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3}"""));

        var item = Assert.Single(reading.Items);

        Assert.Equal(1000m, item.Quantity);
        Assert.Equal(AnalysisConfidence.Low, item.Confidence);
    }

    /// <summary>
    /// The overflow that would otherwise surface in milestone 9 as a 500 from Postgres.
    /// </summary>
    /// <remarks>
    /// A log item's amounts are <c>numeric(12,4)</c> and the quantity is multiplied in before
    /// storing, so a large enough pair does not fit. The grammar cannot prevent it - an
    /// unconstrained JSON number has sixteen digits available either side of the point - and the
    /// user would have already tapped confirm by the time it failed.
    /// </remarks>
    [Fact]
    public void An_absurd_quantity_cannot_overflow_the_stored_total()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Sugar","quantity":900,"energyKcal":900000,"fatG":0,"carbohydrateG":0,"proteinG":0}"""));

        var item = Assert.Single(reading.Items);

        Assert.True(
            item.EnergyKcal * item.Quantity <= 99_999_999.9999m,
            $"{item.EnergyKcal} x {item.Quantity} would not fit a numeric(12,4) column.");
    }

    [Fact]
    public void An_impossible_serving_size_is_dropped()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Toast","quantity":1,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3,"servingSize":0,"servingUnit":"g"}"""));

        var item = Assert.Single(reading.Items);

        Assert.Null(item.ServingSize);
        Assert.Null(item.ServingUnit);
    }

    /// <remarks>
    /// Impossible while the schema's enum is in force, which is exactly why it is tested: reaching
    /// it means constrained decoding is not working, and the reply cannot be trusted anywhere.
    /// </remarks>
    [Fact]
    public void An_unknown_nutrient_key_is_dropped_with_a_warning()
    {
        var reading = Read(Reply(WithNutrients("""{"key":"selenium","amount":55}""")));

        var item = Assert.Single(reading.Items);

        Assert.Empty(item.Nutrients);
        Assert.Contains(item.Warnings, warning => warning.Contains("does not track", StringComparison.Ordinal));
    }

    /// <remarks>
    /// A core nutrient in the map would be counted twice - once as a column and once as a row - and
    /// a database CHECK constraint refuses it outright.
    /// </remarks>
    [Fact]
    public void A_core_nutrient_in_the_map_is_dropped()
    {
        var reading = Read(Reply(WithNutrients("""{"key":"fat","amount":9}""")));

        Assert.Empty(Assert.Single(reading.Items).Nutrients);
    }

    /// <remarks>
    /// The rule wiki/Nutrient-Reference.md states and this is the easiest place to lose: a value the
    /// server could not use is absent, never zero. Zero would say the label declared none.
    /// </remarks>
    [Fact]
    public void A_negative_nutrient_amount_is_not_measured_rather_than_zero()
    {
        var reading = Read(Reply(WithNutrients("""{"key":"sodium","amount":-40}""")));

        Assert.DoesNotContain("sodium", Assert.Single(reading.Items).Nutrients.Keys);
    }

    [Theory]
    [InlineData("1e40")]
    [InlineData("1e999999")]
    public void An_amount_too_large_for_the_column_is_not_measured(string amount)
    {
        var reading = Read(Reply(WithNutrients($$"""{"key":"sodium","amount":{{amount}}}""")));

        Assert.DoesNotContain("sodium", Assert.Single(reading.Items).Nutrients.Keys);
    }

    [Fact]
    public void Duplicate_keys_that_agree_are_deduplicated()
    {
        var reading = Read(Reply(WithNutrients(
            """{"key":"sodium","amount":40}""",
            """{"key":"sodium","amount":40}""")));

        Assert.Equal(40m, Assert.Single(reading.Items).Nutrients["sodium"]);
    }

    /// <remarks>
    /// Two different answers about the same nutrient is the model saying it does not know, and
    /// picking one would be picking at random. The grammar cannot prevent this - <c>uniqueItems</c>
    /// is not a construct it supports.
    /// </remarks>
    [Fact]
    public void Duplicate_keys_that_disagree_drop_the_nutrient()
    {
        var reading = Read(Reply(WithNutrients(
            """{"key":"sodium","amount":40}""",
            """{"key":"sodium","amount":400}""")));

        Assert.DoesNotContain("sodium", Assert.Single(reading.Items).Nutrients.Keys);
    }

    /// <remarks>
    /// Section 5's reconciliation, at roughly 4 kcal per gram of protein and carbohydrate and 9 per
    /// gram of fat.
    /// </remarks>
    [Fact]
    public void Calories_that_do_not_match_the_macros_are_low_confidence()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Soup","quantity":1,"energyKcal":500,"fatG":1,"carbohydrateG":1,"proteinG":1}"""));

        var item = Assert.Single(reading.Items);

        Assert.Equal(AnalysisConfidence.Low, item.Confidence);
        Assert.Contains(item.Warnings, warning => warning.Contains("do not match", StringComparison.Ordinal));
    }

    /// <remarks>
    /// Flagged, not dropped and not failed. Section 5 asks for it to be shown rather than presented
    /// as fact, and the person looking at the card is better placed than this code to say which of
    /// the five numbers is the wrong one.
    /// </remarks>
    [Fact]
    public void A_flagged_item_is_still_returned_with_its_numbers()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Soup","quantity":1,"energyKcal":500,"fatG":1,"carbohydrateG":1,"proteinG":1}"""));

        Assert.Equal(500m, Assert.Single(reading.Items).EnergyKcal);
    }

    /// <summary>
    /// The reason the tolerance is a quarter rather than a tenth.
    /// </summary>
    /// <remarks>
    /// Atwater ignores fibre, which carries roughly 2 kcal per gram rather than 4. A high-fibre
    /// cereal is correctly transcribed and still misses the prediction by tens of kilocalories, and
    /// a check that fires on those is one people learn to ignore.
    /// </remarks>
    [Fact]
    public void A_high_fibre_food_is_not_flagged()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Bran","quantity":1,"energyKcal":400,"fatG":2,"carbohydrateG":70,"proteinG":12,"nutrients":[{"key":"fibre","amount":25}]}"""));

        Assert.Equal(AnalysisConfidence.Normal, Assert.Single(reading.Items).Confidence);
    }

    /// <summary>
    /// The most damaging misreading there is, and the only check that catches it.
    /// </summary>
    /// <remarks>
    /// A dual-column label offers per 100 g and per serving. A model that reads the wrong column
    /// while reporting the right serving produces figures that are individually correct, reconcile
    /// against each other perfectly, and are wrong by threefold. The only thing that gives it away
    /// is that the food would have to weigh more than its own serving.
    /// </remarks>
    [Fact]
    public void Per_serving_masses_that_exceed_the_serving_are_low_confidence()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Crisps","quantity":1,"energyKcal":536,"fatG":34,"carbohydrateG":50,"proteinG":6,"servingSize":28,"servingUnit":"g"}"""));

        var item = Assert.Single(reading.Items);

        Assert.Equal(AnalysisConfidence.Low, item.Confidence);
        Assert.Contains(item.Warnings, warning => warning.Contains("per 100", StringComparison.Ordinal));
    }

    /// <remarks>
    /// The total wins because it is required, core, and has already been reconciled against the
    /// energy. The five per cent plus half a gram of slack is not padding: US labels round fat lines
    /// to the nearest 0.5 g below 5 g, so a correct transcription can genuinely overshoot.
    /// </remarks>
    [Fact]
    public void Saturated_fat_above_total_fat_is_dropped()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Spread","quantity":1,"energyKcal":87,"fatG":3,"carbohydrateG":10,"proteinG":5,"nutrients":[{"key":"saturated_fat","amount":10}]}"""));

        var item = Assert.Single(reading.Items);

        Assert.DoesNotContain("saturated_fat", item.Nutrients.Keys);
        Assert.Contains(item.Warnings, warning => warning.Contains("total fat", StringComparison.Ordinal));
    }

    [Fact]
    public void Sugars_above_carbohydrate_are_low_confidence()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Drink","quantity":1,"energyKcal":40,"fatG":0,"carbohydrateG":10,"proteinG":0,"nutrients":[{"key":"sugars","amount":30}]}"""));

        Assert.Equal(AnalysisConfidence.Low, Assert.Single(reading.Items).Confidence);
    }

    /// <summary>
    /// A negative test whose only job is to stop somebody adding the obvious rule.
    /// </summary>
    /// <remarks>
    /// European labels exclude fibre from the carbohydrate figure and US labels include it, so
    /// "fibre must not exceed carbohydrate" fires on nearly every European product.
    /// docs/decisions/08-barcode-off.md hit the same divergence from the other direction, with
    /// <c>carbohydrates-total</c>.
    /// </remarks>
    [Fact]
    public void Fibre_above_carbohydrate_is_not_flagged()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Psyllium","quantity":1,"energyKcal":20,"fatG":0,"carbohydrateG":5,"proteinG":0,"nutrients":[{"key":"fibre","amount":20}]}"""));

        Assert.Equal(AnalysisConfidence.Normal, Assert.Single(reading.Items).Confidence);
    }

    /// <remarks>
    /// A thousandfold unit slip - milligrams written where grams were asked for - leaves every other
    /// figure on the item internally consistent, so this is the only place it shows up.
    /// </remarks>
    [Fact]
    public void A_nutrient_heavier_than_the_food_is_dropped()
    {
        var reading = Read(Reply(
            """{"productRef":"none","name":"Crisps","quantity":1,"energyKcal":150,"fatG":10,"carbohydrateG":15,"proteinG":2,"servingSize":28,"servingUnit":"g","nutrients":[{"key":"sodium","amount":170000}]}"""));

        Assert.DoesNotContain("sodium", Assert.Single(reading.Items).Nutrients.Keys);
    }

    [Fact]
    public void Amounts_are_rounded_to_what_the_column_keeps()
    {
        var reading = Read(Reply(WithNutrients("""{"key":"sodium","amount":1.234567}""")));

        Assert.Equal(1.2346m, Assert.Single(reading.Items).Nutrients["sodium"]);
    }

    [Fact]
    public void A_product_reference_the_server_never_sent_is_ignored()
    {
        var reading = Read(Reply(
            """{"productRef":"p9","name":"Toast","quantity":1,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3}"""));

        var item = Assert.Single(reading.Items);

        Assert.Null(item.ProductReference);
        Assert.Contains(item.Warnings, warning => warning.Contains("never mentioned", StringComparison.Ordinal));
    }

    [Fact]
    public void A_reference_the_server_did_send_is_kept()
    {
        var reading = Read(Reply(
            """{"productRef":"p1","name":"Spread","quantity":2,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3}"""));

        Assert.Equal("p1", Assert.Single(reading.Items).ProductReference);
    }

    /// <remarks>
    /// Two helpings of the same scanned product is an ordinary thing to eat, so neither is dropped
    /// as a duplicate.
    /// </remarks>
    [Fact]
    public void Two_items_may_reference_the_same_product()
    {
        var reading = Read(Reply(
            """{"productRef":"p1","name":"Spread","quantity":1,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3}""",
            """{"productRef":"p1","name":"Spread","quantity":2,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3}"""));

        Assert.Equal(2, reading.Items.Count);
        Assert.All(reading.Items, item => Assert.Equal("p1", item.ProductReference));
    }

    /// <summary>
    /// The gap milestone 9 shipped with, from the run recorded in docs/decisions/11-chat.md.
    /// </summary>
    /// <remarks>
    /// The model echoed the serving's gram weight back as a count: 131 servings of a 330 kcal tub of
    /// ice cream, for a 43 230 kcal meal that nothing flagged. Every check in force at the time
    /// passed, and correctly - the serving is believable, the macros reconcile to the calorie, the
    /// count is under the thousand-serving ceiling. Only the product was absurd.
    /// <remarks>
    /// The half the energy ceiling cannot see. A hundred servings of lettuce is a trivial number of
    /// kilocalories and thirteen kilograms of lettuce, so weighing the portion catches the misread
    /// that counting its calories misses.
    /// <remarks>
    /// The ceiling is on a misread, not on a big appetite, so a portion somebody could plausibly
    /// have eaten has to pass. A check that fires on those is one people learn to ignore.
    /// <remarks>
    /// No line here is absurd on its own - each is under the per-item ceiling and each reconciles -
    /// but nobody ate them all in one sitting. Every item is flagged, because the sum is what is
    /// wrong and the reader cannot tell which line spoiled it.
    /// <remarks>
    /// An ordinary meal of several courses must not trip the meal ceiling, or the flag stops
    /// meaning anything.
    private ModelReading Read(string content, string? doneReason = "stop") =>
        MealAnalysisReader.Read(content, doneReason, _catalog, Products);

    private static string Reply(params string[] items) =>
        $$"""{"note":"","items":[{{string.Join(",", items)}}]}""";

    /// <summary>The plain item, with a nutrient array bolted on.</summary>
    private static string WithNutrients(params string[] entries) =>
        $$"""{"productRef":"none","name":"Toast","quantity":1,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3,"nutrients":[{{string.Join(",", entries)}}]}""";
}
