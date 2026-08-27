using Nieweb.DataSources;

namespace Nieweb.Api.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IAoiSource"/> stub used by API tests that need
/// to exercise endpoints which enumerate registered sources without
/// touching a real SQL Server. Nothing here is production-safe - the
/// query methods throw <see cref="NotImplementedException"/> unless
/// a test explicitly opts in by supplying data.
/// </summary>
internal sealed class FakeAoiSource : IAoiSource, IPinLevelSource
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

    /// <summary>
    /// When set, <see cref="ListMachinesAsync"/> throws this instead of
    /// returning <see cref="SeededMachines"/>. Lets a test simulate an
    /// offline / mis-configured source and assert that multi-source endpoints
    /// omit it rather than failing the whole response.
    /// </summary>
    public Exception? ListMachinesThrows { get; init; }

    public IReadOnlyList<ReviewOperator> SeededOperators { get; init; } = [];

    public IReadOnlyList<Product> SeededProducts { get; init; } = [];

    public IReadOnlyList<Recipe> SeededRecipes { get; init; } = [];

    public IReadOnlyList<TestedObjectRow> SeededTestedObjects { get; init; } = [];

    /// <summary>Rows returned by <see cref="ListCardsForPanelAsync"/> (TC1 traceability).</summary>
    public IReadOnlyList<CardRow> SeededCards { get; init; } = [];

    /// <summary>Rows returned by <see cref="ListPinsForObjectAsync"/> (TC1 traceability).</summary>
    public IReadOnlyList<PinRow> SeededPins { get; init; } = [];

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
            yield return panel;
            await Task.Yield();
        }
    }

    public IAsyncEnumerable<CardRow> StreamCardsAsync(CardQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return FilterAsync(SeededCards, query, ct);

        static async IAsyncEnumerable<CardRow> FilterAsync(
            IReadOnlyList<CardRow> seed,
            CardQuery q,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken innerCt)
        {
            foreach (var card in seed)
            {
                innerCt.ThrowIfCancellationRequested();
                if (card.PanelNumericDate < q.Window.StartEpochSeconds)
                {
                    continue;
                }
                if (card.PanelNumericDate >= q.Window.EndEpochSecondsExclusive)
                {
                    continue;
                }
                if (q.MachineIds is { Count: > 0 } && !q.MachineIds.Contains(card.MachineId))
                {
                    continue;
                }
                if (q.ProductIds is { Count: > 0 } && !q.ProductIds.Contains(card.ProductId))
                {
                    continue;
                }
                yield return card;
                await Task.Yield();
            }
        }
    }

    public IAsyncEnumerable<TestedObjectRow> StreamTestedObjectsAsync(TestedObjectQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return FilterAsync(SeededTestedObjects, query, ct);

        static async IAsyncEnumerable<TestedObjectRow> FilterAsync(
            IReadOnlyList<TestedObjectRow> seed,
            TestedObjectQuery q,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken innerCt)
        {
            foreach (var obj in seed)
            {
                innerCt.ThrowIfCancellationRequested();
                if (obj.PanelNumericDate < q.Window.StartEpochSeconds)
                {
                    continue;
                }
                if (obj.PanelNumericDate >= q.Window.EndEpochSecondsExclusive)
                {
                    continue;
                }
                if (q.MachineIds is { Count: > 0 } && !q.MachineIds.Contains(obj.MachineId))
                {
                    continue;
                }
                if (q.ProductIds is { Count: > 0 } && !q.ProductIds.Contains(obj.ProductId))
                {
                    continue;
                }
                yield return obj;
                await Task.Yield();
            }
        }
    }

    public Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct)
        => ListMachinesThrows is not null
            ? Task.FromException<IReadOnlyList<Machine>>(ListMachinesThrows)
            : Task.FromResult(SeededMachines);

    public Task<IReadOnlyList<ReviewOperator>> ListOperatorsAsync(CancellationToken ct)
        => Task.FromResult(SeededOperators);

    public Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct)
        => Task.FromResult(SeededProducts);

    public Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct)
        => Task.FromResult(SeededRecipes);

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

    public Task<IReadOnlyList<PanelRow>> ListPanelsByBarcodeAsync(
        string barcode,
        CancellationToken ct)
        => ListPanelsByBarcodeAsync(barcode, limit: 1, ct);

    public Task<IReadOnlyList<PanelRow>> ListPanelsByBarcodeAsync(
        string barcode,
        int limit,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        var clamped = Math.Clamp(limit, 1, 100);
        var matched = SeededPanels
            .Where(p => string.Equals(p.PanelBarCode, barcode, StringComparison.Ordinal))
            .GroupBy(p => p.FaceNumber ?? 0)
            .OrderBy(g => g.Key)
            .SelectMany(g => g
                .OrderByDescending(p => p.PanelNumericDate)
                .ThenByDescending(p => p.PanelId)
                .Take(clamped))
            .ToList();
        return Task.FromResult<IReadOnlyList<PanelRow>>(matched);
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

    public Task<IReadOnlyList<PinRow>> ListPinsForObjectAsync(long testedObjectId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PinRow>>(
            SeededPins.Where(p => p.TestedObjectId == testedObjectId).ToList());
}
