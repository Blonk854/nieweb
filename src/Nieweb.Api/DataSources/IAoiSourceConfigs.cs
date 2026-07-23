namespace Nieweb.Api.DataSources;

/// <summary>
/// Admin-facing service for <see cref="Nieweb.Data.Entities.AoiSourceConfig"/>
/// rows. All mutating calls audit their intent through
/// <c>IAuditLogger</c> at the endpoint layer.
/// </summary>
/// <remarks>
/// Row edits do not affect the running process — active
/// <c>IAoiSource</c> singletons are bound at boot from the DB rows and
/// stay pinned until the next API restart. The UI surfaces this via
/// the "Restart API" banner.
/// </remarks>
public interface IAoiSourceConfigs
{
    /// <summary>Returns all rows ordered by <c>Key</c> — passwords omitted.</summary>
    Task<IReadOnlyList<AoiSourceConfigView>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches a single row by <c>Key</c> — password omitted.</summary>
    Task<AoiSourceConfigView?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates a row keyed by <see cref="AoiSourceConfigSpec.Key"/>.
    /// On update, an empty <see cref="AoiSourceConfigSpec.Password"/>
    /// preserves the existing encrypted blob.
    /// </summary>
    /// <returns>The persisted view (password omitted).</returns>
    Task<AoiSourceConfigView> UpsertAsync(AoiSourceConfigSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Deletes the row identified by <paramref name="key"/>.</summary>
    /// <returns><c>true</c> if a row was deleted; <c>false</c> if not found.</returns>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a transient connection to <paramref name="spec"/>, issues a
    /// trivial read-only probe, and returns the outcome. Never persists.
    /// When <paramref name="spec"/>'s <see cref="AoiSourceConfigSpec.Password"/>
    /// is empty and a row with the same <see cref="AoiSourceConfigSpec.Key"/>
    /// exists, the stored password is used for the probe (so admins can
    /// re-test without re-typing the password).
    /// </summary>
    Task<AoiSourceTestResult> TestAsync(AoiSourceConfigSpec spec, CancellationToken cancellationToken = default);
}
