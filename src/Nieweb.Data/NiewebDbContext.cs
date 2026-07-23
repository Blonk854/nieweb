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

    /// <summary>
    /// Named groups of AOI / SPI machines that make up a physical SMT
    /// production line. See <see cref="ProductionLine"/>.
    /// </summary>
    public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();

    /// <summary>
    /// Per-line machine assignments. See <see cref="ProductionLineMachine"/>.
    /// </summary>
    public DbSet<ProductionLineMachine> ProductionLineMachines => Set<ProductionLineMachine>();

    /// <summary>
    /// Site-wide shift breakpoints. See <see cref="ShiftBreakpoint"/>.
    /// </summary>
    public DbSet<ShiftBreakpoint> ShiftBreakpoints => Set<ShiftBreakpoint>();

    /// <summary>
    /// Per-machine folders that produce board-layout SVG files (TC4).
    /// See <see cref="BoardSvgSource"/>.
    /// </summary>
    public DbSet<BoardSvgSource> BoardSvgSources => Set<BoardSvgSource>();

    /// <summary>
    /// Named containers for <see cref="Report"/> entries. See
    /// <see cref="ReportGroup"/>.
    /// </summary>
    public DbSet<ReportGroup> ReportGroups => Set<ReportGroup>();

    /// <summary>
    /// User-composed dashboards. See <see cref="Report"/>.
    /// </summary>
    public DbSet<Report> Reports => Set<Report>();

    /// <summary>
    /// Individual tiles inside a report. See <see cref="ReportEntity"/>.
    /// </summary>
    public DbSet<ReportEntity> ReportEntities => Set<ReportEntity>();

    /// <summary>
    /// Configured AOI data-source connections. See <see cref="AoiSourceConfig"/>.
    /// Rows are authoritative once seeded; edits require an API restart.
    /// </summary>
    public DbSet<AoiSourceConfig> AoiSourceConfigs => Set<AoiSourceConfig>();

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

        builder.Entity<ProductionLine>(b =>
        {
            b.ToTable("ProductionLines");
            b.HasKey(l => l.Id);
            b.Property(l => l.Name).HasMaxLength(200).IsRequired();
            b.Property(l => l.CreatedUtc).IsRequired();
            b.Property(l => l.LastModifiedUtc).IsRequired();
            b.HasIndex(l => l.Name).IsUnique();
            b.HasIndex(l => l.DisplayOrder);
        });

        builder.Entity<ProductionLineMachine>(b =>
        {
            b.ToTable("ProductionLineMachines");
            b.HasKey(m => m.Id);
            b.Property(m => m.SourceId).HasMaxLength(64).IsRequired();
            b.Property(m => m.MachineName).HasMaxLength(200).IsRequired();
            b.Property(m => m.Category).HasMaxLength(100);
            b.Property(m => m.CreatedUtc).IsRequired();
            b.HasOne(m => m.ProductionLine)
                .WithMany(l => l.Machines)
                .HasForeignKey(m => m.ProductionLineId)
                .OnDelete(DeleteBehavior.Cascade);
            // A physical machine belongs to at most one line at a time
            // (mirroring Vieweb's nullable machine.PRODUCTION_LINE_ID FK).
            b.HasIndex(m => new { m.SourceId, m.MachineId }).IsUnique();
            b.HasIndex(m => m.ProductionLineId);
        });

        builder.Entity<ShiftBreakpoint>(b =>
        {
            b.ToTable("ShiftBreakpoints");
            b.HasKey(s => s.Id);
            b.Property(s => s.Label).HasMaxLength(100);
            b.Property(s => s.CreatedUtc).IsRequired();
            b.Property(s => s.LastModifiedUtc).IsRequired();
            // (Hour, Minute) is the natural key: a breakpoint is a
            // single wall-clock moment on the 24-hour cycle.
            b.HasIndex(s => new { s.Hour, s.Minute }).IsUnique();
        });

        builder.Entity<BoardSvgSource>(b =>
        {
            b.ToTable("BoardSvgSources");
            b.HasKey(s => s.Id);
            b.Property(s => s.MachineName).HasMaxLength(200).IsRequired();
            // UNC paths can get long (server + share + nested dirs); leave
            // headroom on both providers. 1024 is comfortably below the
            // Windows MAX_PATH of \\?\-prefixed extended paths.
            b.Property(s => s.UncPath).HasMaxLength(1024).IsRequired();
            b.Property(s => s.LastSyncError).HasMaxLength(500);
            b.Property(s => s.CreatedUtc).IsRequired();
            b.Property(s => s.LastModifiedUtc).IsRequired();
            b.HasIndex(s => s.MachineName).IsUnique();
        });

        builder.Entity<ReportGroup>(b =>
        {
            b.ToTable("ReportGroups");
            b.HasKey(g => g.Id);
            b.Property(g => g.Name).HasMaxLength(200).IsRequired();
            b.Property(g => g.CreatedUtc).IsRequired();
            b.Property(g => g.LastModifiedUtc).IsRequired();
            b.HasIndex(g => g.Name).IsUnique();
            b.HasIndex(g => g.DisplayOrder);
        });

        builder.Entity<Report>(b =>
        {
            b.ToTable("Reports");
            b.HasKey(r => r.Id);
            b.Property(r => r.Title).HasMaxLength(200).IsRequired();
            b.Property(r => r.Description).HasMaxLength(1000);
            b.Property(r => r.OwnerDisplayName).HasMaxLength(200).IsRequired();
            // Argon2id PHC-encoded lock password (RC3). ~200 chars fits
            // comfortably in the default 500 cap.
            b.Property(r => r.LockPasswordHash).HasMaxLength(500);
            b.Property(r => r.CreatedUtc).IsRequired();
            b.Property(r => r.LastModifiedUtc).IsRequired();
            // ChromeJson is small on both providers but may hold
            // richer chrome config in the future; leave it unbounded.
            b.HasOne(r => r.Group)
                .WithMany(g => g.Reports)
                .HasForeignKey(r => r.ReportGroupId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(r => r.ReportGroupId);
            b.HasIndex(r => r.OwnerUserId);
            b.HasIndex(r => r.IsPinnedHome);
            b.HasIndex(r => r.DisplayOrder);
        });

        builder.Entity<ReportEntity>(b =>
        {
            b.ToTable("ReportEntities");
            b.HasKey(e => e.Id);
            b.Property(e => e.TileType).HasMaxLength(100).IsRequired();
            b.Property(e => e.Title).HasMaxLength(200);
            b.Property(e => e.ConfigJson).IsRequired();
            b.Property(e => e.CreatedUtc).IsRequired();
            b.Property(e => e.LastModifiedUtc).IsRequired();
            b.HasOne(e => e.Report)
                .WithMany(r => r.Entities)
                .HasForeignKey(e => e.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(e => new { e.ReportId, e.DisplayOrder });
        });

        builder.Entity<AoiSourceConfig>(b =>
        {
            b.ToTable("AoiSourceConfigs");
            b.HasKey(c => c.Id);
            // Key is the stable identifier consumed by URL params and
            // audit rows - unique across the tenant, immutable after
            // create. 64 chars comfortably fits every existing id
            // ("postreflow", "prereflow", "fake") and any future ones.
            b.Property(c => c.Key).HasMaxLength(64).IsRequired();
            b.Property(c => c.DisplayName).HasMaxLength(200).IsRequired();
            // Kind is one of the AoiSourceKinds constants. 32 chars
            // leaves headroom for future adapters ("MySql", ...).
            b.Property(c => c.Kind).HasMaxLength(32).IsRequired();
            b.Property(c => c.Server).HasMaxLength(200);
            b.Property(c => c.Database).HasMaxLength(200);
            b.Property(c => c.User).HasMaxLength(200);
            // EncryptedPassword is the raw output of
            // IDataProtector.Protect(UTF8(plaintext)). BLOB on both
            // providers so the payload survives verbatim.
            b.Property(c => c.EncryptedPassword);
            b.Property(c => c.ConnectTimeoutSeconds).IsRequired();
            b.Property(c => c.QueryTimeoutSeconds).IsRequired();
            b.Property(c => c.TrustServerCertificate).IsRequired();
            b.Property(c => c.Encrypt).IsRequired();
            b.Property(c => c.IsEnabled).IsRequired();
            b.Property(c => c.LastTestError).HasMaxLength(500);
            b.Property(c => c.CreatedUtc).IsRequired();
            b.Property(c => c.LastModifiedUtc).IsRequired();
            b.HasIndex(c => c.Key).IsUnique();
            b.HasIndex(c => c.IsEnabled);
        });
    }
}
