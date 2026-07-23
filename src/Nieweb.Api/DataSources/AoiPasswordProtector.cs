using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Nieweb.Api.DataSources;

/// <inheritdoc cref="IAoiPasswordProtector"/>
public sealed class AoiPasswordProtector : IAoiPasswordProtector
{
    /// <summary>
    /// Purpose string bound to the underlying <see cref="IDataProtector"/>.
    /// Versioned so a future key-scheme change can decrypt legacy blobs
    /// via a dedicated protector and re-encrypt with the current one.
    /// </summary>
    public const string Purpose = "Nieweb.Aoi.SourcePassword.v1";

    private readonly IDataProtector _protector;

    public AoiPasswordProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc/>
    public byte[]? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        return _protector.Protect(bytes);
    }

    /// <inheritdoc/>
    public string? Unprotect(byte[]? ciphertext)
    {
        if (ciphertext is null || ciphertext.Length == 0)
        {
            return null;
        }
        var bytes = _protector.Unprotect(ciphertext);
        return Encoding.UTF8.GetString(bytes);
    }
}
