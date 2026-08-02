using System.Diagnostics.CodeAnalysis;

namespace Trackr.Api.Data;

/// <summary>
/// The nutrient set, in memory, for the endpoints that have to validate a key on every write.
/// </summary>
/// <remarks>
/// A singleton is safe here because of one invariant worth stating plainly: <strong>no endpoint
/// ever writes the <c>Nutrients</c> table.</strong> Rows are inserted and updated by
/// <see cref="NutrientSeed"/> at startup and never deleted, so this cannot go stale while the
/// process runs.
/// <para>
/// It reads <see cref="NutrientSeed.All"/> rather than the database, and the two agree by
/// construction: the seeder has already run by the time anything resolves this, so every
/// definition here exists as a row. Code rather than the database is also the right authority for
/// validation - a key the database has and the code does not is a key whose unit nothing knows,
/// which is exactly what must be refused.
/// </para>
/// <para>
/// Behind an injected service rather than reached as a static so a later version can load it from
/// somewhere else without touching a single call site.
/// </para>
/// </remarks>
public sealed class NutrientCatalog
{
    private readonly Dictionary<string, NutrientDefinition> _byKey =
        NutrientSeed.All.ToDictionary(definition => definition.Key, StringComparer.Ordinal);

    /// <summary>Every nutrient, in label order.</summary>
    public IReadOnlyList<NutrientDefinition> All => NutrientSeed.All;

    public bool Contains(string key) => _byKey.ContainsKey(key);

    public bool TryGet(string key, [NotNullWhen(true)] out NutrientDefinition? definition) =>
        _byKey.TryGetValue(key, out definition);
}
