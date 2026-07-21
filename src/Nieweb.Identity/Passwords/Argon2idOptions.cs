namespace Nieweb.Identity.Passwords;

/// <summary>
/// Argon2id hashing parameters, bound from configuration section
/// <c>Nieweb:Identity:Argon2id</c> by
/// <see cref="Nieweb.Identity.DependencyInjection.IdentityServiceCollectionExtensions.AddNiewebIdentity"/>.
/// </summary>
/// <remarks>
/// Defaults follow OWASP's 2023 Password Storage Cheat Sheet for
/// Argon2id (m=64&#160;MiB, t=3, p=1). Increase <see cref="MemoryKb"/>
/// or <see cref="Iterations"/> on faster hardware; changing any parameter
/// causes the hasher to return
/// <see cref="Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded"/>
/// on the next successful sign-in so existing hashes migrate automatically.
/// </remarks>
public sealed class Argon2idOptions
{
    /// <summary>
    /// Memory cost in kibibytes (1 KiB = 1024 bytes). Default 65536 = 64 MiB.
    /// </summary>
    public int MemoryKb { get; set; } = 65536;

    /// <summary>
    /// Time cost (number of Argon2 iterations). Default 3.
    /// </summary>
    public int Iterations { get; set; } = 3;

    /// <summary>
    /// Number of parallel lanes. Default 1 (interactive login on a shared
    /// server; increase only if the hashing host is dedicated).
    /// </summary>
    public int DegreeOfParallelism { get; set; } = 1;

    /// <summary>
    /// Salt length in bytes. Default 16 (128 bits) per RFC 9106 §4.
    /// </summary>
    public int SaltSize { get; set; } = 16;

    /// <summary>
    /// Derived hash length in bytes. Default 32 (256 bits).
    /// </summary>
    public int HashSize { get; set; } = 32;
}
