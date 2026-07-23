using QuestPDF.Infrastructure;

namespace Nieweb.Pdf;

/// <summary>
/// One-time QuestPDF setup for Nieweb. QuestPDF requires an explicit
/// licence choice at first use (per its documentation) — we ship
/// under the Community MIT licence because Nieweb's revenue footprint
/// is well below the QuestPDF Community threshold (USD 1M annual gross
/// revenue).
/// </summary>
/// <remarks>
/// <see cref="EnsureLicenseActivated"/> is idempotent and thread-safe
/// (guarded by <see cref="Interlocked.CompareExchange(ref int, int, int)"/>);
/// hosts can call it eagerly from <c>Program.cs</c> or lazily from any
/// PDF render entry point without worrying about double-setup.
/// </remarks>
public static class NiewebPdfLicense
{
    private static int _activated;

    /// <summary>
    /// Activates the QuestPDF Community MIT licence exactly once per
    /// process. Safe to call from anywhere; subsequent calls are no-ops.
    /// </summary>
    public static void EnsureLicenseActivated()
    {
        if (Interlocked.CompareExchange(ref _activated, 1, 0) != 0)
        {
            return;
        }
        QuestPDF.Settings.License = LicenseType.Community;
    }
}
