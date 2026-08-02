using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Trackr.Api.Identity;

namespace Trackr.Api.Data;

/// <summary>
/// The application's EF Core context.
/// </summary>
/// <remarks>
/// Milestone 2 turns this into the Identity store. The food catalog, log entries and the
/// extensible nutrient store arrive in milestone 3 (see CLAUDE.md section 7).
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
    }
}
