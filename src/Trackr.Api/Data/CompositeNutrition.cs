using Microsoft.EntityFrameworkCore;

namespace Trackr.Api.Data;

/// <summary>
/// Everything a recipe needs that a scanned package does not: keeping the graph acyclic, turning
/// ingredients into per-serving nutrition, and pushing a corrected ingredient back up through every
/// recipe that uses it.
/// </summary>
/// <remarks>
/// <strong>Nutrition is materialised on write.</strong> A composite's own columns and nutrient rows
/// always hold the answer, so the log, the aggregates, the stats views and the whole cascade never
/// learn that composites exist. Aggregating recursively at read time was the alternative and would
/// have put a tree walk in front of every catalog list and every dashboard.
/// <para>
/// The price of that is this class: the numbers are a cache, and a cache has to be invalidated. When
/// an ingredient changes, every recipe above it - transitively, and across accounts, since a global
/// item may be an ingredient in several households members' recipes - is recomputed in the same
/// transaction as the edit that caused it.
/// </para>
/// </remarks>
public sealed class CompositeNutrition(TrackrDbContext db)
{
    /// <summary>
    /// The advisory lock every catalog write that can touch the recipe graph takes first.
    /// </summary>
    /// <remarks>
    /// Without it the cycle check below is advice rather than a guarantee: two requests could each
    /// add one edge of a two-edge loop, both having looked at a graph in which neither edge existed
    /// yet. The result would be a recipe whose numbers can never be recomputed correctly - a silent
    /// wrong number, which CLAUDE.md section 2 names as the failure mode to design against.
    /// <para>
    /// A transaction-scoped advisory lock rather than <c>SERIALIZABLE</c> because it needs no retry
    /// loop, and one global key rather than a per-item one because a cycle is a property of the
    /// whole graph and cannot be locked piecewise. The cost is that catalog writes serialise; on a
    /// household server they are human-paced, and reads are not affected at all.
    /// </para>
    /// </remarks>
    private const long CatalogWriteLock = 707_1971;

    /// <summary>
    /// Serialises catalog writes for the rest of the current transaction. Requires one.
    /// </summary>
    public Task TakeWriteLockAsync(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({CatalogWriteLock})",
            cancellationToken);

    /// <summary>
    /// Whether making <paramref name="childIds"/> the ingredients of <paramref name="parentId"/>
    /// would let a recipe contain itself, at any depth.
    /// </summary>
    /// <remarks>
    /// Walks down from the proposed ingredients looking for the recipe. The recipe's own existing
    /// edges cannot produce a false positive: they lead away from it, so the walk only meets them
    /// after having already arrived - and arriving is the answer.
    /// <para>
    /// Call it under <see cref="TakeWriteLockAsync"/>, or it is only true of a moment that has
    /// passed.
    /// </para>
    /// </remarks>
    public async Task<bool> WouldFormACycleAsync(
        Guid parentId,
        IReadOnlyCollection<Guid> childIds,
        CancellationToken cancellationToken)
    {
        if (childIds.Contains(parentId))
        {
            return true;
        }

        var seen = new HashSet<Guid>(childIds);
        var frontier = childIds.ToList();

        while (frontier.Count > 0)
        {
            var next = await db.FoodItemComponents
                .Where(component => frontier.Contains(component.ParentFoodItemId))
                .Select(component => component.ChildFoodItemId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (next.Contains(parentId))
            {
                return true;
            }

            // The visited set is what bounds this walk. It would terminate on an already-cyclic
            // graph too, which is the point of writing it this way rather than recursively.
            frontier = [.. next.Where(seen.Add)];
        }

        return false;
    }

    /// <summary>The names of recipes using an item, for a refusal a person can act on.</summary>
    /// <remarks>
    /// Capped, because the message names them and a list of forty is not a message. Only personal
    /// items reach this - a global item cannot be deleted at all - and an account's personal item
    /// is invisible to everyone else, so no name here can belong to somebody else's recipe.
    /// </remarks>
    public async Task<IReadOnlyList<string>> RecipesUsingAsync(
        Guid childId,
        CancellationToken cancellationToken) =>
        await db.FoodItemComponents
            .Where(component => component.ChildFoodItemId == childId)
            .Select(component => component.Parent!.Name)
            .Distinct()
            .OrderBy(name => name)
            .Take(5)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Recomputes every recipe that contains <paramref name="changedId"/>, at any depth.
    /// </summary>
    /// <remarks>
    /// Call it after the edit itself has been saved and inside the same transaction, so the reads
    /// below see the new state and the recompute cannot be committed without it. The changed item is
    /// not recomputed here - the handler has just written it, and doing it twice would only rewrite
    /// the same rows.
    /// <para>
    /// Deliberately not scoped to what the editing account can see. A household member correcting a
    /// shared ingredient has to fix everybody's recipes or none of them, and "none of them" means
    /// somebody else's dinner quietly reports the old numbers.
    /// </para>
    /// </remarks>
    /// <param name="now">
    /// Stamped on every recipe this touches so a client knows to refetch. Attribution is left alone
    /// on purpose: the person edited the ingredient, not the recipe, and that is where
    /// <c>UpdatedByUserId</c> records them.
    /// </param>
    public async Task RecomputeAncestorsAsync(
        Guid changedId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var affected = await AncestorsOfAsync(changedId, cancellationToken);

        if (affected.Count == 0)
        {
            // Nothing is made of it, which is the ordinary case for most of the catalog.
            return;
        }

        var edges = await db.FoodItemComponents
            .Where(component => affected.Contains(component.ParentFoodItemId))
            .Select(component => new Edge(
                component.ParentFoodItemId,
                component.ChildFoodItemId,
                component.Quantity))
            .ToListAsync(cancellationToken);

        // The recipes to recompute, plus the ingredients they are computed from - which may include
        // items that are not being recomputed themselves.
        var recipes = edges.Select(edge => edge.ParentId).ToHashSet();
        var involved = new HashSet<Guid>(recipes);
        involved.UnionWith(edges.Select(edge => edge.ChildId));

        var items = await db.FoodItems
            .Include(item => item.Nutrients)
            .Where(item => involved.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var byRecipe = edges.ToLookup(edge => edge.ParentId);

        foreach (var id in InDependencyOrder(recipes, edges))
        {
            var recipe = items[id];

            Materialise(
                recipe,
                [.. byRecipe[id].Select(edge => (items[edge.ChildId], edge.Quantity))],
                db);

            recipe.UpdatedUtc = now;
        }
    }

    /// <summary>
    /// Writes a recipe's per-serving nutrition from its ingredients: the whole batch, divided by the
    /// yield.
    /// </summary>
    /// <remarks>
    /// Replaces the nutrient map wholesale rather than merging, for the same reason
    /// <c>PUT /api/foods</c> does: an ingredient that stopped reporting iron has to be able to make
    /// the recipe stop reporting iron.
    /// </remarks>
    internal static void Materialise(
        FoodItem recipe,
        IReadOnlyCollection<(FoodItem Child, decimal Quantity)> parts,
        TrackrDbContext db)
    {
        var composed = Compose(parts, recipe.Yield ?? 1m);

        recipe.EnergyKcal = composed.EnergyKcal;
        recipe.FatG = composed.FatG;
        recipe.CarbohydrateG = composed.CarbohydrateG;
        recipe.ProteinG = composed.ProteinG;

        db.FoodItemNutrients.RemoveRange(recipe.Nutrients);
        recipe.Nutrients.Clear();

        foreach (var (key, amount) in composed.Nutrients)
        {
            recipe.Nutrients.Add(new FoodItemNutrient
            {
                FoodItemId = recipe.Id,
                NutrientKey = key,
                Amount = amount
            });
        }
    }

    /// <summary>
    /// Sums the ingredients and divides by the yield, giving one serving of the recipe.
    /// </summary>
    /// <remarks>
    /// <strong>A nutrient survives only if every ingredient reports it.</strong> Treating a missing
    /// value as zero is what every other recipe tracker does and it is wrong here: the flour's
    /// silence about iron is "not measured", not "no iron", and summing it as zero would put a
    /// confident understated number on the card. The wiki's rule - distinguish "known to be zero"
    /// from "not measured" - only means something if the arithmetic respects it.
    /// <para>
    /// The visible cost is that a recipe reports few micronutrients until every ingredient is well
    /// described. That is the honest answer to a question nobody has the data for, and it improves
    /// on its own as the catalog fills. The core four are exempt because they are non-nullable
    /// columns on every item, so a recipe always has calories and macros.
    /// </para>
    /// <para>
    /// Rounded once, at the end. Rounding each ingredient's contribution first would drift by up to
    /// half a unit in the last place per ingredient, which over a twelve-ingredient recipe is a
    /// visible number rather than a rounding artefact.
    /// </para>
    /// </remarks>
    internal static ComposedNutrition Compose(
        IReadOnlyCollection<(FoodItem Child, decimal Quantity)> parts,
        decimal yield)
    {
        decimal energy = 0, fat = 0, carbohydrate = 0, protein = 0;

        foreach (var (child, quantity) in parts)
        {
            energy += child.EnergyKcal * quantity;
            fat += child.FatG * quantity;
            carbohydrate += child.CarbohydrateG * quantity;
            protein += child.ProteinG * quantity;
        }

        var maps = parts
            .Select(part => (
                Amounts: part.Child.Nutrients.ToDictionary(
                    nutrient => nutrient.NutrientKey,
                    nutrient => nutrient.Amount,
                    StringComparer.Ordinal),
                part.Quantity))
            .ToList();

        HashSet<string> shared = maps.Count == 0
            ? []
            : new HashSet<string>(maps[0].Amounts.Keys, StringComparer.Ordinal);

        foreach (var (amounts, _) in maps.Skip(1))
        {
            shared.IntersectWith(amounts.Keys);
        }

        var nutrients = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var key in shared)
        {
            var total = maps.Sum(part => part.Amounts[key] * part.Quantity);

            nutrients[key] = StoredPrecision.Amount(total / yield);
        }

        return new ComposedNutrition(
            StoredPrecision.Amount(energy / yield),
            StoredPrecision.Amount(fat / yield),
            StoredPrecision.Amount(carbohydrate / yield),
            StoredPrecision.Amount(protein / yield),
            nutrients);
    }

    /// <summary>Every recipe that contains an item, at any depth.</summary>
    private async Task<HashSet<Guid>> AncestorsOfAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = new HashSet<Guid>();
        var frontier = new List<Guid> { id };

        while (frontier.Count > 0)
        {
            var parents = await db.FoodItemComponents
                .Where(component => frontier.Contains(component.ChildFoodItemId))
                .Select(component => component.ParentFoodItemId)
                .Distinct()
                .ToListAsync(cancellationToken);

            frontier = [.. parents.Where(found.Add)];
        }

        // Only reachable if a cycle has been committed somehow, which the write lock and the check
        // exist to prevent. Removing it here means such a graph produces wrong numbers rather than
        // an endless recompute.
        found.Remove(id);

        return found;
    }

    /// <summary>
    /// Orders recipes so that one is recomputed only after every ingredient of it that is also being
    /// recomputed. Kahn's algorithm over the affected subgraph.
    /// </summary>
    /// <remarks>
    /// A recipe whose ingredients are all outside the affected set has nothing to wait for and comes
    /// first. Ingredients that are recipes elsewhere in the catalog but unaffected by this change
    /// are read at their stored values, which are already correct.
    /// </remarks>
    private static List<Guid> InDependencyOrder(HashSet<Guid> recipes, List<Edge> edges)
    {
        var waitingOn = recipes.ToDictionary(id => id, _ => 0);
        var dependents = new Dictionary<Guid, List<Guid>>();

        foreach (var edge in edges)
        {
            if (!waitingOn.ContainsKey(edge.ChildId))
            {
                continue;
            }

            waitingOn[edge.ParentId]++;

            if (!dependents.TryGetValue(edge.ChildId, out var parents))
            {
                dependents[edge.ChildId] = parents = [];
            }

            parents.Add(edge.ParentId);
        }

        var ready = new Queue<Guid>(waitingOn.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var order = new List<Guid>(waitingOn.Count);

        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            order.Add(id);

            if (!dependents.TryGetValue(id, out var parents))
            {
                continue;
            }

            foreach (var parent in parents)
            {
                if (--waitingOn[parent] == 0)
                {
                    ready.Enqueue(parent);
                }
            }
        }

        // A cycle would leave recipes out of the order rather than loop forever. They keep their
        // previous numbers, which are stale but finite - see AncestorsOfAsync.
        return order;
    }

    private readonly record struct Edge(Guid ParentId, Guid ChildId, decimal Quantity);
}

/// <summary>One serving of a recipe, ready to be written onto it.</summary>
public sealed record ComposedNutrition(
    decimal EnergyKcal,
    decimal FatG,
    decimal CarbohydrateG,
    decimal ProteinG,
    IReadOnlyDictionary<string, decimal> Nutrients);
