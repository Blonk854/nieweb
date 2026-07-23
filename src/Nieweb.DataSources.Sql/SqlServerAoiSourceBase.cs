using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
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
        Func<DbDataReader, T> map,
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
        Func<DbDataReader, T> map,
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

    /// <inheritdoc />
    /// <remarks>
    /// Shared implementation: joins <c>CARDS</c> to <c>PANELS</c>
    /// (for <c>Machine_Id</c> / <c>Product_Id</c> / <c>Panel_Numeric_Date</c>
    /// and for the mandatory <c>Panel_Numeric_Date</c> window filter),
    /// mirroring <see cref="QueryPanelsAsync"/> so panel-level and
    /// board-level reports over the same window agree on scope.
    /// Ordering matches <see cref="CardCursor"/>: (<c>Panel_Id</c>,
    /// <c>Card_Number</c>).
    /// </remarks>
    public virtual async Task<Page<CardRow, CardCursor>> QueryCardsAsync(
        CardQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateWindow(query);
        var pageSize = ValidatePageSize(query.PageSize);

        var (sql, bind) = BuildCardsQuery(query, pageSize + 1);

        var rows = new List<CardRow>(pageSize + 1);
        await foreach (var row in ExecuteQueryAsync(sql, bind, MapCardRow, ct).ConfigureAwait(false))
        {
            rows.Add(row);
        }

        var hasMore = rows.Count > pageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        CardCursor? next = hasMore && rows.Count > 0
            ? new CardCursor(
                LastPanelId: checked((int)rows[^1].PanelId),
                LastCardIdOnPanel: rows[^1].CardIdOnPanel)
            : null;

        return new Page<CardRow, CardCursor>(rows, next, hasMore);
    }

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

    /// <summary>
    /// True when <c>dbo.TESTED_OBJECT</c> exposes the
    /// <c>Error_Table_AR</c> column (post-review defect field). Set by
    /// v5.0 sources (HLYAOI); overridden to <c>false</c> on v4.3.1
    /// sources (MEAOI) where the column does not exist and
    /// <see cref="TestedObjectRow.ErrorTableAr"/> is populated from
    /// <c>Error_Table</c> to satisfy the "missing-AR means no review
    /// yet" contract documented on the DTO.
    /// </summary>
    protected virtual bool HasTestedObjectErrorTableAr => true;

    /// <inheritdoc />
    /// <remarks>
    /// Shared implementation: joins <c>TESTED_OBJECT</c> to <c>CARDS</c>
    /// (for <c>Card_Number</c>) and <c>PANELS</c> (for
    /// <c>Machine_Id</c> / <c>Product_Id</c> / <c>Panel_Numeric_Date</c>
    /// and for the mandatory <c>Panel_Numeric_Date</c> window filter),
    /// and left-joins <c>PART_NUMBER</c> + <c>JEDEC</c> for the
    /// reference-data labels. Ordering matches
    /// <see cref="TestedObjectCursor"/>:
    /// (<c>Panel_Id</c>, <c>Card_Number</c>, <c>Tested_Object_Id</c>).
    /// The window filter and the <c>IS_LAST_INSPECTION</c> guard both
    /// apply to <c>PANELS</c> — mirroring
    /// <see cref="QueryPanelsAsync"/> so the two reports agree on
    /// scope.
    /// </remarks>
    public virtual async Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(
        TestedObjectQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateWindow(query);
        var pageSize = ValidatePageSize(query.PageSize);

        var (sql, bind) = BuildTestedObjectsQuery(query, pageSize + 1);

        var rows = new List<TestedObjectRow>(pageSize + 1);
        await foreach (var row in ExecuteQueryAsync(sql, bind, MapTestedObjectRow, ct).ConfigureAwait(false))
        {
            rows.Add(row);
        }

        var hasMore = rows.Count > pageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        TestedObjectCursor? next = hasMore && rows.Count > 0
            ? new TestedObjectCursor(
                LastPanelId: checked((int)rows[^1].PanelId),
                LastCardIdOnPanel: rows[^1].CardIdOnPanel,
                LastObjectId: rows[^1].ObjectId)
            : null;

        return new Page<TestedObjectRow, TestedObjectCursor>(rows, next, hasMore);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation loops <see cref="QueryTestedObjectsAsync"/>
    /// keyset pages. Adapters that have a cheaper streaming path
    /// (e.g. sqlclient <c>ExecuteReader</c> against a joined query)
    /// override this.
    /// </remarks>
    public virtual async IAsyncEnumerable<TestedObjectRow> StreamTestedObjectsAsync(
        TestedObjectQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateWindow(query);
        _ = ValidatePageSize(query.PageSize);

        var current = query;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await QueryTestedObjectsAsync(current, ct).ConfigureAwait(false);
            foreach (var row in page.Rows)
            {
                yield return row;
            }
            if (!page.HasMore || page.NextCursor is not TestedObjectCursor next)
            {
                yield break;
            }
            current = current with { Cursor = next };
        }
    }

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

    // ---- Traceability drill-down (TC1) ------------------------------------
    // Single-panel / single-subpanel key lookups. No time window because a
    // specific Panel_Id (or a specific Panel_Bar_Code) is already narrow
    // enough for the DB engine to seek an index; adding a window here would
    // just push cycle-time cost onto the live line for no gain.

    /// <inheritdoc />
    public virtual async Task<PanelRow?> GetPanelByIdAsync(int panelId, CancellationToken ct)
    {
        const string Sql = """
            SELECT TOP (1)
              Panel_Id, Machine_Id, Lane_Number, Panel_Bar_Code, Panel_Numeric_Date,
              Nb_Of_Valid_Cards, Test_Time, Panel_Status, Anomaly_BR, Anomaly_AR,
              Has_Been_Reviewed, Nb_Of_Tested_Object, Nb_Of_Error_Object,
              Operator_Id, Product_Id, Recipe_Id
            FROM dbo.PANELS WITH (NOLOCK)
            WHERE Panel_Id = @panelId;
            """;

        PanelRow? found = null;
        await foreach (var row in ExecuteQueryAsync(
            Sql,
            bindParameters: p => p.Add(new SqlParameter("@panelId", SqlDbType.Int) { Value = panelId }),
            map: MapPanelRow,
            ct).ConfigureAwait(false))
        {
            found = row;
        }
        return found;
    }

    /// <inheritdoc />
    public virtual async Task<PanelRow?> GetPanelByBarcodeAsync(string barcode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        // Panel_Bar_Code is varchar(64). Reject longer inputs before
        // hitting the DB so a mis-scan can't blow up the query plan.
        if (barcode.Length > 64)
        {
            throw new ArgumentException(
                $"Panel barcode must be 64 characters or fewer (got {barcode.Length}).",
                nameof(barcode));
        }

        // A physical PCB can be re-inspected several times. Return the
        // most recent inspection so the drill-down entry point lands on
        // the latest state. Callers wanting the full inspection history
        // should build a windowed query on Panel_Bar_Code instead.
        const string Sql = """
            SELECT TOP (1)
              Panel_Id, Machine_Id, Lane_Number, Panel_Bar_Code, Panel_Numeric_Date,
              Nb_Of_Valid_Cards, Test_Time, Panel_Status, Anomaly_BR, Anomaly_AR,
              Has_Been_Reviewed, Nb_Of_Tested_Object, Nb_Of_Error_Object,
              Operator_Id, Product_Id, Recipe_Id
            FROM dbo.PANELS WITH (NOLOCK)
            WHERE Panel_Bar_Code = @barcode
            ORDER BY Panel_Numeric_Date DESC, Panel_Id DESC;
            """;

        PanelRow? found = null;
        await foreach (var row in ExecuteQueryAsync(
            Sql,
            bindParameters: p => p.Add(new SqlParameter("@barcode", SqlDbType.VarChar, 64) { Value = barcode }),
            map: MapPanelRow,
            ct).ConfigureAwait(false))
        {
            found = row;
        }
        return found;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<CardRow>> ListCardsForPanelAsync(long panelId, CancellationToken ct)
    {
        // Card_Id / Panel_Id are 32-bit on the wire; a bigint at the
        // caller layer is a widening convenience. Narrow (checked) so a
        // future 64-bit PANELS.Panel_Id would fail loudly rather than
        // silently truncating.
        var narrow = checked((int)panelId);

        const string Sql = """
            SELECT
              c.Panel_Id, c.Card_Number, c.Card_Status,
              c.Anomaly_BR, c.Anomaly_AR,
              c.Number_Of_Component, c.Number_Of_Anomaly,
              p.Machine_Id, p.Product_Id, p.Panel_Numeric_Date
            FROM dbo.CARDS  c WITH (NOLOCK)
            JOIN dbo.PANELS p WITH (NOLOCK) ON p.Panel_Id = c.Panel_Id
            WHERE c.Panel_Id = @panelId
            ORDER BY c.Card_Number;
            """;

        return await ExecuteListAsync(
            Sql,
            bindParameters: p => p.Add(new SqlParameter("@panelId", SqlDbType.Int) { Value = narrow }),
            map: MapCardRow,
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TestedObjectRow>> ListTestedObjectsForSubpanelAsync(
        long panelId, int cardIdOnPanel, CancellationToken ct)
    {
        var narrow = checked((int)panelId);

        // Same projection as BuildTestedObjectsQuery so MapTestedObjectRow
        // can be reused verbatim. Error_Table_AR is capability-gated
        // (v4.3.1 lacks the column; the mapper reads slot 5 uniformly).
        var arColumn = HasTestedObjectErrorTableAr
            ? "t.Error_Table_AR"
            : "t.Error_Table";

        var sql =
            $"""
            SELECT
              p.Panel_Id, c.Card_Number, t.Tested_Object_Id,
              t.Object_Type_Id, t.Error_Table, {arColumn},
              t.Topology, p.Machine_Id, p.Product_Id, p.Panel_Numeric_Date,
              pn.Part_Number, j.Jedec_Name,
              t.Delta_X, t.Delta_Y, t.Delta_Theta, t.Delta_Thickness, t.Delta_Surface,
              p.Face, p.Face_Number, f.Feeder_Machine,
              t.Repair_State_Result, t.Repair_Numeric_Date_Hour,
              t.Repair_Button_Comment, t.Repair_Error_Comment,
              t.Repair_Operator_Comments, t.Operator_Id
            FROM dbo.TESTED_OBJECT t WITH (NOLOCK)
            JOIN dbo.CARDS  c WITH (NOLOCK) ON c.Card_Id  = t.Card_Id
            JOIN dbo.PANELS p WITH (NOLOCK) ON p.Panel_Id = c.Panel_Id
            LEFT JOIN dbo.PART_NUMBER pn WITH (NOLOCK) ON pn.Part_Number_Id = t.Part_Number_Id
            LEFT JOIN dbo.JEDEC       j  WITH (NOLOCK) ON j.Jedec_Id       = pn.Jedec_Id
            LEFT JOIN dbo.FEEDER      f  WITH (NOLOCK) ON f.Feeder_Id      = t.Feeder_Id
            WHERE p.Panel_Id = @panelId
              AND c.Card_Number = @cardNumber
            ORDER BY t.Tested_Object_Id;
            """;

        return await ExecuteListAsync(
            sql,
            bindParameters: p =>
            {
                p.Add(new SqlParameter("@panelId", SqlDbType.Int) { Value = narrow });
                p.Add(new SqlParameter("@cardNumber", SqlDbType.Int) { Value = cardIdOnPanel });
            },
            map: MapTestedObjectRow,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// TC5 Phase C — single-round-trip override of
    /// <see cref="IAoiSource.ListFailedTestedObjectsForPanelAsync"/>.
    /// Fans across every sub-panel of the given panel and returns
    /// only rows where the post-review defect bitfield is non-zero,
    /// ordered by <c>Card_Number</c> then <c>Tested_Object_Id</c>.
    /// </summary>
    /// <remarks>
    /// Uses the same projection as
    /// <see cref="ListTestedObjectsForSubpanelAsync"/> so
    /// <see cref="MapTestedObjectRow"/> can be reused verbatim. The
    /// <c>Error_Table_AR</c> column is capability-gated (v4.3.1
    /// pre-reflow lacks the AR column, so the adapter substitutes
    /// <c>Error_Table</c> in both slots — the WHERE clause and the
    /// mapper both read from the same expression, so the filter
    /// stays consistent across schemas).
    /// </remarks>
    public virtual async Task<IReadOnlyList<TestedObjectRow>> ListFailedTestedObjectsForPanelAsync(
        long panelId, CancellationToken ct)
    {
        var narrow = checked((int)panelId);

        var arColumn = HasTestedObjectErrorTableAr
            ? "t.Error_Table_AR"
            : "t.Error_Table";

        var sql =
            $"""
            SELECT
              p.Panel_Id, c.Card_Number, t.Tested_Object_Id,
              t.Object_Type_Id, t.Error_Table, {arColumn},
              t.Topology, p.Machine_Id, p.Product_Id, p.Panel_Numeric_Date,
              pn.Part_Number, j.Jedec_Name,
              t.Delta_X, t.Delta_Y, t.Delta_Theta, t.Delta_Thickness, t.Delta_Surface,
              p.Face, p.Face_Number, f.Feeder_Machine,
              t.Repair_State_Result, t.Repair_Numeric_Date_Hour,
              t.Repair_Button_Comment, t.Repair_Error_Comment,
              t.Repair_Operator_Comments, t.Operator_Id
            FROM dbo.TESTED_OBJECT t WITH (NOLOCK)
            JOIN dbo.CARDS  c WITH (NOLOCK) ON c.Card_Id  = t.Card_Id
            JOIN dbo.PANELS p WITH (NOLOCK) ON p.Panel_Id = c.Panel_Id
            LEFT JOIN dbo.PART_NUMBER pn WITH (NOLOCK) ON pn.Part_Number_Id = t.Part_Number_Id
            LEFT JOIN dbo.JEDEC       j  WITH (NOLOCK) ON j.Jedec_Id       = pn.Jedec_Id
            LEFT JOIN dbo.FEEDER      f  WITH (NOLOCK) ON f.Feeder_Id      = t.Feeder_Id
            WHERE p.Panel_Id = @panelId
              AND {arColumn} <> 0
            ORDER BY c.Card_Number, t.Tested_Object_Id;
            """;

        return await ExecuteListAsync(
            sql,
            bindParameters: p => p.Add(new SqlParameter("@panelId", SqlDbType.Int) { Value = narrow }),
            map: MapTestedObjectRow,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Self-inspects the SQL Server session used by this source: returns
    /// the connection's <c>program_name</c> (should equal the
    /// <c>Application Name</c> tag baked in by the constructor) and its
    /// <c>transaction_isolation_level</c> (should be <c>1</c>
    /// / <c>ReadUncommitted</c> because
    /// <see cref="SqlGuards.IsolationPrelude"/> is prepended to every
    /// batch). Used by <c>tools/db-smoke</c> to prove the read-only
    /// discipline holds on the wire without needing
    /// <c>VIEW SERVER STATE</c> permission.
    /// </summary>
    /// <returns>
    /// Tuple of <c>(programName, transactionIsolationLevel, loginName, hostName)</c>.
    /// </returns>
    public virtual async Task<(string ProgramName, short TransactionIsolationLevel, string LoginName, string HostName)>
        GetSessionSelfDiagnosticsAsync(CancellationToken ct)
    {
        // Read-only DMV; goes through ExecuteQueryAsync so it inherits
        // the connection tag (SourceTag) and the READ UNCOMMITTED prelude.
        // Every login can see its own row in sys.dm_exec_sessions
        // without VIEW SERVER STATE.
        const string Sql = """
            SELECT program_name, transaction_isolation_level, login_name, host_name
            FROM sys.dm_exec_sessions WITH (NOLOCK)
            WHERE session_id = @@SPID;
            """;

        (string, short, string, string)? row = null;
        await foreach (var r in ExecuteQueryAsync(
            Sql,
            bindParameters: null,
            map: static r => (
                r.IsDBNull(0) ? string.Empty : r.GetString(0).TrimEnd(),
                r.GetInt16(1),
                r.IsDBNull(2) ? string.Empty : r.GetString(2).TrimEnd(),
                r.IsDBNull(3) ? string.Empty : r.GetString(3).TrimEnd()),
            ct).ConfigureAwait(false))
        {
            row = r;
        }

        return row ?? throw new InvalidOperationException(
            "sys.dm_exec_sessions returned no row for @@SPID — this cannot happen on a live connection.");
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

    internal static PanelRow MapPanelRow(DbDataReader r) => new(
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

    // ---- Shared TESTED_OBJECT query builder --------------------------------

    private (string Sql, Action<SqlParameterCollection> Bind) BuildTestedObjectsQuery(
        TestedObjectQuery q, int topCount)
    {
        EnsureUnderInListCap(q.MachineIds, nameof(q.MachineIds));
        EnsureUnderInListCap(q.ProductIds, nameof(q.ProductIds));

        var startEpoch = checked((int)q.Window.StartEpochSeconds);
        var endEpoch = checked((int)q.Window.EndEpochSecondsExclusive);

        // Error_Table_AR only exists on v5.0 sources. On v4.3.1 the DTO
        // contract is "mirror Error_Table into ErrorTableAr" — we do
        // that in MapTestedObjectRow by reading column 4 in both slots.
        var arColumn = HasTestedObjectErrorTableAr
            ? "t.Error_Table_AR"
            : "t.Error_Table";

        var sb = new StringBuilder(1024);
        sb.Append("SELECT TOP (@topCount)").AppendLine();
        sb.Append("  p.Panel_Id, c.Card_Number, t.Tested_Object_Id,").AppendLine();
        sb.Append("  t.Object_Type_Id, t.Error_Table, ").Append(arColumn).Append(',').AppendLine();
        sb.Append("  t.Topology, p.Machine_Id, p.Product_Id, p.Panel_Numeric_Date,").AppendLine();
        sb.Append("  pn.Part_Number, j.Jedec_Name,").AppendLine();
        // Delta_X / Delta_Y / Delta_Theta / Delta_Thickness / Delta_Surface
        // exist on both v5.0 (post-reflow) and v4.3.1 (pre-reflow)
        // schemas — verified against the live HLYAOI2024 and MEAOI DBs.
        // Feeds the CR2 Deviation chart. Adapters that lack a column
        // materialise null in that slot; the mapper honours NULL.
        sb.Append("  t.Delta_X, t.Delta_Y, t.Delta_Theta, t.Delta_Thickness, t.Delta_Surface,").AppendLine();
        // TC5 Phase B — panel face, feeder, and repair fields.
        // Face / Face_Number live on PANELS (they identify the panel
        // side the whole subpanel is on). Repair_* and Operator_Id
        // live on TESTED_OBJECT. Feeder_Machine is reached through
        // a LEFT JOIN so rows whose Feeder_Id has no matching FEEDER
        // (unheard of on the live DBs but defensively supported)
        // still project. All new columns exist verbatim on both v5.0
        // and v4.3.1 — verified against
        // tools/db/out/{postreflow,prereflow}/05_tested_object_columns.csv.
        sb.Append("  p.Face, p.Face_Number, f.Feeder_Machine,").AppendLine();
        sb.Append("  t.Repair_State_Result, t.Repair_Numeric_Date_Hour,").AppendLine();
        sb.Append("  t.Repair_Button_Comment, t.Repair_Error_Comment,").AppendLine();
        sb.Append("  t.Repair_Operator_Comments, t.Operator_Id").AppendLine();
        sb.Append(
            """
            FROM dbo.TESTED_OBJECT t WITH (NOLOCK)
            JOIN dbo.CARDS  c WITH (NOLOCK) ON c.Card_Id  = t.Card_Id
            JOIN dbo.PANELS p WITH (NOLOCK) ON p.Panel_Id = c.Panel_Id
            LEFT JOIN dbo.PART_NUMBER pn WITH (NOLOCK) ON pn.Part_Number_Id = t.Part_Number_Id
            LEFT JOIN dbo.JEDEC       j  WITH (NOLOCK) ON j.Jedec_Id       = pn.Jedec_Id
            LEFT JOIN dbo.FEEDER      f  WITH (NOLOCK) ON f.Feeder_Id      = t.Feeder_Id
            WHERE p.Panel_Numeric_Date >= @startEpoch
              AND p.Panel_Numeric_Date <  @endEpoch
            """);

        // Reuse the panel-level IS_LAST_INSPECTION guard so DPMO/FPY
        // over the same window agree on which panels are in scope.
        var useLastInsp =
            Descriptor.Caps.HasFlag(Capabilities.IsLastInspectionFilter);
        if (useLastInsp)
        {
            sb.AppendLine().Append("  AND p.IS_LAST_INSPECTION = 1");
        }

        AppendInClause(sb, "p.Machine_Id", "@m", q.MachineIds);
        AppendInClause(sb, "p.Product_Id", "@p", q.ProductIds);

        if (q.Cursor is not null)
        {
            // Keyset paging on (Panel_Id, Card_Number, Tested_Object_Id).
            // Tested_Object_Id is BIGINT on v5.0 so bind as @cursorObj BIGINT.
            sb.AppendLine().Append(
                """
                  AND (p.Panel_Id > @cursorPanel
                    OR (p.Panel_Id = @cursorPanel AND c.Card_Number > @cursorCard)
                    OR (p.Panel_Id = @cursorPanel AND c.Card_Number = @cursorCard AND t.Tested_Object_Id > @cursorObj))
                """);
        }

        sb.AppendLine().Append("ORDER BY p.Panel_Id, c.Card_Number, t.Tested_Object_Id;");

        void Bind(SqlParameterCollection p)
        {
            p.Add(new SqlParameter("@topCount", SqlDbType.Int) { Value = topCount });
            p.Add(new SqlParameter("@startEpoch", SqlDbType.Int) { Value = startEpoch });
            p.Add(new SqlParameter("@endEpoch", SqlDbType.Int) { Value = endEpoch });

            BindInParameters(p, "@m", q.MachineIds);
            BindInParameters(p, "@p", q.ProductIds);

            if (q.Cursor is TestedObjectCursor c)
            {
                p.Add(new SqlParameter("@cursorPanel", SqlDbType.Int) { Value = c.LastPanelId });
                p.Add(new SqlParameter("@cursorCard", SqlDbType.Int) { Value = c.LastCardIdOnPanel });
                p.Add(new SqlParameter("@cursorObj", SqlDbType.BigInt) { Value = (long)c.LastObjectId });
            }
        }

        return (sb.ToString(), Bind);
    }

    /// <summary>
    /// Map a row from <see cref="BuildTestedObjectsQuery"/>. Column
    /// order is fixed by the SELECT list; when the source lacks
    /// <c>Error_Table_AR</c> the builder repeats <c>Error_Table</c> in
    /// slot 5, so this mapper reads slot 5 uniformly.
    /// </summary>
    /// <remarks>
    /// Slot 2 (<c>Tested_Object_Id</c>) and slots 4/5 (<c>Error_Table</c>
    /// / <c>Error_Table_AR</c>) are polymorphic across the two shipped
    /// Superviseur schemas — verified against the live post-reflow
    /// (HLYAOI2024, v5.0) and pre-reflow (MEAOI, v4.3.1) DBs:
    /// <code>
    ///                        v5.0 (post)    v4.3.1 (pre)
    ///   Tested_Object_Id     bigint         int
    ///   Error_Table          int            int
    ///   Error_Table_AR       bigint         (column absent)
    /// </code>
    /// SqlDataReader's typed getters throw <see cref="InvalidCastException"/>
    /// on any mismatch, so we go through <see cref="Convert.ToInt64(object?, IFormatProvider?)"/>
    /// which widens both <see cref="int"/> and <see cref="long"/>.
    /// The tested-object id is narrowed (checked) to <see cref="int"/> to
    /// match the current cursor/DTO shape.
    /// </remarks>
    internal static TestedObjectRow MapTestedObjectRow(DbDataReader r)
    {
        var testedObjectId = checked((int)Convert.ToInt64(r.GetValue(2), CultureInfo.InvariantCulture));
        var errorTable = Convert.ToInt64(r.GetValue(4), CultureInfo.InvariantCulture);
        var errorTableAr = Convert.ToInt64(r.GetValue(5), CultureInfo.InvariantCulture);

        return new TestedObjectRow(
            PanelId: r.GetInt32(0),
            CardIdOnPanel: r.GetInt32(1),
            ObjectId: testedObjectId,
            ObjectTypeId: r.GetInt32(3),
            ErrorTable: errorTable,
            ErrorTableAr: errorTableAr,
            // Object-level Status is not a physical column on
            // TESTED_OBJECT — derive it from Error_Table_AR
            // (0 = OK, 1 = at least one post-review defect) so
            // report code that inspects Status behaves the same for
            // both schemas.
            Status: errorTableAr != 0 ? 1 : 0,
            MachineId: r.GetInt32(7),
            ProductId: r.GetInt32(8),
            PanelNumericDate: r.GetInt32(9),
            Topology: r.IsDBNull(6) ? null : r.GetString(6),
            PartNumberName: r.IsDBNull(10) ? null : r.GetString(10),
            JedecName: r.IsDBNull(11) ? null : r.GetString(11),
            // Deviations (slots 12..16) — SQL Server FLOAT projects
            // as System.Double; nullable in both schemas.
            DeltaXUm: ReadNullableDouble(r, 12),
            DeltaYUm: ReadNullableDouble(r, 13),
            DeltaThetaDeg: ReadNullableDouble(r, 14),
            DeltaThicknessUm: ReadNullableDouble(r, 15),
            DeltaSurface: ReadNullableDouble(r, 16),
            // TC5 Phase B — panel face, feeder, and repair fields.
            // PANELS.Face / Face_Number are NOT NULL on both DBs but
            // we IsDBNull-guard defensively for future schema drift.
            // FEEDER.Feeder_Machine is reached through LEFT JOIN, so
            // it CAN be null when the row's Feeder_Id has no match.
            // Repair_State_Result and Operator_Id are NOT NULL on
            // both DBs; Repair_Numeric_Date_Hour, Repair_*_Comment,
            // and Repair_Operator_Comments are NULL when the object
            // was never reviewed.
            Face: r.IsDBNull(17) ? null : r.GetString(17),
            FaceNumber: r.IsDBNull(18) ? null : r.GetInt32(18),
            FeederName: r.IsDBNull(19) ? null : r.GetString(19),
            RepairState: r.IsDBNull(20) ? null : r.GetInt32(20),
            RepairUtc: r.IsDBNull(21) ? null : r.GetInt32(21),
            RepairButtonComment: r.IsDBNull(22) ? null : r.GetString(22),
            RepairErrorComment: r.IsDBNull(23) ? null : r.GetString(23),
            RepairOperatorComment: r.IsDBNull(24) ? null : r.GetString(24),
            RepairOperatorId: r.IsDBNull(25) ? null : r.GetInt32(25));
    }

    private static double? ReadNullableDouble(DbDataReader r, int ordinal)
        => r.IsDBNull(ordinal) ? null : r.GetDouble(ordinal);

    // ---- Shared CARDS query builder ----------------------------------------

    private (string Sql, Action<SqlParameterCollection> Bind) BuildCardsQuery(
        CardQuery q, int topCount)
    {
        EnsureUnderInListCap(q.MachineIds, nameof(q.MachineIds));
        EnsureUnderInListCap(q.ProductIds, nameof(q.ProductIds));

        var startEpoch = checked((int)q.Window.StartEpochSeconds);
        var endEpoch = checked((int)q.Window.EndEpochSecondsExclusive);

        var sb = new StringBuilder(1024);
        sb.Append(
            """
            SELECT TOP (@topCount)
              c.Panel_Id, c.Card_Number, c.Card_Status,
              c.Anomaly_BR, c.Anomaly_AR,
              c.Number_Of_Component, c.Number_Of_Anomaly,
              p.Machine_Id, p.Product_Id, p.Panel_Numeric_Date
            FROM dbo.CARDS  c WITH (NOLOCK)
            JOIN dbo.PANELS p WITH (NOLOCK) ON p.Panel_Id = c.Panel_Id
            WHERE p.Panel_Numeric_Date >= @startEpoch
              AND p.Panel_Numeric_Date <  @endEpoch
            """);

        // Reuse the panel-level IS_LAST_INSPECTION guard so board-level
        // FPY / DPMO reports over the same window agree on scope with
        // panel-level ones. Capability-gated — v4.3.1 pre-reflow lacks
        // the column.
        var useLastInsp =
            Descriptor.Caps.HasFlag(Capabilities.IsLastInspectionFilter);
        if (useLastInsp)
        {
            sb.AppendLine().Append("  AND p.IS_LAST_INSPECTION = 1");
        }

        AppendInClause(sb, "p.Machine_Id", "@m", q.MachineIds);
        AppendInClause(sb, "p.Product_Id", "@p", q.ProductIds);

        if (q.Cursor is not null)
        {
            // Keyset paging on (Panel_Id, Card_Number). Both columns are
            // int NOT NULL on both DBs.
            sb.AppendLine().Append(
                """
                  AND (c.Panel_Id > @cursorPanel
                    OR (c.Panel_Id = @cursorPanel AND c.Card_Number > @cursorCard))
                """);
        }

        sb.AppendLine().Append("ORDER BY c.Panel_Id, c.Card_Number;");

        void Bind(SqlParameterCollection p)
        {
            p.Add(new SqlParameter("@topCount", SqlDbType.Int) { Value = topCount });
            p.Add(new SqlParameter("@startEpoch", SqlDbType.Int) { Value = startEpoch });
            p.Add(new SqlParameter("@endEpoch", SqlDbType.Int) { Value = endEpoch });

            BindInParameters(p, "@m", q.MachineIds);
            BindInParameters(p, "@p", q.ProductIds);

            if (q.Cursor is CardCursor c)
            {
                p.Add(new SqlParameter("@cursorPanel", SqlDbType.Int) { Value = c.LastPanelId });
                p.Add(new SqlParameter("@cursorCard", SqlDbType.Int) { Value = c.LastCardIdOnPanel });
            }
        }

        return (sb.ToString(), Bind);
    }

    /// <summary>
    /// Map a row from <see cref="BuildCardsQuery"/>. Column order is
    /// fixed by the SELECT list.
    /// </summary>
    /// <remarks>
    /// None of the columns projected here are type-polymorphic between
    /// the two live Superviseur DBs (verified against
    /// <c>tools/db/out/{postreflow,prereflow}/04_cards_columns.csv</c>):
    /// <list type="bullet">
    /// <item><c>Panel_Id</c>, <c>Card_Number</c>, <c>Card_Status</c>,
    ///   <c>Anomaly_BR</c>, <c>Anomaly_AR</c>,
    ///   <c>Number_Of_Component</c>, <c>Number_Of_Anomaly</c> are all
    ///   <c>int NOT NULL</c> on both DBs.</item>
    /// <item><c>Machine_Id</c>, <c>Product_Id</c>,
    ///   <c>Panel_Numeric_Date</c> come from <c>PANELS</c> and are all
    ///   <c>int NOT NULL</c> on both DBs.</item>
    /// </list>
    /// The only polymorphic CARDS column is <c>Card_Id</c>
    /// (<c>bigint</c> on v5.0, <c>int</c> on v4.3.1) — we do not project
    /// it because <see cref="CardRow.CardIdOnPanel"/> is the within-panel
    /// index (<c>Card_Number</c>), and the global <c>Card_Id</c> is only
    /// used in the JOIN. So typed getters are safe on every column.
    /// The <see cref="int"/>-to-<see cref="long"/> widening for
    /// <see cref="CardRow.PanelId"/> / <see cref="CardRow.AnomalyBr"/> /
    /// <see cref="CardRow.AnomalyAr"/> is an implicit conversion.
    /// </remarks>
    internal static CardRow MapCardRow(DbDataReader r) => new(
        PanelId: r.GetInt32(0),
        CardIdOnPanel: r.GetInt32(1),
        CardStatus: r.GetInt32(2),
        AnomalyBr: r.GetInt32(3),
        AnomalyAr: r.GetInt32(4),
        NbOfTestedObject: r.GetInt32(5),
        NbOfErrorObject: r.GetInt32(6),
        MachineId: r.GetInt32(7),
        ProductId: r.GetInt32(8),
        PanelNumericDate: r.GetInt32(9));
}
