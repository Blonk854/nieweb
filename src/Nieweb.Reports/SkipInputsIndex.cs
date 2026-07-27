using Nieweb.DataSources;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Reports;

/// <summary>
/// Pre-streamed skip-classification inputs for a window: the parent
/// panel facts (review flag, panel status, machine / product) and the
/// per-card <c>TESTED_OBJECT</c> aggregates (missing count, manual-skip
/// flag) that <see cref="SkipClassifier"/> needs. A report builds this
/// once (streaming panels + tested objects) and then classifies each
/// <see cref="CardRow"/> during its own card stream, so cards are only
/// streamed once.
/// </summary>
/// <remarks>
/// Production <c>TESTED_OBJECT</c> is defect-only, so the tested-object
/// stream is small even over a wide window. All streams honour the same
/// window / machine / product scope and the source's IS_LAST_INSPECTION
/// de-duplication so the join is consistent.
/// </remarks>
internal sealed class SkipInputsIndex
{
    /// <summary>Parent-panel facts needed for classification and panel-level re-derivation.</summary>
    public readonly record struct PanelInfo(bool Reviewed, int PanelStatus, int MachineId, int ProductId);

    /// <summary><c>TESTED_OBJECT.Error_Table</c> bit 1 (value 1) = "Object missing".</summary>
    private const long ObjectMissingBit = 1L;

    private readonly Dictionary<long, PanelInfo> _panels;
    private readonly Dictionary<(long PanelId, int CardId), int> _missing;
    private readonly HashSet<(long PanelId, int CardId)> _manualSkip;

    private SkipInputsIndex(
        Dictionary<long, PanelInfo> panels,
        Dictionary<(long, int), int> missing,
        HashSet<(long, int)> manualSkip)
    {
        _panels = panels;
        _missing = missing;
        _manualSkip = manualSkip;
    }

    /// <summary>All panels in scope, keyed by <c>Panel_Id</c>.</summary>
    public IReadOnlyDictionary<long, PanelInfo> Panels => _panels;

    /// <summary>Classifies a card using the pre-streamed aggregates.</summary>
    public SkipClass Classify(CardRow card, SkipClassificationConfig config)
    {
        var key = (card.PanelId, card.CardIdOnPanel);
        _missing.TryGetValue(key, out var missing);
        var reviewed = _panels.TryGetValue(card.PanelId, out var p) && p.Reviewed;
        var inputs = new CardSkipInputs(
            // CardRow.NbOfTestedObject maps CARDS.Number_Of_Component.
            NumberOfComponent: card.NbOfTestedObject,
            CardStatus: card.CardStatus,
            AnomalyAr: card.AnomalyAr,
            MissingCount: missing,
            HasManualSkipButton: _manualSkip.Contains(key),
            HasBeenReviewed: reviewed);
        return SkipClassifier.Classify(inputs, config);
    }

    /// <summary>Streams panels + tested objects to build the index.</summary>
    public static async Task<SkipInputsIndex> BuildAsync(
        IAoiSource source,
        DateRange window,
        IReadOnlyCollection<int>? machineIds,
        IReadOnlyCollection<int>? productIds,
        bool onlyLastInspection,
        SkipClassificationConfig config,
        CancellationToken cancellationToken)
    {
        var panels = new Dictionary<long, PanelInfo>();
        var panelQuery = new PanelQuery
        {
            Window = window,
            MachineIds = machineIds,
            ProductIds = productIds,
            OnlyLastInspection = onlyLastInspection,
        };
        await foreach (var panel in source.StreamPanelsAsync(panelQuery, cancellationToken).ConfigureAwait(false))
        {
            panels[panel.PanelId] = new PanelInfo(
                panel.HasBeenReviewed, panel.PanelStatus, panel.MachineId, panel.ProductId);
        }

        var missing = new Dictionary<(long, int), int>();
        var manualSkip = new HashSet<(long, int)>();
        var objectQuery = new TestedObjectQuery
        {
            Window = window,
            MachineIds = machineIds,
            ProductIds = productIds,
        };
        await foreach (var obj in source.StreamTestedObjectsAsync(objectQuery, cancellationToken).ConfigureAwait(false))
        {
            var key = (obj.PanelId, obj.CardIdOnPanel);
            if ((obj.ErrorTable & ObjectMissingBit) != 0)
            {
                missing.TryGetValue(key, out var current);
                missing[key] = current + 1;
            }
            if (config.MeaningOf(obj.RepairButtonComment) == RepairButtonMeaning.ManualSkip)
            {
                manualSkip.Add(key);
            }
        }

        return new SkipInputsIndex(panels, missing, manualSkip);
    }
}
