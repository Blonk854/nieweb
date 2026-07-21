using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
public abstract partial class SqlServerAoiSourceBase : IAoiSource
{
    private readonly AoiSourceOptions _options;
    private readonly string _connectionString;
    private readonly ILogger _log;

    protected SqlServerAoiSourceBase(AoiSourceOptions options, ILogger<SqlServerAoiSourceBase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _log = (ILogger?)logger ?? NullLogger.Instance;

        // ApplicationName includes both the source tag and the target database
        // so a DBA glancing at sys.dm_exec_sessions can immediately tell
        // (a) which Nieweb source is talking and (b) which catalogue it hit
        // (e.g. `Nieweb-postreflow-HLYAOI2024`). This matters because we run
        // against a live inspection DB with a write-capable service account.
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = options.Server,
            InitialCatalog = options.Database,
            UserID = options.User,
            Password = options.Password,
            ApplicationName = $"Nieweb-{SourceTag}-{options.Database}",
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
    /// and prefixed with the standard isolation prelude. Every invocation is
    /// logged (source tag, first line of SQL, parameter count, duration,
    /// row count) so we have an audit trail against a live Superviseur DB.
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

        var sw = Stopwatch.StartNew();
        var rowCount = 0;
        var sqlTag = SqlSummary(sql);

        await using var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlGuards.IsolationPrelude + sql;
        cmd.CommandTimeout = _options.QueryTimeoutSeconds;
        bindParameters?.Invoke(cmd.Parameters);
        var paramCount = cmd.Parameters.Count;

        SqlDataReader reader;
        try
        {
            reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAoiQueryFailed(_log, SourceTag, _options.Database, sqlTag, paramCount, sw.ElapsedMilliseconds, ex);
            throw;
        }

        await using (reader.ConfigureAwait(false))
        {
            try
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    rowCount++;
                    yield return map(reader);
                }
            }
            finally
            {
                sw.Stop();
                var elapsed = sw.ElapsedMilliseconds;
                // Warn if we exceed 5s or half the configured timeout,
                // whichever is smaller. Anything close to the cap is a red
                // flag on a live line DB.
                var warnThreshold = Math.Min(5000L, _options.QueryTimeoutSeconds * 500L);
                if (elapsed >= warnThreshold)
                {
                    LogAoiQuerySlow(_log, SourceTag, _options.Database, sqlTag, paramCount, rowCount, elapsed);
                }
                else
                {
                    LogAoiQuery(_log, SourceTag, _options.Database, sqlTag, paramCount, rowCount, elapsed);
                }
            }
        }
    }

    // Source-generated logger delegates — avoids CA1848 boxing and lets
    // Serilog / OpenTelemetry treat each field as a structured property.
    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "AOI query: source={SourceTag} db={Database} sql={SqlTag} params={ParamCount} rows={RowCount} durationMs={DurationMs}")]
    private static partial void LogAoiQuery(
        ILogger logger, string sourceTag, string database, string sqlTag,
        int paramCount, int rowCount, long durationMs);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Warning,
        Message = "AOI query slow: source={SourceTag} db={Database} sql={SqlTag} params={ParamCount} rows={RowCount} durationMs={DurationMs}")]
    private static partial void LogAoiQuerySlow(
        ILogger logger, string sourceTag, string database, string sqlTag,
        int paramCount, int rowCount, long durationMs);

    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Error,
        Message = "AOI query failed: source={SourceTag} db={Database} sql={SqlTag} params={ParamCount} durationMs={DurationMs}")]
    private static partial void LogAoiQueryFailed(
        ILogger logger, string sourceTag, string database, string sqlTag,
        int paramCount, long durationMs, Exception exception);

    /// <summary>Compact one-line tag for the audit log (first non-blank line, capped).</summary>
    private static string SqlSummary(string sql)
    {
        const int MaxLength = 120;
        var span = sql.AsSpan();
        // Skip leading whitespace.
        var i = 0;
        while (i < span.Length && char.IsWhiteSpace(span[i]))
        {
            i++;
        }
        // Take up to the first newline or MaxLength chars.
        var end = i;
        while (end < span.Length && span[end] != '\r' && span[end] != '\n' && end - i < MaxLength)
        {
            end++;
        }
        return span[i..end].ToString();
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
    // Reference-data (MACHINE/PRODUCT/RECIPE) queries stay abstract: even
    // though the columns are identical today, a future Kohyoung source or a
    // schema-diverged fork should be free to shape them differently.
    //
    // Fact-table queries against PANELS/CARDS/TESTED_OBJECT are shared here:
    // both HLYAOI (v5.0) and MEAOI (v4.3.1) expose the same universal column
    // set we consume, and per-source deviations are toggled via
    // <see cref="Capabilities"/> flags on the <see cref="Descriptor"/>.

    /// <inheritdoc />
    public virtual async Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(
        PanelQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateWindow(query);
        var pageSize = ValidatePageSize(query.PageSize);

        var (sql, bind) = BuildPanelsQuery(query, pageSize + 1);

        var rows = new List<PanelRow>(pageSize + 1);
        await foreach (var row in ExecuteQueryAsync(sql, bind, MapPanelRow, ct).ConfigureAwait(false))
        {
            rows.Add(row);
        }

        var hasMore = rows.Count > pageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        PanelCursor? next = hasMore && rows.Count > 0
            ? new PanelCursor(rows[^1].PanelNumericDate, rows[^1].PanelId)
            : null;

        return new Page<PanelRow, PanelCursor>(rows, next, hasMore);
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<PanelRow> StreamPanelsAsync(
        PanelQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Each page validates itself, but do a fail-fast up front.
        ValidateWindow(query);
        _ = ValidatePageSize(query.PageSize);

        var current = query;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await QueryPanelsAsync(current, ct).ConfigureAwait(false);
            foreach (var row in page.Rows)
            {
                yield return row;
            }
            if (!page.HasMore || page.NextCursor is not PanelCursor next)
            {
                yield break;
            }
            current = current with { Cursor = next };
        }
    }

    public abstract Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery query, CancellationToken ct);

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation loops <see cref="QueryCardsAsync"/> keyset
    /// pages. Adapters that have a cheaper streaming path
    /// (e.g. sqlclient <c>ExecuteReader</c> against a joined query)
    /// override this.
    /// </remarks>
    public virtual async IAsyncEnumerable<CardRow> StreamCardsAsync(
        CardQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateWindow(query);
        _ = ValidatePageSize(query.PageSize);

        var current = query;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await QueryCardsAsync(current, ct).ConfigureAwait(false);
            foreach (var row in page.Rows)
            {
                yield return row;
            }
            if (!page.HasMore || page.NextCursor is not CardCursor next)
            {
                yield break;
            }
            current = current with { Cursor = next };
        }
    }

    public abstract Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct);

    public abstract Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct);

    public abstract Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct);

    public abstract Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct);

    /// <inheritdoc />
    public virtual async Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct)
    {
        const string Sql = """
            SELECT MAX(Panel_Numeric_Date) FROM dbo.PANELS WITH (NOLOCK);
            """;

        int? epoch = null;
        await foreach (var row in ExecuteQueryAsync(
            Sql,
            bindParameters: null,
            map: static r => r.IsDBNull(0) ? (int?)null : r.GetInt32(0),
            ct).ConfigureAwait(false))
        {
            epoch = row;
        }

        return epoch is int e
            ? DateTimeOffset.FromUnixTimeSeconds(e).UtcDateTime
            : null;
    }

    // ---- Shared PANELS query builder ---------------------------------------

    /// <summary>Upper bound on IN-list filter size to keep parameter counts sane.</summary>
    private const int MaxInListSize = 500;

    /// <summary>Upper bound on <see cref="PanelQuery.PageSize"/> to keep memory bounded.</summary>
    private const int MaxPageSize = 10_000;

    private static int ValidatePageSize(int pageSize)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize),
                pageSize, "PageSize must be greater than zero.");
        }
        return Math.Min(pageSize, MaxPageSize);
    }

    private (string Sql, Action<SqlParameterCollection> Bind) BuildPanelsQuery(
        PanelQuery q, int topCount)
    {
        EnsureUnderInListCap(q.MachineIds, nameof(q.MachineIds));
        EnsureUnderInListCap(q.ProductIds, nameof(q.ProductIds));
        EnsureUnderInListCap(q.RecipeIds, nameof(q.RecipeIds));

        // Panel_Numeric_Date is int32 (ANSI time_t). Fail fast in 2038 rather
        // than silently truncating a bigint parameter.
        var startEpoch = checked((int)q.Window.StartEpochSeconds);
        var endEpoch = checked((int)q.Window.EndEpochSecondsExclusive);

        var sb = new StringBuilder(1024);
        sb.Append(
            """
            SELECT TOP (@topCount)
              Panel_Id, Machine_Id, Lane_Number, Panel_Bar_Code, Panel_Numeric_Date,
              Nb_Of_Valid_Cards, Test_Time, Panel_Status, Anomaly_BR, Anomaly_AR,
              Has_Been_Reviewed, Nb_Of_Tested_Object, Nb_Of_Error_Object,
              Operator_Id, Product_Id, Recipe_Id
            FROM dbo.PANELS WITH (NOLOCK)
            WHERE Panel_Numeric_Date >= @startEpoch
              AND Panel_Numeric_Date <  @endEpoch
            """);

        var useLastInsp =
            q.OnlyLastInspection &&
            Descriptor.Caps.HasFlag(Capabilities.IsLastInspectionFilter);
        if (useLastInsp)
        {
            sb.AppendLine().Append("  AND IS_LAST_INSPECTION = 1");
        }

        AppendInClause(sb, "Machine_Id", "@m", q.MachineIds);
        AppendInClause(sb, "Product_Id", "@p", q.ProductIds);
        AppendInClause(sb, "Recipe_Id", "@r", q.RecipeIds);

        if (q.Cursor is not null)
        {
            sb.AppendLine().Append(
                """
                  AND (Panel_Numeric_Date > @cursorDate
                    OR (Panel_Numeric_Date = @cursorDate AND Panel_Id > @cursorId))
                """);
        }

        sb.AppendLine().Append("ORDER BY Panel_Numeric_Date, Panel_Id;");

        void Bind(SqlParameterCollection p)
        {
            p.Add(new SqlParameter("@topCount", SqlDbType.Int) { Value = topCount });
            p.Add(new SqlParameter("@startEpoch", SqlDbType.Int) { Value = startEpoch });
            p.Add(new SqlParameter("@endEpoch", SqlDbType.Int) { Value = endEpoch });

            BindInParameters(p, "@m", q.MachineIds);
            BindInParameters(p, "@p", q.ProductIds);
            BindInParameters(p, "@r", q.RecipeIds);

            if (q.Cursor is PanelCursor c)
            {
                p.Add(new SqlParameter("@cursorDate", SqlDbType.Int) { Value = c.LastPanelNumericDate });
                p.Add(new SqlParameter("@cursorId", SqlDbType.Int) { Value = c.LastPanelId });
            }
        }

        return (sb.ToString(), Bind);
    }

    private static void EnsureUnderInListCap(
        IReadOnlyCollection<int>? ids, string paramName)
    {
        if (ids is not null && ids.Count > MaxInListSize)
        {
            throw new ArgumentException(
                $"IN-list filter '{paramName}' has {ids.Count} values (cap is {MaxInListSize}). " +
                "Batch the filter or widen the range instead.", paramName);
        }
    }

    private static void AppendInClause(
        StringBuilder sb, string column, string paramPrefix,
        IReadOnlyCollection<int>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return;
        }
        sb.AppendLine().Append("  AND ").Append(column).Append(" IN (");
        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(paramPrefix).Append(i);
        }
        sb.Append(')');
    }

    private static void BindInParameters(
        SqlParameterCollection p, string paramPrefix,
        IReadOnlyCollection<int>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return;
        }
        var i = 0;
        foreach (var id in ids)
        {
            p.Add(new SqlParameter(paramPrefix + i, SqlDbType.Int) { Value = id });
            i++;
        }
    }

    private static PanelRow MapPanelRow(SqlDataReader r) => new(
        PanelId: r.GetInt32(0),
        MachineId: r.GetInt32(1),
        LaneNumber: r.GetInt32(2),
        PanelBarCode: r.GetString(3),
        PanelNumericDate: r.GetInt32(4),
        NbOfValidCards: r.GetInt32(5),
        TestTime: r.GetDouble(6),
        PanelStatus: r.GetInt32(7),
        AnomalyBr: r.GetInt32(8),
        AnomalyAr: r.GetInt32(9),
        HasBeenReviewed: r.GetByte(10) != 0,
        NbOfTestedObject: r.GetInt32(11),
        NbOfErrorObject: r.GetInt32(12),
        OperatorId: r.IsDBNull(13) ? null : r.GetInt32(13),
        ProductId: r.GetInt32(14),
        RecipeId: r.GetInt32(15));
}
