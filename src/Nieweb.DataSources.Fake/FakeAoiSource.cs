using System.Runtime.CompilerServices;

namespace Nieweb.DataSources.Fake;

/// <summary>
/// Deterministic in-memory <see cref="IAoiSource"/> used by the
/// Playwright end-to-end harness so a smoke run can exercise the
/// panel-yield pipeline without touching a real Superviseur database.
///
/// Not registered in production hosts - see
/// <see cref="DependencyInjection.FakeAoiSourceServiceCollectionExtensions"/>
/// which only wires the source when
/// <c>Nieweb:Aoi:Fake:Enabled</c> is true.
/// </summary>
public sealed class FakeAoiSource : IAoiSource
{
    /// <summary>Fixed source id, referenced from the E2E spec URLs.</summary>
    public const string SourceId = "fake";

    private readonly IReadOnlyList<PanelRow> _panels;
    private readonly IReadOnlyList<Machine> _machines;
    private readonly IReadOnlyList<Product> _products;
    private readonly IReadOnlyList<Recipe> _recipes;

    /// <summary>
    /// Builds the singleton with the canonical E2E fixture: one machine,
    /// one product, one recipe, and ten panels on 2026-01-15 UTC (five
    /// clean, five with a single defect - FPY = 50%).
    /// </summary>
    public FakeAoiSource()
    {
        Descriptor = new SourceDescriptor(
            Id: SourceId,
            DisplayName: "Fake AOI (E2E fixture)",
            SchemaVersion: "5.0",
            Caps: Capabilities.IsLastInspectionFilter);

        _machines =
        [
            new Machine(MachineId: 1, MachineType: 100, MachineName: "AOI-E2E-1", MachineTypeName: "Vision3D CR4"),
        ];
        _products =
        [
            new Product(ProductId: 1, ProductName: "E2E Product", Revision: "A", Description: "Playwright fixture product"),
        ];
        _recipes =
        [
            new Recipe(
                RecipeId: 1,
                FileName: "E2E-Recipe",
                ProductId: 1,
                Author: "e2e",
                InspectedSideNb: 1,
                InspectedSideName: "Top",
                Customer: null,
                ProductionStep: null,
                VariantName: null),
        ];

        // 2026-01-15 08:00:00 UTC = 1768464000 seconds since epoch.
        // Panels are 15 min apart, so the last one lands at 10:15 UTC.
        const int baseEpoch = 1768464000;
        var panels = new List<PanelRow>(capacity: 10);
        for (var i = 0; i < 10; i++)
        {
            var isDefective = i >= 5;
            panels.Add(new PanelRow(
                PanelId: 100 + i,
                MachineId: 1,
                LaneNumber: 1,
                PanelBarCode: $"E2E-{i:D3}",
                PanelNumericDate: baseEpoch + (i * 900), // 15 min apart
                NbOfValidCards: 1,
                TestTime: 6.5,
                PanelStatus: isDefective ? 2 : 0,
                AnomalyBr: isDefective ? 1 : 0,
                AnomalyAr: 0,
                HasBeenReviewed: false,
                NbOfTestedObject: 42,
                NbOfErrorObject: isDefective ? 1 : 0,
                OperatorId: null,
                ProductId: 1,
                RecipeId: 1));
        }
        _panels = panels;
    }

    public SourceDescriptor Descriptor { get; }

    public Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct)
    {
        var latest = _panels
            .Select(p => (DateTime?)DateTimeOffset.FromUnixTimeSeconds(p.PanelNumericDate).UtcDateTime)
            .DefaultIfEmpty(null)
            .Max();
        return Task.FromResult(latest);
    }

    public Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(PanelQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = FilterPanels(query).ToList();
        return Task.FromResult(new Page<PanelRow, PanelCursor>(rows, NextCursor: null, HasMore: false));
    }

    public Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery query, CancellationToken ct)
        => Task.FromResult(new Page<CardRow, CardCursor>([], NextCursor: null, HasMore: false));

    public Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct)
        => Task.FromResult(new Page<TestedObjectRow, TestedObjectCursor>([], NextCursor: null, HasMore: false));

    public async IAsyncEnumerable<PanelRow> StreamPanelsAsync(
        PanelQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var panel in FilterPanels(query))
        {
            ct.ThrowIfCancellationRequested();
            yield return panel;
            await Task.Yield();
        }
    }

    public Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct)
        => Task.FromResult(_machines);

    public Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct)
        => Task.FromResult(_products);

    public Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct)
        => Task.FromResult(_recipes);

    private IEnumerable<PanelRow> FilterPanels(PanelQuery query)
    {
        foreach (var panel in _panels)
        {
            if (panel.PanelNumericDate < query.Window.StartEpochSeconds)
            {
                continue;
            }
            if (panel.PanelNumericDate >= query.Window.EndEpochSecondsExclusive)
            {
                continue;
            }
            if (query.MachineIds is { Count: > 0 } && !query.MachineIds.Contains(panel.MachineId))
            {
                continue;
            }
            if (query.ProductIds is { Count: > 0 } && !query.ProductIds.Contains(panel.ProductId))
            {
                continue;
            }
            if (query.RecipeIds is { Count: > 0 } && !query.RecipeIds.Contains(panel.RecipeId))
            {
                continue;
            }
            yield return panel;
        }
    }
}
