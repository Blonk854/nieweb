using Xunit;

namespace Nieweb.DataSources.Sql.Tests;

/// <summary>
/// SQL-shape tests for <c>BuildTestedObjectsQuery</c>. These assert on the
/// generated text rather than executing anything, so they run without a
/// SQL Server and without touching the live Superviseur DBs.
/// </summary>
public sealed class TestedObjectQuerySqlTests
{
    private static readonly DateRange _window = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Minimal concrete source that lets a test flip
    /// <c>HasTestedObjectErrorTableAr</c> without opening a connection.
    /// Nothing here talks to a network; only the query builder is exercised.
    /// </summary>
    private sealed class ProbeSource : SqlServerAoiSourceBase
    {
        private readonly bool _hasAr;
        private readonly Capabilities _caps;

        public ProbeSource(bool hasErrorTableAr, Capabilities caps = Capabilities.None)
            : base(new AoiSourceOptions
            {
                Server = "unused",
                Database = "unused",
                User = "unused",
                Password = "unused",
            })
        {
            _hasAr = hasErrorTableAr;
            _caps = caps;
        }

        public override SourceDescriptor Descriptor =>
            new("probe", "Probe", _hasAr ? "5.0" : "4.3.1", _caps);

        protected override string SourceTag => "probe";

        protected override bool HasTestedObjectErrorTableAr => _hasAr;

        // Catalogue lookups are irrelevant to query-shape tests; they would
        // need a live connection, so they are hard-failed rather than stubbed.
        public override Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public override Task<IReadOnlyList<ReviewOperator>> ListOperatorsAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public override Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public override Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public string BuildSql(TestedObjectQuery q) => BuildTestedObjectsQuery(q, 100).Sql;
    }

    [Fact]
    public void DefectsOnly_False_OmitsThePredicate()
    {
        var sql = new ProbeSource(hasErrorTableAr: true)
            .BuildSql(new TestedObjectQuery { Window = _window });

        Assert.DoesNotContain("Error_Table <> 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DefectsOnly_True_PostReflowShape_UsesErrorTableAr()
    {
        var sql = new ProbeSource(hasErrorTableAr: true)
            .BuildSql(new TestedObjectQuery { Window = _window, DefectsOnly = true });

        Assert.Contains("(t.Error_Table <> 0 OR t.Error_Table_AR <> 0)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DefectsOnly_True_PreReflowShape_DegradesToErrorTable()
    {
        // v4.3.1 TESTED_OBJECT has no Error_Table_AR column. The predicate
        // must degrade to the base column rather than emit SQL that only
        // parses against post-reflow.
        var sql = new ProbeSource(hasErrorTableAr: false)
            .BuildSql(new TestedObjectQuery { Window = _window, DefectsOnly = true });

        Assert.Contains("(t.Error_Table <> 0 OR t.Error_Table <> 0)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Error_Table_AR", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DefectsOnly_WithSkipInputsOnly_EmitsBothPredicates()
    {
        var sql = new ProbeSource(hasErrorTableAr: true).BuildSql(new TestedObjectQuery
        {
            Window = _window,
            DefectsOnly = true,
            SkipInputsOnly = true,
        });

        Assert.Contains("(t.Error_Table & 1) <> 0", sql, StringComparison.Ordinal);
        Assert.Contains("(t.Error_Table <> 0 OR t.Error_Table_AR <> 0)", sql, StringComparison.Ordinal);
    }
}
