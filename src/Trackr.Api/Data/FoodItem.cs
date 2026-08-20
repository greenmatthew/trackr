using Trackr.Api.Identity;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Data;

/// <summary>
/// One thing that can be eaten, as the catalog knows it: a product, an ingredient, or a dish
/// somebody typed in.
/// </summary>
/// <remarks>
/// <strong>Every nutrient value on this entity is per one serving.</strong> Open Food Facts
/// returns both per-100g and per-serving figures, so milestone 7's mapper is where the conversion
/// happens; nothing downstream should ever have to ask which it is holding.
/// <para>
/// The catalog is not built up front (CLAUDE.md section 10). It accumulates as meals are logged,
/// from barcode lookups, from the model's reads of photos, and from things typed by hand.
/// </para>
/// <para>
/// <strong><see cref="UserId"/> is nullable, and null means global.</strong> This amends CLAUDE.md
/// section 7, which describes an owning user: a household sharing one server should scan a can of
/// beans once between them, not once each. Visibility is chosen when the item is created rather
/// than derived from <see cref="Source"/> - deriving it would forbid sharing a hand-typed staple
/// and force sharing a barcode scan of something private. Promotion to global is one-way, because
/// another account may already be logging the item by the time anyone regrets it.
/// </para>
/// <para>
/// Global items are editable by anyone, wiki-style, which is why <see cref="UpdatedByUserId"/>
/// exists. Two things bound the damage from a bad edit: it is attributable, and the snapshot rule
/// on <see cref="LogItem"/> means no already-logged number ever changes.
/// </para>
/// </remarks>
public class FoodItem
{
    /// <summary>
    /// Version 7 so the primary key index keeps appending rather than fragmenting on random
    /// inserts - the same reasoning as <see cref="TrackrUser"/>, which is also why the model marks
    /// this <c>ValueGeneratedNever</c>.
    /// </summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// The owning account, or null for an item shared with everyone on this server.
    /// </summary>
    /// <remarks>
    /// Cascade on delete, so removing an account takes its personal items with it - they have no
    /// cross-account record-keeping value, the same reasoning as <see cref="UserAvatar"/>. A
    /// global item has no owner to cascade from, so it survives, which is the entire point of
    /// having shared it.
    /// </remarks>
    public Guid? UserId { get; set; }

    public TrackrUser? User { get; set; }

    public string Name { get; set; } = "";

    public string? Brand { get; set; }

    /// <summary>
    /// The product's barcode, when it came off a package. Never shown to or typed by the user -
    /// CLAUDE.md section 1 keeps barcodes invisible plumbing.
    /// </summary>
    /// <remarks>
    /// Two partial unique indexes cover this, not one: unique among global items, and unique per
    /// account among personal ones. That is what lets the household share one scan of a product
    /// while still letting an individual keep a private override of it.
    /// </remarks>
    public string? Barcode { get; set; }

    /// <summary>How much one serving is, in <see cref="ServingUnit"/>.</summary>
    public decimal ServingSize { get; set; } = 1m;

    /// <summary>Free text on purpose: "g", "ml", "slice", "egg".</summary>
    public string ServingUnit { get; set; } = "";

    public FoodSource Source { get; set; } = FoodSource.Manual;

    /// <summary>
    /// What the product is made of, as printed on the package.
    /// </summary>
    /// <remarks>
    /// <strong>Raw text, deliberately, and not the nutrient machinery.</strong> Nutrients are a
    /// fixed server-owned vocabulary with units and a sort order; an ingredient list is open-ended
    /// prose belonging to one brand's formulation, and modelling it as rows would be solving a
    /// harder problem than anybody has asked for - CLAUDE.md section 7 says so directly.
    /// <para>
    /// <strong>Only true of a specific product.</strong> It is accepted on an item carrying a brand
    /// or a barcode and refused on a generic one, because "chicken breast" has no formulation to
    /// describe. A composite derives its own from its components rather than storing one.
    /// </para>
    /// </remarks>
    public string? IngredientsText { get; set; }

    /// <summary>
    /// Allergens the product declares, as Open Food Facts tags - <c>en:milk</c>, <c>en:nuts</c>.
    /// </summary>
    /// <remarks>
    /// A short closed vocabulary, which is what makes it worth storing structured when the
    /// ingredient list next to it is not. "Does this contain nuts" is a question with consequences,
    /// and answering it from prose is not something to attempt.
    /// <para>
    /// Stored as a Postgres <c>text[]</c> rather than a join table. The relational nutrient store
    /// exists because a nutrient carries a unit, a display name and an order and the set grows; an
    /// allergen tag carries none of that and the list is five entries long. A join table here would
    /// be machinery with nothing to hold. Array containment is what queries it.
    /// </para>
    /// <para>
    /// Open Food Facts' tags rather than a vocabulary of Trackr's own, because the taxonomy is
    /// already maintained by somebody else and translating into a local set would be a mapping to
    /// keep correct forever. Empty means "the source said nothing", never "contains no allergens" -
    /// the same distinction the nutrient store draws.
    /// </para>
    /// </remarks>
    public List<string> Allergens { get; set; } = [];

    /// <summary>
    /// What Open Food Facts' ingredient analysis concluded: <c>en:palm-oil</c>, <c>en:non-vegan</c>.
    /// </summary>
    /// <remarks>
    /// Same storage and the same caveat as <see cref="Allergens"/>. These are inferences OFF drew
    /// from the ingredient list rather than claims a label made, and OFF says so itself with tags
    /// like <c>en:vegan-status-unknown</c>; they are carried as reported and not tidied up.
    /// </remarks>
    public List<string> DietFlags { get; set; } = [];

    /// <summary>
    /// The four always-present nutrients, as columns.
    /// </summary>
    /// <remarks>
    /// CLAUDE.md section 7 calls for these to be first-class so the dashboard and the goal maths
    /// reach them without a join or a parse. They are non-nullable, which is the one deliberate
    /// exception to "missing is not zero": section 5's validator refuses to save without them, so
    /// a source that could not determine the protein is a cascade problem to raise with the user,
    /// not a hole in the schema.
    /// </remarks>
    public decimal EnergyKcal { get; set; }

    /// <inheritdoc cref="EnergyKcal"/>
    public decimal FatG { get; set; }

    /// <inheritdoc cref="EnergyKcal"/>
    public decimal CarbohydrateG { get; set; }

    /// <inheritdoc cref="EnergyKcal"/>
    public decimal ProteinG { get; set; }

    /// <summary>Everything else, one row per measured nutrient. Never the core four.</summary>
    public List<FoodItemNutrient> Nutrients { get; set; } = [];

    /// <summary>
    /// How many servings one batch of a recipe makes. Null for everything that is not a recipe.
    /// </summary>
    /// <remarks>
    /// <strong>Non-null is what makes an item a composite</strong>, and it comes with
    /// <see cref="Components"/>: the API refuses a yield without components and components without a
    /// yield, so the two are never out of step.
    /// <para>
    /// It exists so a recipe's per-serving values stay comparable with every other item's. The
    /// components add up to a whole batch; dividing by the yield puts the result back on the same
    /// footing as a scanned package, which is what lets the log, the stats views and the cascade
    /// stay ignorant that composites exist at all.
    /// </para>
    /// </remarks>
    public decimal? Yield { get; set; }

    /// <summary>The ingredients, for a composite. Empty for everything else.</summary>
    /// <remarks>
    /// The nutrient columns and <see cref="Nutrients"/> above are <em>derived</em> from these when
    /// the item is a composite - materialised on write by <see cref="CompositeNutrition"/> rather
    /// than summed at read time, so no catalog list or dashboard has a tree walk in front of it.
    /// </remarks>
    public List<FoodItemComponent> Components { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>
    /// Who last changed it, so a correction to a shared item is attributable.
    /// </summary>
    /// <remarks>
    /// SetNull rather than cascade or restrict: attribution is a nice-to-have and must never block
    /// an account deletion, nor take a shared item down with the account that last touched it.
    /// </remarks>
    public Guid? UpdatedByUserId { get; set; }

    public TrackrUser? UpdatedBy { get; set; }
}
