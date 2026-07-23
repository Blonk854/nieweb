namespace Nieweb.Api.DataSources;

/// <summary>
/// Encrypts / decrypts AOI-source passwords at rest.
/// </summary>
/// <remarks>
/// Wraps an ASP.NET Core Data Protection <c>IDataProtector</c> keyed to
/// the purpose <c>"Nieweb.Aoi.SourcePassword.v1"</c>. Persisted
/// ciphertext lives in <see cref="Nieweb.Data.Entities.AoiSourceConfig.EncryptedPassword"/>
/// as raw bytes so it survives verbatim on both SQLite (BLOB) and
/// PostgreSQL (bytea).
/// </remarks>
public interface IAoiPasswordProtector
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> for at-rest storage. Returns
    /// <c>null</c> when <paramref name="plaintext"/> is <c>null</c> or
    /// empty (meaning "no password").
    /// </summary>
    byte[]? Protect(string? plaintext);

    /// <summary>
    /// Decrypts a stored ciphertext blob back to the original password.
    /// Returns <c>null</c> when <paramref name="ciphertext"/> is
    /// <c>null</c> or empty.
    /// </summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Thrown when the ciphertext cannot be decrypted (typically because
    /// the Data Protection keys were rotated or lost).
    /// </exception>
    string? Unprotect(byte[]? ciphertext);
}
