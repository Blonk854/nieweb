using Microsoft.AspNetCore.Identity;

using Nieweb.Identity.Passwords;

using Xunit;

namespace Nieweb.Identity.Tests.Passwords;

/// <summary>
/// Unit tests for <see cref="Argon2idPasswordHasher{TUser}"/>. Uses very
/// small Argon2 parameters (m=8&#160;KiB, t=1, p=1) so each test hashes
/// in milliseconds; production defaults live in
/// <see cref="Argon2idOptions"/>.
/// </summary>
public sealed class Argon2idPasswordHasherTests
{
    // Any reference type works - IPasswordHasher<TUser> never touches the user.
    private sealed class DummyUser
    {
    }

    private static readonly Argon2idOptions _fastOptions = new()
    {
        MemoryKb = 8,
        Iterations = 1,
        DegreeOfParallelism = 1,
        SaltSize = 16,
        HashSize = 32,
    };

    private static Argon2idPasswordHasher<DummyUser> CreateHasher(Argon2idOptions? options = null)
        => new(new TestOptionsMonitor<Argon2idOptions>(options ?? _fastOptions));

    [Fact]
    public void HashPassword_ProducesPhcFormattedString()
    {
        var hasher = CreateHasher();

        var hash = hasher.HashPassword(new DummyUser(), "correct horse battery staple");

        Assert.StartsWith("$argon2id$v=19$m=8,t=1,p=1$", hash, StringComparison.Ordinal);
        // Six PHC segments: leading empty + argon2id + v + params + salt + hash.
        Assert.Equal(6, hash.Split('$').Length);
    }

    [Fact]
    public void HashPassword_EncodesConfiguredParameters()
    {
        var hasher = CreateHasher(new Argon2idOptions
        {
            MemoryKb = 32,
            Iterations = 2,
            DegreeOfParallelism = 2,
            SaltSize = 16,
            HashSize = 32,
        });

        var hash = hasher.HashPassword(new DummyUser(), "pw");

        Assert.StartsWith("$argon2id$v=19$m=32,t=2,p=2$", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void HashPassword_ProducesUniqueSaltForEachCall()
    {
        var hasher = CreateHasher();
        const string password = "pw";

        var a = hasher.HashPassword(new DummyUser(), password);
        var b = hasher.HashPassword(new DummyUser(), password);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashPassword_ThrowsArgumentNullException_ForNullPassword()
    {
        var hasher = CreateHasher();

        Assert.Throws<ArgumentNullException>(
            () => hasher.HashPassword(new DummyUser(), null!));
    }

    [Fact]
    public void VerifyHashedPassword_ReturnsSuccess_ForCorrectPassword()
    {
        var hasher = CreateHasher();
        const string password = "correct horse battery staple";
        var hash = hasher.HashPassword(new DummyUser(), password);

        var result = hasher.VerifyHashedPassword(new DummyUser(), hash, password);

        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public void VerifyHashedPassword_ReturnsFailed_ForIncorrectPassword()
    {
        var hasher = CreateHasher();
        var hash = hasher.HashPassword(new DummyUser(), "correct");

        var result = hasher.VerifyHashedPassword(new DummyUser(), hash, "wrong");

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-phc-string")]
    [InlineData("$argon2i$v=19$m=8,t=1,p=1$c2FsdA==$aGFzaA==")]   // wrong algorithm
    [InlineData("$argon2id$v=1$m=8,t=1,p=1$c2FsdA==$aGFzaA==")]    // wrong version
    [InlineData("$argon2id$v=19$m=8,t=1$c2FsdA==$aGFzaA==")]       // missing 'p' param
    [InlineData("$argon2id$v=19$m=8,t=1,p=1$!!!!$aGFzaA==")]       // bad base64 salt
    [InlineData("$argon2id$v=19$m=8,t=1,p=1$c2FsdA==$!!!!")]       // bad base64 hash
    public void VerifyHashedPassword_ReturnsFailed_ForMalformedHash(string bogus)
    {
        var hasher = CreateHasher();

        var result = hasher.VerifyHashedPassword(new DummyUser(), bogus, "pw");

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void VerifyHashedPassword_ThrowsArgumentNullException_ForNullHash()
    {
        var hasher = CreateHasher();

        Assert.Throws<ArgumentNullException>(
            () => hasher.VerifyHashedPassword(new DummyUser(), null!, "pw"));
    }

    [Fact]
    public void VerifyHashedPassword_ThrowsArgumentNullException_ForNullPassword()
    {
        var hasher = CreateHasher();
        var hash = hasher.HashPassword(new DummyUser(), "pw");

        Assert.Throws<ArgumentNullException>(
            () => hasher.VerifyHashedPassword(new DummyUser(), hash, null!));
    }

    [Theory]
    [InlineData(16, 1, 1, 16, 32)]  // memory  differs
    [InlineData(8, 2, 1, 16, 32)]   // iterations differ
    [InlineData(8, 1, 2, 16, 32)]   // parallelism differs
    [InlineData(8, 1, 1, 24, 32)]   // salt size differs
    [InlineData(8, 1, 1, 16, 48)]   // hash size differs
    public void VerifyHashedPassword_ReturnsRehashNeeded_WhenParametersChange(
        int newMemoryKb, int newIterations, int newParallelism, int newSaltSize, int newHashSize)
    {
        // Hash with the fast baseline options.
        var oldHasher = CreateHasher();
        const string password = "pw";
        var hash = oldHasher.HashPassword(new DummyUser(), password);

        // Verify with a hasher configured for a stronger / different cost.
        var newHasher = CreateHasher(new Argon2idOptions
        {
            MemoryKb = newMemoryKb,
            Iterations = newIterations,
            DegreeOfParallelism = newParallelism,
            SaltSize = newSaltSize,
            HashSize = newHashSize,
        });

        var result = newHasher.VerifyHashedPassword(new DummyUser(), hash, password);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void VerifyHashedPassword_ReturnsSuccess_WhenParametersMatch()
    {
        // Two independent hasher instances configured identically.
        var options = new Argon2idOptions
        {
            MemoryKb = 16,
            Iterations = 2,
            DegreeOfParallelism = 1,
            SaltSize = 16,
            HashSize = 32,
        };

        var writer = CreateHasher(options);
        var reader = CreateHasher(options);

        var hash = writer.HashPassword(new DummyUser(), "pw");
        var result = reader.VerifyHashedPassword(new DummyUser(), hash, "pw");

        Assert.Equal(PasswordVerificationResult.Success, result);
    }
}
