using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace Nieweb.DataSources.Sql;

/// <summary>
/// Base class for SQL Server-backed AOI Superviseur sources. Owns connection
/// composition and enforces every read-only guard listed in
/// <c>.github/copilot-instructions.md</c>:
///
/// - Forbidden-keyword regex on all SQL text.
/// - <see cref="SqlGuards.IsolationPrelude"/> prepended to every batch.
/// - Per-source <c>Application Name</c> tag on the connection string.
/// - Query timeout capped by <see cref="AoiSourceOptions.QueryTimeoutSeconds"/>.
///
/// Concrete subclasses (<c>HlyaoiSource</c>, <c>MeaoiSource</c>) supply the
/// <see cref="IAoiSource.Descriptor"/> and the source-specific SQL text.
/// </summary>
public abstract class SqlServerAoiSourceBase : IAoiSource
{
    private readonly AoiSourceOptions _options;
    private readonly string _connectionString;

    protected SqlServerAoiSourceBase(AoiSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = options.Server,
            InitialCatalog = options.Database,
            UserID = options.User,
            Password = options.Password,
            ApplicationName = $"Nieweb-{SourceTag}",
            ConnectTimeout = options.ConnectTimeoutSeconds,
            TrustServerCertificate = options.TrustServerCertificate,
            Encrypt = options.Encrypt,
        };
        _connectionString = builder.ConnectionString;
    }

    /// <inheritdoc />
    public abstract SourceDescriptor Descriptor { get; }

    /// <summary>Short identifier baked into <c>Application Name</c>, e.g. "postreflow".</summary>
    protected abstract string SourceTag { get; }

    /// <summary>Opens a new <see cref="SqlConnection"/>. Caller owns disposal.</summary>
    protected async Task<SqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Streams the rows produced by <paramref name="sql"/> through
    /// <paramref name="map"/>. The SQL is guarded (write keywords rejected)
    /// and prefixed with the standard isolation prelude.
    /// </summary>
    protected async IAsyncEnumerable<T> ExecuteQueryAsync<T>(
        string sql,
        Action<SqlParameterCollection>? bindParameters,
        Func<SqlDataReader, T> map,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(map);
        SqlGuards.EnsureReadOnly(sql);

        await using var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlGuards.IsolationPrelude + sql;
        cmd.CommandTimeout = _options.QueryTimeoutSeconds;
        bindParameters?.Invoke(cmd.Parameters);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return map(reader);
        }
    }

    /// <summary>
    /// Materialises the entire result set into a list. Convenience for small
    /// reference queries (MACHINE, PRODUCT, RECIPE); do not use on PANELS /
    /// CARDS / TESTED_OBJECT (use pagination or streaming instead).
    /// </summary>
    protected async Task<IReadOnlyList<T>> ExecuteListAsync<T>(
        string sql,
        Action<SqlParameterCollection>? bindParameters,
        Func<SqlDataReader, T> map,
        CancellationToken ct)
    {
        var list = new List<T>();
        await foreach (var row in ExecuteQueryAsync(sql, bindParameters, map, ct).ConfigureAwait(false))
        {
            list.Add(row);
        }
        return list;
    }

    /// <summary>
    /// Validates that a <see cref="BaseQuery"/> carries a reasonable
    /// <see cref="DateRange"/>. The <see cref="DateRange"/> constructor already
    /// enforces start &lt; end; this adds a sanity cap so a runaway "last 10
    /// years" filter can't sneak through.
    /// </summary>
    protected static void ValidateWindow(BaseQuery query, TimeSpan? maxDuration = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var cap = maxDuration ?? TimeSpan.FromDays(400); // ~13 months, generous
        if (query.Window.Duration > cap)
        {
            throw new ArgumentException(
                $"Query window {query.Window.Duration} exceeds the {cap} safety cap. " +
                "Narrow the date range or lift the cap explicitly for this call site.",
                nameof(query));
        }
    }

    // ---- IAoiSource contract: default abstract members ----------------------
    // Concrete subclasses implement these with source-appropriate SQL. Even
    // though MACHINE/PRODUCT/RECIPE column sets are identical between post-
    // and pre-reflow, we keep the SQL in the subclasses so a future schema
    // divergence (or a Koh Young source built on a totally different shape)
    // doesn't require a base-class fork.

    public abstract Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(PanelQuery query, CancellationToken ct);

    public abstract Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery query, CancellationToken ct);

    public abstract Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct);

    public abstract IAsyncEnumerable<PanelRow> StreamPanelsAsync(PanelQuery query, CancellationToken ct);

    public abstract Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct);

    public abstract Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct);

    public abstract Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct);
}
