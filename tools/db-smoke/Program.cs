using System.Diagnostics;
using Nieweb.DataSources;
using Nieweb.DataSources.Sql;
using Nieweb.Reports;

// -----------------------------------------------------------------------------
// Smoke test for the Nieweb data-source layer.
//
// Loads credentials from ../../.env, opens a connection to each requested
// source using its read-only guarded adapter, and exercises every layer that
// touches the live Superviseur DB in production:
//
//   1. Reference-data queries (MACHINE / PANELS / TESTED_OBJECT paging).
//   2. The SqlGuards forbidden-keyword regex (proves DELETE/UPDATE reject).
//   3. ParetoReport + DpmoTableReport over a small window (proves the report
//      layer works end-to-end against real rows, not just the fake source).
//   4. sys.dm_exec_sessions inspection (proves the ApplicationName tag
//      and READ UNCOMMITTED isolation prelude actually made it to the wire).
//
// Every phase is opt-out via --skip-* flags so a DBA can dial the load down.
//
// Usage (from repo root):
//   dotnet run --project tools/db-smoke -- postreflow
//   dotnet run --project tools/db-smoke -- prereflow
//   dotnet run --project tools/db-smoke              # both
//   dotnet run --project tools/db-smoke -- postreflow --window-minutes 5
//   dotnet run --project tools/db-smoke -- postreflow --skip-reports
// -----------------------------------------------------------------------------

var positional = new List<string>();
var windowMinutes = 15;
var skipReports = false;
var skipSessions = false;
for (var i = 0; i < args.Length; i++)
{
    var a = args[i];
    switch (a.ToLowerInvariant())
    {
        case "--window-minutes":
            if (i + 1 >= args.Length || !int.TryParse(args[++i], out windowMinutes) || windowMinutes <= 0)
            {
                Console.Error.WriteLine("--window-minutes requires a positive integer.");
                return 2;
            }
            break;
        case "--skip-reports":
            skipReports = true;
            break;
        case "--skip-sessions":
            skipSessions = true;
            break;
        case "--help" or "-h":
            Console.WriteLine("Usage: dotnet run --project tools/db-smoke -- [postreflow|prereflow]... [--window-minutes N] [--skip-reports] [--skip-sessions]");
            return 0;
        default:
            positional.Add(a.ToLowerInvariant());
            break;
    }
}
var wanted = positional.Count == 0
    ? new[] { "postreflow", "prereflow" }
    : positional.ToArray();

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var envPath = Path.Combine(repoRoot, ".env");
if (!File.Exists(envPath))
{
    Console.Error.WriteLine($"Missing {envPath}. Copy .env.example to .env and fill in the credentials.");
    return 1;
}

var env = LoadDotEnv(envPath);

var exitCode = 0;
foreach (var name in wanted)
{
    var prefix = name switch
    {
        "postreflow" => "AOI_POSTREFLOW_",
        "prereflow" => "AOI_PREREFLOW_",
        _ => null,
    };
    if (prefix is null)
    {
        Console.Error.WriteLine($"Unknown source '{name}'. Use 'postreflow' or 'prereflow'.");
        exitCode = 2;
        continue;
    }

    try
    {
        var options = BuildOptions(env, prefix);
        SqlServerAoiSourceBase source = name == "postreflow"
            ? new HlyaoiSource(options)
            : new MeaoiSource(options);

        Console.WriteLine();
        Console.WriteLine($"=== {source.Descriptor.DisplayName} (schema {source.Descriptor.SchemaVersion}) ===");
        Console.WriteLine($"    Caps: {source.Descriptor.Caps}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var machines = await source.ListMachinesAsync(cts.Token);
        Console.WriteLine($"    {machines.Count} machines returned.");
        foreach (var m in machines)
        {
            Console.WriteLine($"      [{m.MachineId,3}] {m.MachineName,-24} type={m.MachineType} ({m.MachineTypeName})");
        }

        // --- PANELS smoke: 60 days ending at the source's most recent panel.
        // Both sources are live now (post-reflow HLYAOI was renamed to
        // HLYAOI2024 in 2026-07 and points at the current production catalogue).
        var latest = await source.GetLatestPanelUtcAsync(cts.Token);
        if (latest is null)
        {
            Console.WriteLine();
            Console.WriteLine("    PANELS table is empty; skipping panel-query smoke.");
            continue;
        }
        var windowEnd = latest.Value.AddSeconds(1);   // inclusive of the latest row
        var windowStart = windowEnd - TimeSpan.FromDays(60);
        var panelQuery = new PanelQuery
        {
            Window = new DateRange(windowStart, windowEnd),
            PageSize = 10,
        };

        Console.WriteLine();
        Console.WriteLine($"    Latest panel: {latest:yyyy-MM-dd HH:mm:ss}Z");
        Console.WriteLine($"    Panels in [{windowStart:yyyy-MM-dd}, {windowEnd:yyyy-MM-dd}) (first {panelQuery.PageSize}):");
        var page = await source.QueryPanelsAsync(panelQuery, cts.Token);
        Console.WriteLine(
            $"    -> {page.Rows.Count} rows, HasMore={page.HasMore}, " +
            $"NextCursor={(page.NextCursor is PanelCursor c ? $"({c.LastPanelNumericDate}, {c.LastPanelId})" : "null")}.");
        foreach (var p in page.Rows.Take(5))
        {
            var ts = DateTimeOffset.FromUnixTimeSeconds(p.PanelNumericDate).UtcDateTime;
            Console.WriteLine(
                $"      [{p.PanelId,8}] {ts:yyyy-MM-dd HH:mm:ss}Z  M{p.MachineId,-2}  " +
                $"status={p.PanelStatus,2}  cards={p.NbOfValidCards,2}  " +
                $"barcode='{p.PanelBarCode}'");
        }

        // --- TESTED_OBJECT smoke: first page of a SMALL window ending at
        // the latest panel. We deliberately do NOT reuse the 60-day
        // panelQuery.Window here: pre-reflow (MEAOI, v4.3.1) lacks
        // IS_LAST_INSPECTION so the join scans every row in scope, and a
        // 60-day scan blows past the 30 s guard. windowMinutes (default 15)
        // is the same tiny slice the report smoke uses just below, so this
        // exercises the shared BuildTestedObjectsQuery join without
        // stressing either DB.
        var toWindowEnd = latest.Value.AddSeconds(1);
        var toWindowStart = toWindowEnd - TimeSpan.FromMinutes(windowMinutes);
        var toQuery = new TestedObjectQuery
        {
            Window = new DateRange(toWindowStart, toWindowEnd),
            PageSize = 5,
        };
        var toPage = await source.QueryTestedObjectsAsync(toQuery, cts.Token);
        Console.WriteLine();
        Console.WriteLine(
            $"    Tested objects in same window (first {toQuery.PageSize}): " +
            $"{toPage.Rows.Count} rows, HasMore={toPage.HasMore}.");
        foreach (var o in toPage.Rows.Take(5))
        {
            Console.WriteLine(
                $"      panel={o.PanelId,8} card={o.CardIdOnPanel,2} obj={o.ObjectId,8}  " +
                $"type=0x{o.ObjectTypeId:X}  errBR=0x{o.ErrorTable:X}  errAR=0x{o.ErrorTableAr:X}  " +
                $"topo='{o.Topology}'  pn='{o.PartNumberName}'  jedec='{o.JedecName}'");
        }

        // --- SqlGuards forbidden-keyword regex. Purely in-process assertion;
        // no wire traffic. Proves the guard would refuse a hand-typed write
        // if a future refactor accidentally routed one through the base.
        Console.WriteLine();
        Console.WriteLine("    SqlGuards.EnsureReadOnly rejection check:");
        foreach (var evilSql in new[]
        {
            "DELETE FROM dbo.PANELS",
            "UPDATE dbo.CARDS SET Card_Status = 0",
            "EXEC sp_who",
            "TRUNCATE TABLE dbo.TESTED_OBJECT",
        })
        {
            try
            {
                SqlGuards.EnsureReadOnly(evilSql);
                Console.Error.WriteLine($"      !!! {evilSql} was NOT rejected. GUARD BROKEN.");
                exitCode = 4;
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine($"      rejected: {evilSql}");
            }
        }

        // --- Report layer smoke: run ParetoReport + DpmoTableReport over a
        // narrow window (default 15 minutes) ending at the latest panel. This
        // exercises StreamTestedObjectsAsync + DefectBitDecoder + accumulator
        // wiring against real defect bit-fields, not just the fake source.
        if (!skipReports)
        {
            var reportEnd = latest.Value.AddSeconds(1);
            var reportStart = reportEnd - TimeSpan.FromMinutes(windowMinutes);
            var reportWindow = new DateRange(reportStart, reportEnd);
            Console.WriteLine();
            Console.WriteLine($"    Report smoke over [{reportStart:yyyy-MM-dd HH:mm:ss}, {reportEnd:yyyy-MM-dd HH:mm:ss}) UTC ({windowMinutes} min):");

            var paretoSw = Stopwatch.StartNew();
            var paretoResult = await ParetoReport.Instance.RunAsync(
                source,
                new ParetoFilter(
                    Window: reportWindow,
                    Axis: ParetoAxis.Defect,
                    Numerator: DpmoNumerator.Real,
                    Opportunity: DpmoOpportunity.All,
                    TopN: 5,
                    IncludeOthersBucket: true),
                cts.Token);
            paretoSw.Stop();
            Console.WriteLine(
                $"      Pareto(Defect): {paretoResult.Rows.Count} rows (+others={paretoResult.OthersBucket is not null}), " +
                $"total defects={paretoResult.Overall.DefectBitCount}, " +
                $"opps={paretoResult.Overall.OpportunityCount}, " +
                $"dpmo={paretoResult.Overall.DpmoPpm:N0} ppm, " +
                $"elapsed={paretoSw.ElapsedMilliseconds} ms");
            foreach (var r in paretoResult.Rows.Take(5))
            {
                Console.WriteLine(
                    $"        {r.GroupKey,-6} '{r.GroupName}' count={r.DefectCount,6} share={r.DefectSharePercent,5:F1}% cum={r.CumulativePercent,5:F1}% dpmo={r.DpmoPpm,10:N0} vital={(r.IsVitalFew ? "yes" : "no")}");
            }
            if (paretoResult.OthersBucket is { } others)
            {
                Console.WriteLine(
                    $"        Others  count={others.DefectCount,6} share={others.DefectSharePercent,5:F1}% cum={others.CumulativePercent,5:F1}% dpmo={others.DpmoPpm,10:N0}");
            }

            var dpmoSw = Stopwatch.StartNew();
            var dpmoResult = await DpmoTableReport.Instance.RunAsync(
                source,
                new DpmoTableFilter(
                    Window: reportWindow,
                    GroupBy: DpmoGroupBy.Defect,
                    Numerator: DpmoNumerator.Real,
                    Opportunity: DpmoOpportunity.All),
                cts.Token);
            dpmoSw.Stop();
            Console.WriteLine(
                $"      DPMO(Defect): {dpmoResult.Rows.Count} rows, " +
                $"total defects={dpmoResult.Overall.DefectBitCount}, " +
                $"opps={dpmoResult.Overall.OpportunityCount}, " +
                $"dpmo={dpmoResult.Overall.DpmoPpm:N0} ppm, " +
                $"elapsed={dpmoSw.ElapsedMilliseconds} ms");
            foreach (var r in dpmoResult.Rows.Take(5))
            {
                Console.WriteLine(
                    $"        {r.GroupKey,-6} '{r.GroupName}' defects={r.Kpi.DefectBitCount,6} opps={r.Kpi.OpportunityCount,7} dpmo={r.Kpi.DpmoPpm,10:N0} ppm");
            }

            // Pareto and DPMO totals over the same scope must agree exactly
            // (both aggregate the same DefectBitDecoder counts). If they
            // ever diverge, a report layer bug slipped through — fail loud.
            if (paretoResult.Overall.DefectBitCount != dpmoResult.Overall.DefectBitCount ||
                paretoResult.Overall.OpportunityCount != dpmoResult.Overall.OpportunityCount)
            {
                Console.Error.WriteLine(
                    $"      !!! Pareto vs DPMO totals disagree: " +
                    $"defects {paretoResult.Overall.DefectBitCount} vs {dpmoResult.Overall.DefectBitCount}, " +
                    $"opps {paretoResult.Overall.OpportunityCount} vs {dpmoResult.Overall.OpportunityCount}.");
                exitCode = 5;
            }
            else
            {
                Console.WriteLine("      Pareto and DPMO totals agree — count-first / divide-last parity holds.");
            }
        }

        // --- sys.dm_exec_sessions self-inspection. We call it through the
        // source's own ExecuteQueryAsync so the query rides one of the
        // pooled 'Nieweb-<tag>-<db>' connections and inherits the
        // READ UNCOMMITTED isolation prelude. @@SPID is always visible
        // to the connection's own login, so this works without needing
        // VIEW SERVER STATE permission on the service account.
        if (!skipSessions)
        {
            Console.WriteLine();
            Console.WriteLine("    sys.dm_exec_sessions self-inspection (via source connection):");
            var expectedProgram = $"Nieweb-{(name == "postreflow" ? "postreflow" : "prereflow")}-{options.Database}";
            try
            {
                var diag = await source.GetSessionSelfDiagnosticsAsync(cts.Token);
                Console.WriteLine(
                    $"      program='{diag.ProgramName}' login='{diag.LoginName}' host='{diag.HostName}' iso={diag.TransactionIsolationLevel}({IsoName(diag.TransactionIsolationLevel)})");
                var programOk = string.Equals(diag.ProgramName, expectedProgram, StringComparison.Ordinal);
                var isoOk = diag.TransactionIsolationLevel == 1;
                if (!programOk)
                {
                    Console.Error.WriteLine($"      !!! Expected program_name '{expectedProgram}' — ApplicationName tag broken.");
                    exitCode = 6;
                }
                if (!isoOk)
                {
                    Console.Error.WriteLine("      !!! transaction_isolation_level != 1 (ReadUncommitted) — isolation prelude did not apply.");
                    exitCode = 7;
                }
                if (programOk && isoOk)
                {
                    Console.WriteLine("      ApplicationName tag + READ UNCOMMITTED isolation prelude both confirmed on the wire.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"      !!! self-inspection failed: {ex.GetType().Name}: {ex.Message}");
                exitCode = 8;
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"!!! {name} failed: {ex.GetType().Name}: {ex.Message}");
        exitCode = 3;
    }
}

return exitCode;

// -----------------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------------

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }
    throw new InvalidOperationException($"Could not locate repo root from {start}.");
}

static Dictionary<string, string> LoadDotEnv(string path)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var raw in File.ReadAllLines(path))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var eq = line.IndexOf('=');
        if (eq <= 0)
        {
            continue;
        }

        var key = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1];
        }
        result[key] = value;
    }
    return result;
}

static AoiSourceOptions BuildOptions(Dictionary<string, string> env, string prefix)
{
    string Req(string key) =>
        env.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new InvalidOperationException($"Missing or empty {key} in .env");

    int OptInt(string key, int fallback) =>
        env.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;

    return new AoiSourceOptions
    {
        Server = Req(prefix + "SERVER"),
        Database = Req(prefix + "DATABASE"),
        User = Req(prefix + "USER"),
        Password = Req(prefix + "PASSWORD"),
        ConnectTimeoutSeconds = OptInt("AOI_CONNECT_TIMEOUT", 15),
        QueryTimeoutSeconds = OptInt("AOI_QUERY_TIMEOUT", 30),
    };
}

static string IsoName(short level) => level switch
{
    0 => "Unspecified",
    1 => "ReadUncommitted",
    2 => "ReadCommitted",
    3 => "Repeatable",
    4 => "Serializable",
    5 => "Snapshot",
    _ => "?",
};
