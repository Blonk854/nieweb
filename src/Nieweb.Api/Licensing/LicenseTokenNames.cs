namespace Nieweb.Api.Licensing;

/// <summary>
/// Canonical license-token slugs used by Nieweb feature gates.
/// Persisted in AppParameters as <c>license.{token}.enabled</c>.
/// </summary>
public static class LicenseTokenNames
{
    /// <summary>AOI Analyse dashboards and routes.</summary>
    public const string Analyse = "analyse";
}
