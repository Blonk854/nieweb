using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Nieweb.Data;
using Nieweb.Data.Entities;

namespace Nieweb.Api.DataSources;

/// <summary>
/// EF Core implementation of <see cref="IAoiSourceConfigs"/> backed by
/// <see cref="NiewebDbContext.AoiSourceConfigs"/>.
/// </summary>
public sealed class EfAoiSourceConfigs : IAoiSourceConfigs
{
    private readonly NiewebDbContext _db;
    private readonly IAoiPasswordProtector _protector;
    private readonly TimeProvider _time;

    public EfAoiSourceConfigs(NiewebDbContext db, IAoiPasswordProtector protector, TimeProvider time)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AoiSourceConfigView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.AoiSourceConfigs
            .AsNoTracking()
            .OrderBy(c => c.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToView).ToList();
    }

    /// <inheritdoc/>
    public async Task<AoiSourceConfigView?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var row = await _db.AoiSourceConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == key, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToView(row);
    }

    /// <inheritdoc/>
    public async Task<AoiSourceConfigView> UpsertAsync(AoiSourceConfigSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Validate(spec, isNew: false);
        var now = _time.GetUtcNow().UtcDateTime;

        var row = await _db.AoiSourceConfigs
            .FirstOrDefaultAsync(c => c.Key == spec.Key, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            row = new AoiSourceConfig
            {
                Key = spec.Key.Trim(),
                CreatedUtc = now,
            };
            _db.AoiSourceConfigs.Add(row);
        }

        row.DisplayName = spec.DisplayName.Trim();
        row.Kind = spec.Kind.Trim();
        row.Server = Trimmed(spec.Server);
        row.Database = Trimmed(spec.Database);
        row.User = Trimmed(spec.User);
        // Only re-encrypt when a fresh password was supplied. Empty
        // means "leave the existing blob alone" so admins can edit
        // metadata (display name, enable flag, timeouts) without
        // re-typing the password.
        if (!string.IsNullOrEmpty(spec.Password))
        {
            row.EncryptedPassword = _protector.Protect(spec.Password);
        }
        row.ConnectTimeoutSeconds = spec.ConnectTimeoutSeconds;
        row.QueryTimeoutSeconds = spec.QueryTimeoutSeconds;
        row.TrustServerCertificate = spec.TrustServerCertificate;
        row.Encrypt = spec.Encrypt;
        row.IsEnabled = spec.IsEnabled;
        row.LastModifiedUtc = now;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(row);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var row = await _db.AoiSourceConfigs
            .FirstOrDefaultAsync(c => c.Key == key, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        _db.AoiSourceConfigs.Remove(row);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public async Task<AoiSourceTestResult> TestAsync(AoiSourceConfigSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Validate(spec, isNew: false);

        // Fake sources never open a network connection - success is
        // trivially true so the UI can offer a consistent Test button.
        if (string.Equals(spec.Kind, AoiSourceKinds.Fake, StringComparison.Ordinal))
        {
            var now = _time.GetUtcNow().UtcDateTime;
            await UpdateTestStateAsync(spec.Key, ok: true, error: null, now, cancellationToken).ConfigureAwait(false);
            return new AoiSourceTestResult(Ok: true, DurationMs: 0, ErrorMessage: null);
        }

        // Resolve password: use the spec's plaintext if provided,
        // otherwise fall back to the stored ciphertext so an admin can
        // "Test" an existing row without re-typing.
        string? password = spec.Password;
        if (string.IsNullOrEmpty(password))
        {
            var existing = await _db.AoiSourceConfigs
                .AsNoTracking()
                .Where(c => c.Key == spec.Key)
                .Select(c => c.EncryptedPassword)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            password = _protector.Unprotect(existing);
        }
        if (string.IsNullOrEmpty(password))
        {
            return new AoiSourceTestResult(Ok: false, DurationMs: 0, ErrorMessage: "Password is required for a SQL Server test.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = spec.Server,
            InitialCatalog = spec.Database,
            UserID = spec.User,
            Password = password,
            ApplicationName = $"Nieweb-test-{spec.Key}",
            ConnectTimeout = Math.Max(1, spec.ConnectTimeoutSeconds),
            TrustServerCertificate = spec.TrustServerCertificate,
            Encrypt = spec.Encrypt,
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Trivial read-only probe. WITH (NOLOCK) + READ UNCOMMITTED
            // preserves the read-only discipline mandated for every
            // Superviseur query. TOP 1 keeps the round-trip cheap even
            // against very large PRODUCT tables.
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SET NOCOUNT ON; "
                + "SELECT TOP 1 1 FROM PRODUCT WITH (NOLOCK)";
            cmd.CommandTimeout = Math.Max(1, spec.QueryTimeoutSeconds);
            _ = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();

            var now = _time.GetUtcNow().UtcDateTime;
            await UpdateTestStateAsync(spec.Key, ok: true, error: null, now, cancellationToken).ConfigureAwait(false);
            return new AoiSourceTestResult(Ok: true, DurationMs: sw.ElapsedMilliseconds, ErrorMessage: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            var msg = Truncate(ex.Message, 500);
            var now = _time.GetUtcNow().UtcDateTime;
            await UpdateTestStateAsync(spec.Key, ok: false, error: msg, now, cancellationToken).ConfigureAwait(false);
            return new AoiSourceTestResult(Ok: false, DurationMs: sw.ElapsedMilliseconds, ErrorMessage: msg);
        }
    }

    private async Task UpdateTestStateAsync(string key, bool ok, string? error, DateTime nowUtc, CancellationToken ct)
    {
        var row = await _db.AoiSourceConfigs
            .FirstOrDefaultAsync(c => c.Key == key, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            // No persisted row to update - test was against a candidate
            // spec that hasn't been saved yet. That's fine.
            return;
        }
        row.LastTestedUtc = nowUtc;
        row.LastTestSucceeded = ok;
        row.LastTestError = ok ? null : error;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static void Validate(AoiSourceConfigSpec spec, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(spec.Key))
        {
            throw new ArgumentException("Key is required.", nameof(spec));
        }
        if (spec.Key.Length > 64)
        {
            throw new ArgumentException("Key must be 64 characters or fewer.", nameof(spec));
        }
        if (string.IsNullOrWhiteSpace(spec.DisplayName))
        {
            throw new ArgumentException("Display name is required.", nameof(spec));
        }
        if (spec.DisplayName.Length > 200)
        {
            throw new ArgumentException("Display name must be 200 characters or fewer.", nameof(spec));
        }
        if (spec.Kind != AoiSourceKinds.SqlServer && spec.Kind != AoiSourceKinds.Fake)
        {
            throw new ArgumentException($"Unknown kind '{spec.Kind}'. Use '{AoiSourceKinds.SqlServer}' or '{AoiSourceKinds.Fake}'.", nameof(spec));
        }
        if (spec.Kind == AoiSourceKinds.SqlServer)
        {
            if (string.IsNullOrWhiteSpace(spec.Server))
            {
                throw new ArgumentException("Server is required for SqlServer sources.", nameof(spec));
            }
            if (string.IsNullOrWhiteSpace(spec.Database))
            {
                throw new ArgumentException("Database is required for SqlServer sources.", nameof(spec));
            }
            if (string.IsNullOrWhiteSpace(spec.User))
            {
                throw new ArgumentException("User is required for SqlServer sources.", nameof(spec));
            }
        }
        if (spec.ConnectTimeoutSeconds < 1 || spec.ConnectTimeoutSeconds > 300)
        {
            throw new ArgumentException("ConnectTimeoutSeconds must be between 1 and 300.", nameof(spec));
        }
        if (spec.QueryTimeoutSeconds < 1 || spec.QueryTimeoutSeconds > 600)
        {
            throw new ArgumentException("QueryTimeoutSeconds must be between 1 and 600.", nameof(spec));
        }
    }

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

    private static AoiSourceConfigView ToView(AoiSourceConfig row) => new(
        Key: row.Key,
        DisplayName: row.DisplayName,
        Kind: row.Kind,
        Server: row.Server,
        Database: row.Database,
        User: row.User,
        HasPassword: row.EncryptedPassword is { Length: > 0 },
        ConnectTimeoutSeconds: row.ConnectTimeoutSeconds,
        QueryTimeoutSeconds: row.QueryTimeoutSeconds,
        TrustServerCertificate: row.TrustServerCertificate,
        Encrypt: row.Encrypt,
        IsEnabled: row.IsEnabled,
        LastTestedUtc: row.LastTestedUtc,
        LastTestSucceeded: row.LastTestSucceeded,
        LastTestError: row.LastTestError,
        CreatedUtc: row.CreatedUtc,
        LastModifiedUtc: row.LastModifiedUtc);
}
