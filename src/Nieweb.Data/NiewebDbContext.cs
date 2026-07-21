using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using Nieweb.Data.Entities;

namespace Nieweb.Data;

/// <summary>
/// EF Core context for Nieweb's internal database (users, roles, saved
/// views, audit log). Extends <see cref="IdentityDbContext{TUser,TRole,TKey}"/>
/// so ASP.NET Core Identity's user/role/claim/login/token tables live in
/// the same database as our domain entities.
/// </summary>
/// <remarks>
/// Nieweb owns this database. The remote AOI Superviseur databases
/// (post-reflow / pre-reflow) are read-only and never touched via EF Core;
/// they are accessed exclusively through <c>Nieweb.DataSources.Sql</c>.
/// </remarks>
public sealed class NiewebDbContext : IdentityDbContext<
    NiewebUser,
    NiewebRole,
    int,
    IdentityUserClaim<int>,
    IdentityUserRole<int>,
    IdentityUserLogin<int>,
    IdentityRoleClaim<int>,
    IdentityUserToken<int>>
{
    public NiewebDbContext(DbContextOptions<NiewebDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Append-only audit log. See <see cref="AuditEvent"/> for the shape
    /// and immutability rules.
    /// </summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <summary>
    /// User- or team-owned saved report views. See <see cref="SavedView"/>.
    /// </summary>
    public DbSet<SavedView> SavedViews => Set<SavedView>();

    /// <summary>
    /// Typed key/value knobs surfaced under the "Application parameters"
    /// admin page. See <see cref="AppParameter"/>.
    /// </summary>
    public DbSet<AppParameter> AppParameters => Set<AppParameter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Rename Identity's default AspNet* tables to shorter, Nieweb-owned
        // names. Column names, keys, and indexes are inherited from the
        // Identity base configuration.
        builder.Entity<NiewebUser>(b =>
        {
            b.ToTable("Users");
            b.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(u => u.CreatedUtc).IsRequired();
            b.Property(u => u.LastModifiedUtc).IsRequired();
            b.HasIndex(u => u.IsDisabled);
        });

        builder.Entity<NiewebRole>(b =>
        {
            b.ToTable("Roles");
            b.Property(r => r.Description).HasMaxLength(500);
        });

        builder.Entity<IdentityUserRole<int>>(b => b.ToTable("UserRoles"));
        builder.Entity<IdentityUserClaim<int>>(b => b.ToTable("UserClaims"));
        builder.Entity<IdentityUserLogin<int>>(b => b.ToTable("UserLogins"));
        builder.Entity<IdentityRoleClaim<int>>(b => b.ToTable("RoleClaims"));
        builder.Entity<IdentityUserToken<int>>(b => b.ToTable("UserTokens"));

        builder.Entity<AuditEvent>(b =>
        {
            b.ToTable("AuditEvents");
            b.HasKey(e => e.Id);
            b.Property(e => e.EventTimeUtc).IsRequired();
            b.Property(e => e.ActorDisplayName).HasMaxLength(200).IsRequired();
            b.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            b.Property(e => e.TargetType).HasMaxLength(100).IsRequired();
            b.Property(e => e.TargetId).HasMaxLength(100).IsRequired();
            b.Property(e => e.IpAddress).HasMaxLength(45); // fits IPv6.
            b.HasIndex(e => e.EventTimeUtc);
            b.HasIndex(e => new { e.TargetType, e.TargetId });
            b.HasIndex(e => e.EventType);
        });

        builder.Entity<SavedView>(b =>
        {
            b.ToTable("SavedViews");
            b.HasKey(v => v.Id);
            b.Property(v => v.Name).HasMaxLength(100).IsRequired();
            b.Property(v => v.ReportKey).HasMaxLength(100).IsRequired();
            b.Property(v => v.FilterJson).IsRequired();
            b.Property(v => v.CreatedUtc).IsRequired();
            b.Property(v => v.LastModifiedUtc).IsRequired();
            b.HasIndex(v => new { v.OwnerUserId, v.ReportKey });
            b.HasIndex(v => v.ReportKey);
        });

        builder.Entity<AppParameter>(b =>
        {
            b.ToTable("AppParameters");
            // Key is the natural PK - short, dotted, immutable strings
            // like "msa.gr_r" or "batch.enabled". Bounded at 128 so
            // provider-agnostic indexes fit inside default limits.
            b.HasKey(p => p.Key);
            b.Property(p => p.Key).HasMaxLength(128).IsRequired();
            b.Property(p => p.ValueType).HasMaxLength(16).IsRequired();
            // Value is stored as invariant-culture text; long enough for
            // JSON blobs or path lists if a future report needs them.
            b.Property(p => p.Value).HasMaxLength(2048).IsRequired();
            b.Property(p => p.Description).HasMaxLength(500);
            b.Property(p => p.CreatedUtc).IsRequired();
            b.Property(p => p.LastModifiedUtc).IsRequired();
            b.HasIndex(p => p.IsSystem);
        });
    }
}
