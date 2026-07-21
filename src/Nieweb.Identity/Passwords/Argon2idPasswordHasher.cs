using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Konscious.Security.Cryptography;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Nieweb.Identity.Passwords;

/// <summary>
/// ASP.NET Core Identity <see cref="IPasswordHasher{TUser}"/> implementation
/// backed by Argon2id (Konscious.Security.Cryptography).
/// </summary>
/// <remarks>
/// <para>
/// Hashes are stored in PHC string format so the derivation parameters
/// travel with each hash and can evolve without breaking existing accounts:
/// </para>
/// <code>
/// $argon2id$v=19$m=&lt;memoryKb&gt;,t=&lt;iterations&gt;,p=&lt;parallelism&gt;$&lt;salt-b64&gt;$&lt;hash-b64&gt;
/// </code>
/// <para>
/// <see cref="VerifyHashedPassword"/> returns
/// <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> when the
/// stored parameters differ from the currently-configured
/// <see cref="Argon2idOptions"/>, prompting Identity to rehash on the next
/// successful sign-in so the account transparently upgrades to the new cost.
/// </para>
/// </remarks>
public sealed class Argon2idPasswordHasher<TUser> : IPasswordHasher<TUser>
    where TUser : class
{
    // Argon2 v1.3 (0x13 = 19 decimal); the PHC string encodes it in decimal.
    private const int Argon2Version = 19;

    private readonly IOptionsMonitor<Argon2idOptions> _options;

    public Argon2idPasswordHasher(IOptionsMonitor<Argon2idOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public string HashPassword(TUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var o = _options.CurrentValue;

        var salt = RandomNumberGenerator.GetBytes(o.SaltSize);
        var hash = DeriveHash(
            password, salt, o.MemoryKb, o.Iterations, o.DegreeOfParallelism, o.HashSize);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"$argon2id$v={Argon2Version}$m={o.MemoryKb},t={o.Iterations},p={o.DegreeOfParallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    public PasswordVerificationResult VerifyHashedPassword(
        TUser user, string hashedPassword, string providedPassword)
    {
        ArgumentNullException.ThrowIfNull(hashedPassword);
        ArgumentNullException.ThrowIfNull(providedPassword);

        if (!TryParse(hashedPassword, out var parsed))
        {
            return PasswordVerificationResult.Failed;
        }

        var recomputed = DeriveHash(
            providedPassword, parsed.Salt,
            parsed.MemoryKb, parsed.Iterations, parsed.DegreeOfParallelism,
            parsed.Hash.Length);

        if (!CryptographicOperations.FixedTimeEquals(recomputed, parsed.Hash))
        {
            return PasswordVerificationResult.Failed;
        }

        var o = _options.CurrentValue;
        var needsRehash =
            parsed.MemoryKb != o.MemoryKb ||
            parsed.Iterations != o.Iterations ||
            parsed.DegreeOfParallelism != o.DegreeOfParallelism ||
            parsed.Hash.Length != o.HashSize ||
            parsed.Salt.Length != o.SaltSize;

        return needsRehash
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    private static byte[] DeriveHash(
        string password, byte[] salt,
        int memoryKb, int iterations, int parallelism, int hashSize)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(hashSize);
    }

    private static bool TryParse(string encoded, out ParsedHash parsed)
    {
        parsed = default;

        // Expected: $argon2id$v=19$m=NNN,t=NN,p=NN$saltB64$hashB64
        // Split on '$' yields 6 parts (leading empty + 5 fields).
        var parts = encoded.Split('$');
        if (parts.Length != 6)
        {
            return false;
        }

        if (parts[0].Length != 0)
        {
            return false;
        }

        if (!string.Equals(parts[1], "argon2id", StringComparison.Ordinal))
        {
            return false;
        }

        if (!parts[2].StartsWith("v=", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(
                parts[2].AsSpan(2),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var version)
            || version != Argon2Version)
        {
            return false;
        }

        var paramParts = parts[3].Split(',');
        if (paramParts.Length != 3)
        {
            return false;
        }

        if (!TryParseIntSegment(paramParts[0], "m=", out var m)
            || !TryParseIntSegment(paramParts[1], "t=", out var t)
            || !TryParseIntSegment(paramParts[2], "p=", out var p))
        {
            return false;
        }

        byte[] salt;
        byte[] hash;
        try
        {
            salt = Convert.FromBase64String(parts[4]);
            hash = Convert.FromBase64String(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        parsed = new ParsedHash
        {
            MemoryKb = m,
            Iterations = t,
            DegreeOfParallelism = p,
            Salt = salt,
            Hash = hash,
        };
        return true;
    }

    private static bool TryParseIntSegment(string segment, string prefix, out int value)
    {
        value = 0;
        if (!segment.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(
            segment.AsSpan(prefix.Length),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private struct ParsedHash
    {
        public int MemoryKb;
        public int Iterations;
        public int DegreeOfParallelism;
        public byte[] Salt;
        public byte[] Hash;
    }
}
