using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// Resolves the set of "NOGO" product ids for a source. NOGO boards are
/// known-defect calibration coupons run at changeover; every KPI report
/// that offers an <c>ExcludeNogo</c> toggle drops them from <b>both</b>
/// the numerator and the denominator so they don't skew production
/// numbers. Centralised so the marker string and the match rule stay
/// byte-identical across the Pareto / DPMO / FPY reports (KPI parity).
/// </summary>
internal static class NogoProducts
{
    /// <summary>Case-insensitive product-name marker.</summary>
    public const string Marker = "NOGO";

    /// <summary>
    /// Returns the ids of every product whose name contains
    /// <see cref="Marker"/> (case-insensitive), or <c>null</c> when
    /// <paramref name="exclude"/> is <c>false</c> — the fast path that
    /// reads no catalogue and adds no per-row check.
    /// </summary>
    public static async Task<HashSet<int>?> BuildAsync(
        IAoiSource source, bool exclude, CancellationToken cancellationToken)
    {
        if (!exclude)
        {
            return null;
        }

        var set = new HashSet<int>();
        foreach (var product in await source.ListProductsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (product.ProductName is not null
                && product.ProductName.Contains(Marker, StringComparison.OrdinalIgnoreCase))
            {
                set.Add(product.ProductId);
            }
        }
        return set;
    }
}
