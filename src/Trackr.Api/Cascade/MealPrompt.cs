using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Cascade;

/// <summary>
/// Builds what the model is sent: the schema its answer is constrained to, the instructions, and
/// the decision about which photographs it gets to see.
/// </summary>
/// <remarks>
/// Pure and separate from <see cref="OllamaMealAnalyzer"/>, for the reason
/// <see cref="OpenFoodFactsMapper"/> is separate from its client: the half worth testing needs no
/// network, and a prompt is a piece of program logic rather than a string constant.
/// <para>
/// <strong>Everything here is generated from <see cref="NutrientCatalog"/>.</strong> The nutrient
/// list in the instructions, the units beside it and the <c>enum</c> of permitted keys in the schema
/// all come from the same seed the database is built from, so adding selenium later cannot leave the
/// model being told about a nutrient the server will reject, or being told the wrong unit for one.
/// CLAUDE.md section 7 asks for the set to stay data-driven; this is where that would otherwise
/// quietly stop being true.
/// </para>
/// </remarks>
public static class MealPrompt
{
    /// <summary>What the model says when an item is none of the identified products.</summary>
    /// <remarks>
    /// A sentinel rather than an omitted field, because the property is required and declared first.
    /// Ollama's grammar emits required properties in declaration order, so making this the first
    /// required field forces the model to decide <em>which product it is looking at</em> before it
    /// commits to any numbers. Optional, it could name the product after having already invented
    /// figures for it.
    /// </remarks>
    public const string NoProduct = "none";

    /// <summary>
    /// The most foods one photo and one sentence may plausibly describe.
    /// </summary>
    /// <remarks>
    /// A sanity limit, not a view about meals - it is grammar-enforced, so it also bounds the
    /// output against the repetition failure <see cref="OllamaOptions.MaxOutputTokens"/> describes.
    /// </remarks>
    public const int MaxItems = 12;

    /// <summary>Column limits from <see cref="SaveLogItemRequest"/>, enforced in the grammar.</summary>
    /// <remarks>
    /// Matching them matters for a reason beyond tidiness: milestone 9 hands a confirmed item
    /// straight back to <c>POST /api/log</c>, so a name the model was allowed to make 400 characters
    /// long would fail validation on the request the user had already approved. Constraining the
    /// grammar is also the only thing that stops a model from looping inside a string field, which
    /// no amount of JSON validity checking can catch.
    /// </remarks>
    private const int MaxNameLength = 200;

    private const int MaxBrandLength = 120;

    private const int MaxUnitLength = 32;

    private const int MaxNoteLength = 300;

    /// <summary>A kilogram of butter is about 7 200 kcal, so nothing real reaches this.</summary>
    private const int MaxEnergyKcal = 20_000;

    /// <summary>The handle a product is given in the prompt and quoted back by.</summary>
    public static string Reference(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"p{index + 1}");

    /// <summary>
    /// Which photographs actually go to the model - CLAUDE.md section 5's central optimisation.
    /// </summary>
    /// <remarks>
    /// <strong>A photo whose product was fully identified is withheld.</strong> Open Food Facts has
    /// already read that label properly, so sending the picture would spend the most expensive part
    /// of the request asking a small model to do worse. Partial matches still send theirs, because
    /// the whole point of a partial match is that something is missing from it.
    /// <para>
    /// The decision lives here rather than in the caller so it is visible in the request that goes
    /// out, which is where it can be asserted.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MealPhoto> PhotosToSend(MealAnalysisRequest request)
    {
        var resolved = request.KnownProducts
            .Where(product => product.Complete && product.MealImageId is not null)
            .Select(product => product.MealImageId!.Value)
            .ToHashSet();

        return [.. request.Photos.Where(photo => !resolved.Contains(photo.Id))];
    }

    /// <summary>The non-core nutrients the model may report, in nutrition-label order.</summary>
    public static IReadOnlyList<NutrientDefinition> ReportableNutrients(NutrientCatalog catalog) =>
        [.. catalog.All.Where(nutrient => !nutrient.IsCore).OrderBy(nutrient => nutrient.SortOrder)];

    /// <summary>
    /// The JSON Schema the reply is constrained to.
    /// </summary>
    /// <remarks>
    /// <strong>Every construct here was chosen against what Ollama's schema-to-grammar conversion
    /// actually supports, and the failure mode for getting it wrong is silent.</strong> An
    /// unsupported keyword is skipped rather than rejected, leaving that part of the answer
    /// unconstrained with nothing to indicate it. So there is no <c>$ref</c>, no <c>oneOf</c>, no
    /// <c>additionalProperties</c>, no <c>pattern</c> and no <c>uniqueItems</c> below, and a test
    /// asserts there never is.
    /// <para>
    /// Two consequences shape the fields:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>minimum</c> and <c>maximum</c> are honoured on an integer and ignored on a number, so
    /// energy is an integer and gets range checking free. Labels declare whole kilocalories anyway.
    /// The macros stay numbers, because half a gram of fat is a real quantity.
    /// </description></item>
    /// <item><description>
    /// Required properties are generated before optional ones, in declaration order. That ordering
    /// is the reason <c>productRef</c> comes first - see <see cref="NoProduct"/>.
    /// </description></item>
    /// </list>
    /// <para>
    /// <strong>The nutrient <c>enum</c> does less than it appears to, and this is worth knowing
    /// before trusting it.</strong> It makes an invented key impossible - but a model that was about
    /// to report selenium must now pick one of the 25 permitted keys instead, and it will attach
    /// selenium's number to whichever it picks. A rejected key has become a misattributed amount,
    /// which looks exactly like a measurement. The instructions tell the model to omit anything not
    /// listed, and <see cref="MealAnalysisReader"/>'s cross-checks are what actually catch it.
    /// </para>
    /// </remarks>
    public static JsonObject Schema(NutrientCatalog catalog, MealAnalysisRequest request)
    {
        var references = new JsonArray { NoProduct };

        for (var index = 0; index < request.KnownProducts.Count; index++)
        {
            references.Add(request.KnownProducts[index].Reference);
        }

        var nutrientKeys = new JsonArray();

        foreach (var nutrient in ReportableNutrients(catalog))
        {
            nutrientKeys.Add(nutrient.Key);
        }

        var item = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                // Required, in the order the model must commit to them.
                ["productRef"] = new JsonObject { ["type"] = "string", ["enum"] = references },
                ["name"] = Text(1, MaxNameLength),
                ["quantity"] = new JsonObject { ["type"] = "number" },
                ["energyKcal"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 0,
                    ["maximum"] = MaxEnergyKcal
                },
                ["fatG"] = new JsonObject { ["type"] = "number" },
                ["carbohydrateG"] = new JsonObject { ["type"] = "number" },
                ["proteinG"] = new JsonObject { ["type"] = "number" },

                // Optional from here down. Anything the model cannot determine it leaves out.
                ["brand"] = Text(0, MaxBrandLength),
                ["servingSize"] = new JsonObject { ["type"] = "number" },
                ["servingUnit"] = Text(0, MaxUnitLength),
                ["nutrients"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = nutrientKeys.Count,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["key"] = new JsonObject { ["type"] = "string", ["enum"] = nutrientKeys },
                            ["amount"] = new JsonObject { ["type"] = "number" }
                        },
                        ["required"] = new JsonArray { "key", "amount" }
                    }
                }
            },
            ["required"] = new JsonArray
            {
                "productRef", "name", "quantity", "energyKcal", "fatG", "carbohydrateG", "proteinG"
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 0,
                    ["maxItems"] = MaxItems,
                    ["items"] = item
                },
                ["note"] = Text(0, MaxNoteLength)
            },
            ["required"] = new JsonArray { "items", "note" }
        };

        static JsonObject Text(int minLength, int maxLength)
        {
            var schema = new JsonObject { ["type"] = "string", ["maxLength"] = maxLength };

            if (minLength > 0)
            {
                schema["minLength"] = minLength;
            }

            return schema;
        }
    }

    /// <summary>The instructions, assembled for this particular request.</summary>
    public static string SystemMessage(NutrientCatalog catalog, MealAnalysisRequest request)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine(
            """
            You read nutrition information out of photographs and short descriptions of food. You
            are one stage of an automated pipeline. Your entire reply is a single JSON object
            matching the schema you have been given. No prose, no explanation, no markdown fences.

            WHAT YOU ARE BEING ASKED
            Identify each distinct food, and for each one report the nutrition of ONE SERVING plus
            how many servings were eaten. Do not multiply anything. The server multiplies servings
            by per-serving values itself, and it is better at arithmetic than you are.

              "two eggs"       -> one item, quantity 2, the nutrition of one egg
              "half a can"     -> one item, quantity 0.5, the nutrition of one serving
              "150 g of rice"  -> quantity is 150 divided by the grams in one serving

            SERVINGS
            If a label states a serving size, use it, and report it in servingSize and servingUnit
            ("28", "g"). If nothing states one, choose a serving a person would recognise - one egg,
            one slice, one cup - and say what it is.

            MISSING IS NOT ZERO
            Report only what you can actually determine: from a label, from the data given to you,
            or from confident general knowledge of the food. A nutrient you leave out is recorded as
            "not measured", which is correct and harmless. A nutrient you guess is recorded as a
            measurement, which is a wrong number in somebody's health record. Leave it out.

            Zero is a claim. Use 0 only where the label says zero.

            If a nutrient you can see is not in the list below, leave it out entirely. Do not record
            it under a different nutrient's name.

            NUTRIENTS AND THEIR UNITS
            energyKcal, fatG, carbohydrateG and proteinG are required on every item. Energy is whole
            kilocalories and the three macros are grams.

            Everything else is optional, goes in the "nutrients" array, and must use the key and the
            unit given here. Do not convert: if a label says 480 mg of sodium, write 480 under
            sodium. List each key at most once.
            """);

        prompt.AppendLine();

        foreach (var nutrient in ReportableNutrients(catalog))
        {
            prompt.AppendLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {nutrient.Key,-22}{nutrient.DisplayName,-26}{Symbol(nutrient.Unit)}"));
        }

        prompt.AppendLine();
        AppendKnownProducts(prompt, catalog, request);
        AppendEarlierProblems(prompt, request);

        prompt.Append(
            """
            IF YOU CANNOT TELL
            Return an empty "items" array and put one plain sentence in "note" saying why - "the
            photo is too blurry to read the label". A wrong answer is worse than no answer: a person
            will be shown whatever you say and asked to approve it. Otherwise leave "note" empty.
            """);

        return prompt.ToString();
    }

    /// <summary>The system and user turns, with the surviving photographs attached.</summary>
    /// <remarks>
    /// The user's own text is a separate message rather than being pasted into the instructions.
    /// That costs nothing and keeps a structural boundary between what this server said and what
    /// somebody typed - the realistic case being not an attack but a photograph of a recipe card
    /// whose own words read like instructions.
    /// </remarks>
    /// <param name="images">Base64 photographs, already downscaled, in <see cref="PhotosToSend"/> order.</param>
    public static IReadOnlyList<OllamaMessage> Messages(
        NutrientCatalog catalog,
        MealAnalysisRequest request,
        IReadOnlyList<string> images)
    {
        var text = string.IsNullOrWhiteSpace(request.Text)
            ? "No description was given. Work from the photographs."
            : request.Text.Trim();

        return
        [
            new OllamaMessage { Role = "system", Content = SystemMessage(catalog, request) },
            new OllamaMessage
            {
                Role = "user",
                Content = text,
                Images = images.Count > 0 ? images : null
            }
        ];
    }

    /// <summary>
    /// Describes each identified product, pinning the serving its figures are expressed in.
    /// </summary>
    /// <remarks>
    /// <strong>Pinning the serving is what makes the merge safe.</strong> Open Food Facts may be
    /// reporting per 100 g because no serving size existed anywhere, while the label in the photo
    /// states 28 g. Take one side's protein and the other's fat and every number is defensible while
    /// the total is nonsense - which is the failure docs/decisions/08-barcode-off.md already had to
    /// solve once, on the other side of the same seam.
    /// <para>
    /// So the model is never asked to re-state a known product's serving. It expresses how much was
    /// eaten as a fraction of the serving it is given, which is the unit
    /// <c>FoodItemComponent.Quantity</c> already counts in and the only one every catalog item is
    /// guaranteed to have.
    /// </para>
    /// </remarks>
    private static void AppendKnownProducts(
        StringBuilder prompt,
        NutrientCatalog catalog,
        MealAnalysisRequest request)
    {
        prompt.AppendLine("IDENTIFIED PRODUCTS");

        if (request.KnownProducts.Count == 0)
        {
            prompt.AppendLine(
                """
                None. Nothing in this request was matched against the nutrition database, so set
                "productRef" to "none" on every item.
                """);
            prompt.AppendLine();

            return;
        }

        prompt.AppendLine(
            """
            A barcode in one of the photographs was matched against a nutrition database. These
            figures were read off a real label and are better than anything you can determine from
            a picture.
            """);
        prompt.AppendLine();

        foreach (var product in request.KnownProducts)
        {
            var draft = product.Draft;
            var name = draft.Name ?? "an unnamed product";
            var brand = draft.Brand is null ? "" : $" - {draft.Brand}";

            prompt.AppendLine(
                string.Create(CultureInfo.InvariantCulture, $"{product.Reference}  {name}{brand}"));

            prompt.AppendLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"    One serving is {Number(draft.ServingSize)} {draft.ServingUnit}. Every figure below is per that serving."));

            AppendKnownFigures(prompt, catalog, draft);

            if (product.Complete)
            {
                prompt.AppendLine(
                    "    Its photograph is deliberately not being shown to you, because the figures");
                prompt.AppendLine("    above are already better than a reading of it.");
                prompt.AppendLine(
                    $"    -> If one of the foods is this product, set \"productRef\" to \"{product.Reference}\",");
                prompt.AppendLine(
                    "       copy the figures above into the nutrition fields, and give the quantity");
                prompt.AppendLine("       as a number of those servings. Half a serving is 0.5.");
            }
            else
            {
                prompt.AppendLine(
                    "    Its photograph IS included, because the database was missing some of this.");
                prompt.AppendLine(
                    $"    -> If one of the foods is this product, set \"productRef\" to \"{product.Reference}\",");
                prompt.AppendLine(
                    "       give the quantity as a number of those servings, and read whatever is");
                prompt.AppendLine("       missing above off the label. Do not change the serving size.");
            }

            prompt.AppendLine();
        }

        prompt.AppendLine("""Anything that is not one of these products takes "none".""");
        prompt.AppendLine();
    }

    private static void AppendKnownFigures(
        StringBuilder prompt,
        NutrientCatalog catalog,
        ProductDraft draft)
    {
        var known = new List<string>();

        Add(draft.EnergyKcal, "energy", "kcal");
        Add(draft.FatG, "fat", "g");
        Add(draft.CarbohydrateG, "carbohydrate", "g");
        Add(draft.ProteinG, "protein", "g");

        foreach (var nutrient in ReportableNutrients(catalog))
        {
            if (draft.Nutrients.TryGetValue(nutrient.Key, out var amount))
            {
                Add(amount, nutrient.Key, Symbol(nutrient.Unit));
            }
        }

        prompt.AppendLine(
            known.Count == 0
                ? "    Known: nothing - the database had the product but none of its numbers."
                : $"    Known: {string.Join(", ", known)}.");

        prompt.AppendLine("    Anything not listed there is unknown.");

        void Add(decimal? value, string label, string unit)
        {
            if (value is { } amount)
            {
                known.Add(string.Create(CultureInfo.InvariantCulture, $"{label} {Number(amount)} {unit}"));
            }
        }
    }

    /// <summary>
    /// Relays what went wrong earlier, which CLAUDE.md section 5 asks for by name.
    /// </summary>
    /// <remarks>
    /// So the model can explain itself in the reply - "I couldn't reach the food database, so I
    /// estimated from your photo instead". The user is told separately and unconditionally; this is
    /// the half that makes the explanation read like an answer rather than an error banner.
    /// </remarks>
    private static void AppendEarlierProblems(StringBuilder prompt, MealAnalysisRequest request)
    {
        if (request.EarlierProblems.Count == 0)
        {
            return;
        }

        prompt.AppendLine("WHAT WENT WRONG EARLIER");
        prompt.AppendLine(
            "Mention this in \"note\" if it is relevant to how confident your answer is.");

        foreach (var problem in request.EarlierProblems)
        {
            prompt.AppendLine($"  - {problem}");
        }

        prompt.AppendLine();
    }

    /// <summary>Drops trailing zeroes, so a serving reads "28" rather than "28.000".</summary>
    /// <remarks>
    /// Four decimal places because that is what <see cref="StoredPrecision.Amount"/> keeps; showing
    /// more would offer the model precision the server cannot store.
    /// </remarks>
    private static string Number(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// The unit as the API spells it, read from the enum rather than written out again.
    /// </summary>
    /// <remarks>
    /// One source for "µg". A second copy here would eventually disagree with the wire contract and
    /// with wiki/Nutrient-Reference.md, and the model would be told to use a unit no client renders.
    /// </remarks>
    private static string Symbol(NutrientUnit unit) => Symbols[unit];

    private static readonly IReadOnlyDictionary<NutrientUnit, string> Symbols =
        Enum.GetValues<NutrientUnit>().ToDictionary(
            unit => unit,
            unit => typeof(NutrientUnit).GetField(unit.ToString())
                ?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()
                ?.Name
                ?? unit.ToString());
}
