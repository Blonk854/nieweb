namespace Nieweb.Api.DataSources;

/// <summary>
/// Snapshot of an AOI data-source row exposed to the admin API.
/// Never carries a decrypted password.
/// </summary>
/// <param name="Key">Stable identifier ("postreflow", "prereflow", "fake"…).</param>
/// <param name="DisplayName">Human-readable label.</param>
/// <param name="Kind">Discriminator: "SqlServer" or "Fake".</param>
/// <param name="Server">SQL Server host (SqlServer kind).</param>
/// <param name="Database">Database name (SqlServer kind).</param>
/// <param name="User">SQL login (SqlServer kind).</param>
/// <param name="HasPassword">True when the encrypted-password blob is stored.</param>
/// <param name="ConnectTimeoutSeconds">Connect timeout in seconds.</param>
/// <param name="QueryTimeoutSeconds">Per-command timeout in seconds.</param>
/// <param name="TrustServerCertificate">Trust the server certificate.</param>
/// <param name="Encrypt">Encrypt the SQL transport.</param>
/// <param name="IsEnabled">Enrolled in <c>IEnumerable&lt;IAoiSource&gt;</c> at process start.</param>
/// <param name="LastTestedUtc">UTC time of the most recent test connection attempt.</param>
/// <param name="LastTestSucceeded">Result of the most recent test connection attempt.</param>
/// <param name="LastTestError">Diagnostic from the most recent failed test.</param>
/// <param name="CreatedUtc">Row create time.</param>
/// <param name="LastModifiedUtc">Row last-modify time.</param>
public sealed record AoiSourceConfigView(
    string Key,
    string DisplayName,
    string Kind,
    string? Server,
    string? Database,
    string? User,
    bool HasPassword,
    int ConnectTimeoutSeconds,
    int QueryTimeoutSeconds,
    bool TrustServerCertificate,
    bool Encrypt,
    bool IsEnabled,
    DateTime? LastTestedUtc,
    bool? LastTestSucceeded,
    string? LastTestError,
    DateTime CreatedUtc,
    DateTime LastModifiedUtc);

/// <summary>
/// Payload accepted by upsert / test endpoints. When
/// <see cref="Password"/> is <c>null</c> or empty on an update the
/// existing encrypted password is preserved.
/// </summary>
public sealed record AoiSourceConfigSpec(
    string Key,
    string DisplayName,
    string Kind,
    string? Server,
    string? Database,
    string? User,
    string? Password,
    int ConnectTimeoutSeconds,
    int QueryTimeoutSeconds,
    bool TrustServerCertificate,
    bool Encrypt,
    bool IsEnabled);

/// <summary>
/// Result of an ad-hoc "Test connection" attempt against a candidate
/// spec (which may or may not correspond to a persisted row).
/// </summary>
public sealed record AoiSourceTestResult(
    bool Ok,
    long DurationMs,
    string? ErrorMessage);
