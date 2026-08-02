using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Trackr.Api.Identity;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Data;

/// <summary>
/// The application's EF Core context.
/// </summary>
/// <remarks>
/// Milestone 2 turned this into the Identity store; milestone 6 added the food catalog, the log
/// and the extensible nutrient store (see CLAUDE.md section 7).
/// <para>
/// Roles are mapped even though none are ever created. It costs three empty tables and
/// buys the conventional store shape (UserStore rather than UserOnlyStore), which keeps
/// this matching every piece of Identity documentation, and leaves "only the owner may
/// mint invites" available later as a data change rather than a migration.
/// </para>
/// </remarks>
public class TrackrDbContext(DbContextOptions<TrackrDbContext> options)
    : IdentityDbContext<TrackrUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Invite> Invites => Set<Invite>();

    public DbSet<UserAvatar> UserAvatars => Set<UserAvatar>();

    /// <summary>
    /// The nutrient reference set. Written only by <see cref="NutrientSeed"/> at startup - no
    /// endpoint inserts, updates or deletes a row here.
    /// </summary>
    public DbSet<Nutrient> Nutrients => Set<Nutrient>();

    public DbSet<FoodItem> FoodItems => Set<FoodItem>();

    public DbSet<FoodItemNutrient> FoodItemNutrients => Set<FoodItemNutrient>();

    public DbSet<LogEntry> LogEntries => Set<LogEntry>();

    public DbSet<LogItem> LogItems => Set<LogItem>();

    public DbSet<LogItemNutrient> LogItemNutrients => Set<LogItemNutrient>();

    public DbSet<MealImage> MealImages => Set<MealImage>();

    /// <summary>
    /// The data-protection key ring, which is what encrypts the session cookie and every
    /// Identity token. It lives in Postgres rather than on disk - see Program.cs.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Must come first: this is what maps the AspNet* tables. Our own configuration
        // then refines the model it produced.
        base.OnModelCreating(builder);

        builder.Entity<TrackrUser>()
            // TrackrUser's constructor assigns a version 7 GUID. Without this, EF's
            // convention for Guid keys marks the property ValueGeneratedOnAdd and
            // substitutes its own value.
            .Property(u => u.Id)
            .ValueGeneratedNever();

        builder.Entity<UserAvatar>(avatar =>
        {
            // The foreign key is the primary key, which is what makes it one-per-account
            // without a unique index on top.
            avatar.HasKey(a => a.UserId);

            avatar.Property(a => a.ContentType).HasMaxLength(64).IsRequired();
            avatar.Property(a => a.Content).IsRequired();

            // Cascade, unlike Invite's Restrict: an avatar is the account's own data with no
            // record-keeping value once the account is gone, whereas an invite records who
            // vouched for whom.
            avatar.HasOne(a => a.User)
                .WithOne()
                .HasForeignKey<UserAvatar>(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Invite>(invite =>
        {
            invite.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();
            invite.Property(i => i.TokenPrefix).HasMaxLength(8).IsRequired();
            invite.Property(i => i.Note).HasMaxLength(200);

            // Redemption looks an invite up by hash, and a collision would mean two
            // invites share a token.
            invite.HasIndex(i => i.TokenHash).IsUnique();

            // Two foreign keys to the same principal table, so both relationships must
            // be configured explicitly. Restrict rather than cascade: deleting an
            // account must not silently erase the record of who invited whom.
            invite.HasOne(i => i.CreatedBy)
                .WithMany()
                .HasForeignKey(i => i.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            invite.HasOne(i => i.RedeemedBy)
                .WithMany()
                .HasForeignKey(i => i.RedeemedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureNutrition(builder);
    }

    /// <summary>
    /// The food catalog, the log and the nutrient store (CLAUDE.md section 7).
    /// </summary>
    /// <remarks>
    /// Money-like numbers are <c>decimal</c> mapped to <c>numeric(12,4)</c> throughout. Milestone
    /// 11 sums thousands of label values, and <c>double</c> accumulates representation error that
    /// exact decimal arithmetic does not - the same reason section 5's 4/4/9 kcal reconciliation is
    /// easier to reason about here. Quantities and serving sizes get <c>numeric(10,3)</c>.
    /// </remarks>
    private static void ConfigureNutrition(ModelBuilder builder)
    {
        builder.Entity<Nutrient>(nutrient =>
        {
            // The key is the primary key. See Nutrient's remarks: it means an amount row's foreign
            // key already IS the wire key, so projecting a nutrient map needs no join to this table.
            nutrient.HasKey(n => n.Key);
            nutrient.Property(n => n.Key).HasMaxLength(32);
            nutrient.Property(n => n.DisplayName).HasMaxLength(64).IsRequired();

            // Stored as text rather than as an int, so a pg_dump stays readable and inserting a
            // group later cannot renumber the rows already written.
            nutrient.Property(n => n.Unit).HasConversion<string>().HasMaxLength(16).IsRequired();
            nutrient.Property(n => n.Group).HasConversion<string>().HasMaxLength(32).IsRequired();
        });

        builder.Entity<FoodItem>(food =>
        {
            // The constructor assigns a version 7 GUID, so EF must not substitute its own.
            food.Property(f => f.Id).ValueGeneratedNever();

            food.Property(f => f.Name).HasMaxLength(200).IsRequired();
            food.Property(f => f.Brand).HasMaxLength(120);
            // GTIN-14 is fourteen digits; the extra room covers prefixed and padded forms.
            food.Property(f => f.Barcode).HasMaxLength(32);
            food.Property(f => f.ServingUnit).HasMaxLength(32).IsRequired();
            food.Property(f => f.ServingSize).HasPrecision(10, 3);
            food.Property(f => f.Source).HasConversion<string>().HasMaxLength(16).IsRequired();

            food.Property(f => f.EnergyKcal).HasPrecision(12, 4);
            food.Property(f => f.FatG).HasPrecision(12, 4);
            food.Property(f => f.CarbohydrateG).HasPrecision(12, 4);
            food.Property(f => f.ProteinG).HasPrecision(12, 4);

            // Two foreign keys to the same principal table, so both are configured explicitly -
            // as on Invite. The delete behaviours differ on purpose and are explained on the entity.
            food.HasOne(f => f.User)
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            food.HasOne(f => f.UpdatedBy)
                .WithMany()
                .HasForeignKey(f => f.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // The catalog list and milestone 10's re-log picker. A btree indexes nulls, so this
            // serves the global half of "mine or global" as well as the personal half.
            food.HasIndex(f => new { f.UserId, f.Name });

            // Two partial unique indexes rather than one: a barcode is unique among global items,
            // and unique per account among personal ones. Together they are what lets a household
            // scan a product once while an individual can still keep a private override of it.
            food.HasIndex(f => f.Barcode)
                .IsUnique()
                .HasFilter("\"UserId\" IS NULL");

            food.HasIndex(f => new { f.UserId, f.Barcode })
                .IsUnique()
                .HasFilter("\"UserId\" IS NOT NULL");
        });

        builder.Entity<FoodItemNutrient>(amount =>
        {
            amount.HasKey(a => new { a.FoodItemId, a.NutrientKey });
            amount.Property(a => a.NutrientKey).HasMaxLength(32);
            amount.Property(a => a.Amount).HasPrecision(12, 4);

            amount.HasOne(a => a.FoodItem)
                .WithMany(f => f.Nutrients)
                .HasForeignKey(a => a.FoodItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict, so deleting a nutrient somebody has measured fails loudly rather than
            // quietly discarding measurements. Retiring one later is a flag, never a DELETE.
            amount.HasOne(a => a.Nutrient)
                .WithMany()
                .HasForeignKey(a => a.NutrientKey)
                .OnDelete(DeleteBehavior.Restrict);

            amount.ToTable(table => table.HasCheckConstraint(
                "CK_FoodItemNutrients_NotCore",
                NotCoreConstraint));
        });

        builder.Entity<LogEntry>(entry =>
        {
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.Note).HasMaxLength(500);

            entry.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // One index serves day, week and month alike: Postgres scans a btree backwards, so no
            // descending variant is needed. EF already creates the plain foreign-key indexes.
            entry.HasIndex(e => new { e.UserId, e.LoggedUtc });
        });

        builder.Entity<LogItem>(item =>
        {
            item.Property(i => i.Id).ValueGeneratedNever();
            item.Property(i => i.Name).HasMaxLength(200).IsRequired();
            item.Property(i => i.Brand).HasMaxLength(120);
            item.Property(i => i.ServingUnit).HasMaxLength(32);
            item.Property(i => i.Quantity).HasPrecision(10, 3);
            item.Property(i => i.ServingSize).HasPrecision(10, 3);

            item.Property(i => i.EnergyKcal).HasPrecision(12, 4);
            item.Property(i => i.FatG).HasPrecision(12, 4);
            item.Property(i => i.CarbohydrateG).HasPrecision(12, 4);
            item.Property(i => i.ProteinG).HasPrecision(12, 4);

            item.HasOne(i => i.LogEntry)
                .WithMany(e => e.Items)
                .HasForeignKey(i => i.LogEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            // SetNull: tidying the catalog must not erase history, and a logged item must not be
            // undeletable. The snapshot on this row is what survives.
            item.HasOne(i => i.FoodItem)
                .WithMany()
                .HasForeignKey(i => i.FoodItemId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<LogItemNutrient>(amount =>
        {
            amount.HasKey(a => new { a.LogItemId, a.NutrientKey });
            amount.Property(a => a.NutrientKey).HasMaxLength(32);
            amount.Property(a => a.Amount).HasPrecision(12, 4);

            amount.HasOne(a => a.LogItem)
                .WithMany(i => i.Nutrients)
                .HasForeignKey(a => a.LogItemId)
                .OnDelete(DeleteBehavior.Cascade);

            amount.HasOne(a => a.Nutrient)
                .WithMany()
                .HasForeignKey(a => a.NutrientKey)
                .OnDelete(DeleteBehavior.Restrict);

            amount.ToTable(table => table.HasCheckConstraint(
                "CK_LogItemNutrients_NotCore",
                NotCoreConstraint));
        });

        builder.Entity<MealImage>(image =>
        {
            image.Property(i => i.Id).ValueGeneratedNever();
            image.Property(i => i.ContentType).HasMaxLength(64).IsRequired();
            image.Property(i => i.Content).IsRequired();

            image.HasOne(i => i.User)
                .WithMany()
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cascade fires only for non-null foreign keys, so deleting an entry takes its photos
            // while images still waiting to be claimed are untouched.
            image.HasOne(i => i.LogEntry)
                .WithMany(e => e.Images)
                .HasForeignKey(i => i.LogEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            // For milestone 14's sweep of images that were uploaded and never confirmed.
            image.HasIndex(i => new { i.UserId, i.CreatedUtc });
        });
    }

    /// <summary>
    /// Keeps the four always-present nutrients out of the amount tables, where they would be a
    /// second copy of a value that already has a column - and a double count for any client that
    /// summed the map and then added the typed fields.
    /// </summary>
    /// <remarks>
    /// Enforced twice on purpose: here, and as a friendly 400 at the API boundary naming the
    /// offending key. The constraint is the one that cannot be forgotten.
    /// </remarks>
    private static string NotCoreConstraint =>
        $"\"NutrientKey\" NOT IN ({string.Join(", ", CoreNutrients.Keys.Select(key => $"'{key}'"))})";
}
