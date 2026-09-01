using Nieweb.Api.Parameters;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Licensing;

/// <summary>
/// <see cref="ILicenseTokens"/> backed by <see cref="IAppParameters"/>
/// rows named <c>license.{token}.enabled</c>.
/// </summary>
/// <remarks>
/// Missing or malformed rows default to <c>true</c> to keep legacy hosts
/// operational during rollout; admins can explicitly disable a token by
/// setting the row to <c>false</c>.
/// </remarks>
public sealed class AppParameterLicenseTokens : ILicenseTokens
{
    private readonly IAppParameters _parameters;

    public AppParameterLicenseTokens(IAppParameters parameters)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    public async Task<bool> IsEnabledAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var key = BuildParameterKey(token);
        var row = await _parameters.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return true;
        }

        if (row.ValueType != AppParameterValueTypes.Bool)
        {
            return true;
        }

        try
        {
            return row.AsBool();
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    internal static string BuildParameterKey(string token)
        => "license." + token.Trim().ToLowerInvariant() + ".enabled";
}
