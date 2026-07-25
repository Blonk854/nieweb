using System.Runtime.CompilerServices;
using Nieweb.DataSources;

namespace Nieweb.Reports.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IAoiSource"/> stub for report unit tests.
/// Behaviourally a duplicate of the fake used in Nieweb.Api.Tests, but
/// kept local so the test projects do not need a shared helpers
/// package. Only <see cref="StreamPanelsAsync"/>,
/// <see cref="ListMachinesAsync"/>, and <see cref="Descriptor"/> are
/// meaningfully implemented - other members throw
/// <see cref="NotImplementedException"/>.
/// </summary>
internal sealed class FakeAoiSource : IAoiSource
{
    public FakeAoiSource(SourceDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public SourceDescriptor Descriptor { get; }

    public IReadOnlyList<PanelRow> SeededPanels { get; init; } = [];

    public IReadOnlyList<CardRow> SeededCards { get; init; } = [];

    public IReadOnlyList<Machine> SeededMachines { get; init; } = [];

    public IReadOnlyList<ReviewOperator> SeededOperators { get; init; } = [];

    public IReadOnlyList<Product> SeededProducts { get; init; } = [];

    /// <summary>
    /// Tested-object rows used by component-level report tests (DPMO
    /// table, Pareto chart). Rows are filtered on
    /// <c>TestedObjectQuery.Window</c> via
    /// <see cref="TestedObjectRow.PanelNumericDate"/> and on the
    /// standard <see cref="BaseQuery.MachineIds"/> /
    /// <see cref="BaseQuery.ProductIds"/> masks.
    /// </summary>
    public IReadOnlyList<TestedObjectRow> SeededTestedObjects { get; init; } = [];

    public Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct)
        => Task.FromResult<DateTime?>(null);

    public Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(PanelQuery query, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery query, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct)
        => throw new NotImplementedException();

    public async IAsyncEnumerable<PanelRow> StreamPanelsAsync(
        PanelQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var panel in SeededPanels)
        {
            ct.ThrowIfCancellationRequested();
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
            yield return panel;
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<CardRow> StreamCardsAsync(
        CardQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var card in SeededCards)
        {
            ct.ThrowIfCancellationRequested();
            if (card.PanelNumericDate < query.Window.StartEpochSeconds)
            {
                continue;
            }
            if (card.PanelNumericDate >= query.Window.EndEpochSecondsExclusive)
            {
                continue;
            }
            if (query.MachineIds is { Count: > 0 } && !query.MachineIds.Contains(card.MachineId))
            {
                continue;
            }
            if (query.ProductIds is { Count: > 0 } && !query.ProductIds.Contains(card.ProductId))
            {
                continue;
            }
            yield return card;
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<TestedObjectRow> StreamTestedObjectsAsync(
        TestedObjectQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var obj in SeededTestedObjects)
        {
            ct.ThrowIfCancellationRequested();
            if (obj.PanelNumericDate < query.Window.StartEpochSeconds)
            {
                continue;
            }
            if (obj.PanelNumericDate >= query.Window.EndEpochSecondsExclusive)
            {
                continue;
            }
            if (query.MachineIds is { Count: > 0 } && !query.MachineIds.Contains(obj.MachineId))
            {
                continue;
            }
            if (query.ProductIds is { Count: > 0 } && !query.ProductIds.Contains(obj.ProductId))
            {
                continue;
            }
            yield return obj;
            await Task.Yield();
        }
    }

    public Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct)
        => Task.FromResult(SeededMachines);

    public Task<IReadOnlyList<ReviewOperator>> ListOperatorsAsync(CancellationToken ct)
        => Task.FromResult(SeededOperators);

    public Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct)
        => Task.FromResult(SeededProducts);

    public Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Recipe>>([]);

    public Task<PanelRow?> GetPanelByIdAsync(int panelId, CancellationToken ct)
        => Task.FromResult<PanelRow?>(SeededPanels.FirstOrDefault(p => p.PanelId == panelId));

    public Task<PanelRow?> GetPanelByBarcodeAsync(string barcode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        var match = SeededPanels
            .Where(p => string.Equals(p.PanelBarCode, barcode, StringComparison.Ordinal))
            .OrderByDescending(p => p.PanelNumericDate)
            .ThenByDescending(p => p.PanelId)
            .FirstOrDefault();
        return Task.FromResult<PanelRow?>(match);
    }

    public Task<IReadOnlyList<CardRow>> ListCardsForPanelAsync(long panelId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CardRow>>(
            SeededCards.Where(c => c.PanelId == panelId).ToList());

    public Task<IReadOnlyList<TestedObjectRow>> ListTestedObjectsForSubpanelAsync(
        long panelId, int cardIdOnPanel, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<TestedObjectRow>>(
            SeededTestedObjects
                .Where(o => o.PanelId == panelId && o.CardIdOnPanel == cardIdOnPanel)
                .ToList());
}
