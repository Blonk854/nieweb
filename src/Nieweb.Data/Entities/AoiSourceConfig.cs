namespace Nieweb.Data.Entities;

/// <summary>
/// Persistent configuration for one AOI data source. Each row spawns an
/// <c>IAoiSource</c> singleton at process start when
/// <see cref="IsEnabled"/> is true, keyed by <see cref="Key"/>. Password
/// is stored encrypted at rest via ASP.NET Core Data Protection.
/// </summary>
/// <remarks>
/// <para>
/// On first boot the table is seeded from the ambient <c>Nieweb:Aoi:*</c>
/// configuration (post-reflow / pre-reflow / fake); subsequent boots
/// treat the DB rows as authoritative. Row edits require an API restart
/// to swap the live singletons — the UI surfaces this via a pending-
/// restart banner and a Restart API button.
/// </para>
/// <para>
/// <see cref="Key"/> is the stable identifier consumed by URL params
/// (<c>sourceId=postreflow</c>, <c>sourceId=fake</c>). It cannot be
/// changed after create (would orphan bookmarks and audit-log entries).
/// </para>
/// </remarks>
public sealed class AoiSourceConfig
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Stable identifier used in URL params and DI keys, e.g.
    /// <c>"postreflow"</c>, <c>"prereflow"</c>, <c>"fake"</c>. Unique
    /// across the tenant. Immutable after create.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-readable label shown in the source picker.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Discriminator that selects the concrete adapter to spawn.
    /// See <see cref="AoiSourceKinds"/>.
    /// </summary>
    public string Kind { get; set; } = AoiSourceKinds.SqlServer;

    /// <summary>
    /// SQL Server host name or DNS alias. Nullable — only meaningful
    /// when <see cref="Kind"/> is <see cref="AoiSourceKinds.SqlServer"/>.
    /// </summary>
    public string? Server { get; set; }

    /// <summary>
    /// Initial catalog / database name. Nullable — only meaningful
    /// when <see cref="Kind"/> is <see cref="AoiSourceKinds.SqlServer"/>.
    /// </summary>
    public string? Database { get; set; }

    /// <summary>
    /// SQL Server login. Read-only account strongly preferred.
    /// Nullable — only meaningful when <see cref="Kind"/> is
    /// <see cref="AoiSourceKinds.SqlServer"/>.
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// Encrypted password bytes produced by
    /// <c>IDataProtector.Protect(Encoding.UTF8.GetBytes(plaintext))</c>.
    /// Nullable — <c>null</c> means "no password stored" (e.g. Fake
    /// kind, or a pending row awaiting a password). The database column
    /// is BLOB so the payload survives verbatim on both SQLite and
    /// PostgreSQL.
    /// </summary>
    public byte[]? EncryptedPassword { get; set; }

    /// <summary>Connect timeout in seconds. Defaults to 15.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>Per-command timeout in seconds. Defaults to 30.</summary>
    public int QueryTimeoutSeconds { get; set; } = 30;

    /// <summary>Trust server certificate. Defaults to true.</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>Encrypt SQL transport. Defaults to false.</summary>
    public bool Encrypt { get; set; }

    /// <summary>
    /// When <c>false</c>, the source is skipped at process start and
    /// vanishes from every <c>IEnumerable&lt;IAoiSource&gt;</c> consumer
    /// (source picker, report endpoints, board-SVG sync). Row is
    /// retained so an admin can toggle it back on without re-typing
    /// credentials.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// UTC timestamp of the last "Test connection" run against this
    /// row's live values (or against a candidate spec matching this
    /// row's key). <c>null</c> until the first test.
    /// </summary>
    public DateTime? LastTestedUtc { get; set; }

    /// <summary>
    /// Result of the most recent test connection. <c>null</c> until
    /// the first test; <c>true</c> on success; <c>false</c> on failure.
    /// </summary>
    public bool? LastTestSucceeded { get; set; }

    /// <summary>
    /// Short human-readable diagnostic from the most recent failed
    /// test. Truncated at 500 chars. <c>null</c> on success or when
    /// the row has never been tested.
    /// </summary>
    public string? LastTestError { get; set; }

    /// <summary>UTC timestamp of first insert.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of the last admin edit.</summary>
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>
/// Stable discriminator values for <see cref="AoiSourceConfig.Kind"/>.
/// Preserved character-for-character across products (same rule as
/// <c>PANEL_*</c> defect status constants) so historical rows remain
/// interpretable.
/// </summary>
public static class AoiSourceKinds
{
    /// <summary>Live SQL Server-backed Superviseur DB (post-reflow / pre-reflow / future).</summary>
    public const string SqlServer = "SqlServer";

    /// <summary>Deterministic in-memory fixture used by the Playwright E2E harness.</summary>
    public const string Fake = "Fake";
}
