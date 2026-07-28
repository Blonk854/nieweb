using System.Globalization;

using Nieweb.DataSources;
using Nieweb.Filters;
using Nieweb.Reports.Common.Defects;

namespace Nieweb.Reports.Filters;

/// <summary>
/// Adapts AOI Superviseur row types to <see cref="IFilterRowValues"/> so
/// the generic Vieweb-style operator filters (<see cref="FilterEvaluator"/>)
/// can narrow a report's streamed rows in memory. Only the fields that map
/// cleanly to a column (or a small reference lookup) are surfaced; every
/// other <see cref="FilterField"/> returns no tokens, which
/// <see cref="FilterValidator"/> and the per-tile UI already prevent from
/// being submitted.
/// </summary>
/// <remarks>
/// Scope (docs plan, confirmed 2026-07-27): Pareto (component/TESTED_OBJECT)
/// and Panel Yield (PANELS/CARDS) tiles. Product and AOI-machine filters are
/// resolved to their display names via the reference lists the report already
/// fetches; defect filters decode the object's post-review error bitfield to
/// defect display names.
/// </remarks>
public static class ReportFilterRows
{
    private static readonly string[] Empty = [];

    private static string[] Scalar(string? value)
        => value is null ? Empty : [value];

    private static string[] Scalar(int value)
        => [value.ToString(CultureInfo.InvariantCulture)];

    private static string? Resolve(IReadOnlyDictionary<int, string?>? names, int id)
        => names is not null && names.TryGetValue(id, out var name) ? name : null;

    /// <summary>
    /// Which <see cref="FilterField"/>s the TESTED_OBJECT adapter can honour.
    /// The UI restricts the field picker to this set for the Pareto tile.
    /// </summary>
    public static readonly IReadOnlySet<FilterField> TestedObjectFields =
        new HashSet<FilterField>
        {
            FilterField.ReferenceDesignator,
            FilterField.PartNumber,
            FilterField.Package,
            FilterField.Product,
            FilterField.AoiMachine,
            FilterField.Defect,
        };

    /// <summary>
    /// Which <see cref="FilterField"/>s the panel-granularity PANELS
    /// adapter (Panel Yield tile) can honour. Card-level fields
    /// (<see cref="FilterField.BoardNumber"/> / <see cref="FilterField.BoardStatus"/>)
    /// are intentionally excluded because Panel Yield folds panel rows,
    /// not sub-panel rows. The UI restricts the field picker to this set.
    /// </summary>
    public static readonly IReadOnlySet<FilterField> PanelFields =
        new HashSet<FilterField>
        {
            FilterField.PanelBarcode,
            FilterField.PanelStatus,
            FilterField.Product,
            FilterField.AoiMachine,
        };

    /// <summary>
    /// Builds an <see cref="IFilterRowValues"/> view over a
    /// <see cref="TestedObjectRow"/>. <paramref name="errorField"/> is the
    /// numerator-consistent defect bitfield the report already computed for
    /// the row, so a <see cref="FilterField.Defect"/> clause filters on the
    /// same defects the chart counts.
    /// </summary>
    public static IFilterRowValues ForTestedObject(
        TestedObjectRow row,
        long errorField,
        bool includeObsoleteBits,
        IReadOnlyDictionary<int, string?>? machineNames,
        IReadOnlyDictionary<int, string?>? productNames)
        => new TestedObjectFilterRow(row, errorField, includeObsoleteBits, machineNames, productNames);

    /// <summary>
    /// Builds an <see cref="IFilterRowValues"/> view over a
    /// <see cref="CardRow"/> (sub-panel) with its parent
    /// <see cref="PanelRow"/> for panel-level fields (bar code, status).
    /// </summary>
    public static IFilterRowValues ForCard(
        CardRow card,
        PanelRow? panel,
        IReadOnlyDictionary<int, string?>? machineNames,
        IReadOnlyDictionary<int, string?>? productNames)
        => new CardFilterRow(card, panel, machineNames, productNames);

    /// <summary>
    /// Builds an <see cref="IFilterRowValues"/> view over a
    /// <see cref="PanelRow"/> for panel-granularity reports.
    /// </summary>
    public static IFilterRowValues ForPanel(
        PanelRow panel,
        IReadOnlyDictionary<int, string?>? machineNames,
        IReadOnlyDictionary<int, string?>? productNames)
        => new PanelFilterRow(panel, machineNames, productNames);

    private sealed class TestedObjectFilterRow(
        TestedObjectRow row,
        long errorField,
        bool includeObsoleteBits,
        IReadOnlyDictionary<int, string?>? machineNames,
        IReadOnlyDictionary<int, string?>? productNames) : IFilterRowValues
    {
        public IReadOnlyCollection<string> GetValues(FilterField field) => field switch
        {
            FilterField.ReferenceDesignator => Scalar(row.Topology),
            FilterField.PartNumber => Scalar(row.PartNumberName),
            FilterField.Package => Scalar(row.JedecName),
            FilterField.Product => Scalar(Resolve(productNames, row.ProductId)),
            FilterField.AoiMachine => Scalar(Resolve(machineNames, row.MachineId)),
            FilterField.Defect => DefectTokens(),
            _ => Empty,
        };

        private string[] DefectTokens()
        {
            if (errorField == 0)
            {
                return Empty;
            }
            var tokens = new List<string>();
            foreach (var info in DefectBitDecoder.Decode(errorField))
            {
                if (!includeObsoleteBits && info.IsObsolete)
                {
                    continue;
                }
                tokens.Add(info.DisplayName);
            }
            return tokens.Count == 0 ? Empty : [.. tokens];
        }
    }

    private sealed class CardFilterRow(
        CardRow card,
        PanelRow? panel,
        IReadOnlyDictionary<int, string?>? machineNames,
        IReadOnlyDictionary<int, string?>? productNames) : IFilterRowValues
    {
        public IReadOnlyCollection<string> GetValues(FilterField field) => field switch
        {
            FilterField.PanelBarcode => Scalar(panel?.PanelBarCode),
            FilterField.BoardNumber => Scalar(card.CardIdOnPanel),
            FilterField.BoardStatus => Scalar(card.CardStatus),
            FilterField.PanelStatus => panel is null ? Empty : Scalar(panel.PanelStatus),
            FilterField.Product => Scalar(Resolve(productNames, card.ProductId)),
            FilterField.AoiMachine => Scalar(Resolve(machineNames, card.MachineId)),
            _ => Empty,
        };
    }

    private sealed class PanelFilterRow(
        PanelRow panel,
        IReadOnlyDictionary<int, string?>? machineNames,
        IReadOnlyDictionary<int, string?>? productNames) : IFilterRowValues
    {
        public IReadOnlyCollection<string> GetValues(FilterField field) => field switch
        {
            FilterField.PanelBarcode => Scalar(panel.PanelBarCode),
            FilterField.PanelStatus => Scalar(panel.PanelStatus),
            FilterField.Product => Scalar(Resolve(productNames, panel.ProductId)),
            FilterField.AoiMachine => Scalar(Resolve(machineNames, panel.MachineId)),
            _ => Empty,
        };
    }
}
