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

    public IReadOnlyList<Product> SeededProducts { get; init; } = [];

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
            if (query.RecipeIds is { Count: > 0 } && !query.RecipeIds.Contains(panel.RecipeId))
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

    public Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct)
        => Task.FromResult(SeededMachines);

    public Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct)
        => Task.FromResult(SeededProducts);

    public Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Recipe>>([]);
}
