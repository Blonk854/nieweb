using Nieweb.DataSources;

namespace Nieweb.Api.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IAoiSource"/> stub used by API tests that need
/// to exercise endpoints which enumerate registered sources without
/// touching a real SQL Server. Nothing here is production-safe - the
/// query methods throw <see cref="NotImplementedException"/> unless
/// a test explicitly opts in by supplying data.
/// </summary>
internal sealed class FakeAoiSource : IAoiSource
{
    public FakeAoiSource(SourceDescriptor descriptor, DateTime? latestPanelUtc = null, Exception? latestPanelThrows = null)
    {
        Descriptor = descriptor;
        LatestPanelUtc = latestPanelUtc;
        LatestPanelThrows = latestPanelThrows;
    }

    public SourceDescriptor Descriptor { get; }

    public DateTime? LatestPanelUtc { get; }

    public Exception? LatestPanelThrows { get; }

    public IReadOnlyList<PanelRow> SeededPanels { get; init; } = [];

    public IReadOnlyList<Machine> SeededMachines { get; init; } = [];

    public IReadOnlyList<Product> SeededProducts { get; init; } = [];

    public IReadOnlyList<Recipe> SeededRecipes { get; init; } = [];

    public Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct)
    {
        if (LatestPanelThrows is not null)
        {
            throw LatestPanelThrows;
        }
        return Task.FromResult(LatestPanelUtc);
    }

    public Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(PanelQuery query, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery query, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct)
        => throw new NotImplementedException();

    public async IAsyncEnumerable<PanelRow> StreamPanelsAsync(
        PanelQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
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

    public IAsyncEnumerable<CardRow> StreamCardsAsync(CardQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return EmptyAsync(ct);

        static async IAsyncEnumerable<CardRow> EmptyAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken innerCt)
        {
            innerCt.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }

    public IAsyncEnumerable<TestedObjectRow> StreamTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return EmptyAsync(ct);

        static async IAsyncEnumerable<TestedObjectRow> EmptyAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken innerCt)
        {
            innerCt.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }

    public Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct)
        => Task.FromResult(SeededMachines);

    public Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct)
        => Task.FromResult(SeededProducts);

    public Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct)
        => Task.FromResult(SeededRecipes);
}
