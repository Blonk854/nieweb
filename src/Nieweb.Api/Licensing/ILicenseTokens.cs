namespace Nieweb.Api.Licensing;

/// <summary>
/// Read-only access to module license-token switches.
/// </summary>
public interface ILicenseTokens
{
    /// <summary>
    /// Returns <c>true</c> when the given token is enabled for this host.
    /// </summary>
    Task<bool> IsEnabledAsync(string token, CancellationToken cancellationToken = default);
}
