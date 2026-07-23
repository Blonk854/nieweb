using System.Data.Common;
using System.Text;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Nieweb.Api.DataSources;
using Nieweb.Data;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests.DataSources;

/// <summary>
/// Unit tests for <see cref="EfAoiSourceConfigs"/>. Uses an in-memory
/// SQLite database with the real <see cref="NiewebDbContext"/> model
/// (so migrations / column shapes are honoured) plus the real
/// <see cref="AoiPasswordProtector"/> wrapping an
/// <see cref="EphemeralDataProtectionProvider"/>. This proves the
/// end-to-end round-trip (plaintext → encrypt → BLOB → decrypt →
/// plaintext) without booting the full API host.
/// </summary>
public sealed class EfAoiSourceConfigsTests : IDisposable
{
    private readonly DbConnection _connection;
    private readonly NiewebDbContext _db;
    private readonly AoiPasswordProtector _protector;
    private readonly FixedTimeProvider _time;
    private readonly EfAoiSourceConfigs _sut;

    public EfAoiSourceConfigsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<NiewebDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new NiewebDbContext(options);
        _db.Database.EnsureCreated();

        _protector = new AoiPasswordProtector(new EphemeralDataProtectionProvider());
        _time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
        _sut = new EfAoiSourceConfigs(_db, _protector, _time);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------
    // Password round-trip
    // ---------------------------------------------------------------

    [Fact]
    public async Task UpsertAsync_NewRow_EncryptsPassword_AndReturnsHasPasswordTrue()
    {
        var view = await _sut.UpsertAsync(Spec("postreflow", password: "s3cr3t"));

        Assert.True(view.HasPassword);
        // The persisted blob must not be the raw plaintext.
        var row = await _db.AoiSourceConfigs.AsNoTracking().SingleAsync(c => c.Key == "postreflow");
        Assert.NotNull(row.EncryptedPassword);
        Assert.NotEmpty(row.EncryptedPassword!);
        Assert.NotEqual(Encoding.UTF8.GetBytes("s3cr3t"), row.EncryptedPassword);
        // Round-trip through the protector recovers the original.
        Assert.Equal("s3cr3t", _protector.Unprotect(row.EncryptedPassword));
    }

    [Fact]
    public async Task UpsertAsync_UpdateWithBlankPassword_PreservesExistingBlob()
    {
        // Create with a password, capture the encrypted bytes.
        await _sut.UpsertAsync(Spec("postreflow", password: "original"));
        var original = await LoadPasswordAsync("postreflow");
        Assert.NotNull(original);

        // Update with an empty password - should NOT clear the blob.
        await _sut.UpsertAsync(Spec("postreflow", password: "", displayName: "Renamed"));

        var afterEmpty = await LoadPasswordAsync("postreflow");
        Assert.Equal(original, afterEmpty);
        var view = await _sut.GetAsync("postreflow");
        Assert.NotNull(view);
        Assert.Equal("Renamed", view!.DisplayName);
        Assert.True(view.HasPassword);

        // Same with a null password.
        await _sut.UpsertAsync(Spec("postreflow", password: null, displayName: "Renamed again"));
        var afterNull = await LoadPasswordAsync("postreflow");
        Assert.Equal(original, afterNull);
    }

    [Fact]
    public async Task UpsertAsync_UpdateWithNewPassword_ReplacesEncryptedBlob()
    {
        await _sut.UpsertAsync(Spec("postreflow", password: "original"));
        var original = await LoadPasswordAsync("postreflow");

        await _sut.UpsertAsync(Spec("postreflow", password: "rotated"));
        var rotated = await LoadPasswordAsync("postreflow");

        Assert.NotNull(rotated);
        Assert.NotEqual(original, rotated);
        Assert.Equal("rotated", _protector.Unprotect(rotated));
    }

    [Fact]
    public async Task UpsertAsync_NewRow_WithoutPassword_LeavesEncryptedBlobNull()
    {
        var view = await _sut.UpsertAsync(Spec("fake", kind: AoiSourceKinds.Fake, password: null));

        Assert.False(view.HasPassword);
        var row = await _db.AoiSourceConfigs.AsNoTracking().SingleAsync(c => c.Key == "fake");
        Assert.Null(row.EncryptedPassword);
    }

    // ---------------------------------------------------------------
    // Timestamps
    // ---------------------------------------------------------------

    [Fact]
    public async Task UpsertAsync_NewRow_StampsCreatedAndLastModifiedFromTimeProvider()
    {
        var t0 = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
        _time.Now = new DateTimeOffset(t0, TimeSpan.Zero);

        var view = await _sut.UpsertAsync(Spec("postreflow", password: "x"));

        Assert.Equal(t0, view.CreatedUtc);
        Assert.Equal(t0, view.LastModifiedUtc);
    }

    [Fact]
    public async Task UpsertAsync_Update_KeepsCreatedUtc_AdvancesLastModified()
    {
        var t0 = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
        _time.Now = new DateTimeOffset(t0, TimeSpan.Zero);
        await _sut.UpsertAsync(Spec("postreflow", password: "x"));

        var t1 = t0.AddMinutes(5);
        _time.Now = new DateTimeOffset(t1, TimeSpan.Zero);
        var view = await _sut.UpsertAsync(Spec("postreflow", password: "", displayName: "Edited"));

        Assert.Equal(t0, view.CreatedUtc);
        Assert.Equal(t1, view.LastModifiedUtc);
    }

    // ---------------------------------------------------------------
    // Trimming / normalization
    // ---------------------------------------------------------------

    [Fact]
    public async Task UpsertAsync_TrimsStringFields_AndCollapsesWhitespaceOptionalsToNull()
    {
        var view = await _sut.UpsertAsync(new AoiSourceConfigSpec(
            Key: "  postreflow  ",
            DisplayName: "  Post-reflow AOI  ",
            Kind: "SqlServer",
            Server: "  HLYMSSQL2  ",
            Database: "  HLYAOI2024  ",
            User: "  svc_hlyaoiprod  ",
            Password: "x",
            ConnectTimeoutSeconds: 15,
            QueryTimeoutSeconds: 30,
            TrustServerCertificate: true,
            Encrypt: false,
            IsEnabled: true));

        Assert.Equal("postreflow", view.Key);
        Assert.Equal("Post-reflow AOI", view.DisplayName);
        Assert.Equal("HLYMSSQL2", view.Server);
        Assert.Equal("HLYAOI2024", view.Database);
        Assert.Equal("svc_hlyaoiprod", view.User);

        // Whitespace-only optional fields collapse to null on the round-trip.
        var view2 = await _sut.UpsertAsync(new AoiSourceConfigSpec(
            Key: "fake",
            DisplayName: "Fake",
            Kind: AoiSourceKinds.Fake,
            Server: "   ",
            Database: "   ",
            User: "   ",
            Password: null,
            ConnectTimeoutSeconds: 15,
            QueryTimeoutSeconds: 30,
            TrustServerCertificate: true,
            Encrypt: false,
            IsEnabled: false));
        Assert.Null(view2.Server);
        Assert.Null(view2.Database);
        Assert.Null(view2.User);
        Assert.False(view2.IsEnabled);
    }

    // ---------------------------------------------------------------
    // Delete
    // ---------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_ExistingRow_RemovesRow_AndReturnsTrue()
    {
        await _sut.UpsertAsync(Spec("postreflow", password: "x"));

        var deleted = await _sut.DeleteAsync("postreflow");

        Assert.True(deleted);
        Assert.False(await _db.AoiSourceConfigs.AnyAsync(c => c.Key == "postreflow"));
        Assert.Null(await _sut.GetAsync("postreflow"));
    }

    [Fact]
    public async Task DeleteAsync_MissingRow_ReturnsFalse()
    {
        var deleted = await _sut.DeleteAsync("does-not-exist");
        Assert.False(deleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteAsync_NullOrWhitespaceKey_Throws(string? key)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace raises ArgumentNullException
        // for null and ArgumentException for whitespace - both derive from
        // ArgumentException, so accept any subtype.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _sut.DeleteAsync(key!));
    }

    // ---------------------------------------------------------------
    // List / Get
    // ---------------------------------------------------------------

    [Fact]
    public async Task ListAsync_OrdersByKey_AndOmitsPassword()
    {
        await _sut.UpsertAsync(Spec("prereflow", password: "a"));
        await _sut.UpsertAsync(Spec("fake", kind: AoiSourceKinds.Fake, password: null));
        await _sut.UpsertAsync(Spec("postreflow", password: "b"));

        var list = await _sut.ListAsync();

        Assert.Collection(list,
            v => Assert.Equal("fake", v.Key),
            v => Assert.Equal("postreflow", v.Key),
            v => Assert.Equal("prereflow", v.Key));
        // The DTO shape carries HasPassword but no plaintext field at all,
        // so it is structurally impossible to leak a password through
        // ListAsync. Sanity-check HasPassword follows the row.
        Assert.False(list[0].HasPassword);
        Assert.True(list[1].HasPassword);
        Assert.True(list[2].HasPassword);
    }

    [Fact]
    public async Task GetAsync_UnknownKey_ReturnsNull()
    {
        Assert.Null(await _sut.GetAsync("nope"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAsync_NullOrWhitespaceKey_Throws(string? key)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _sut.GetAsync(key!));
    }

    // ---------------------------------------------------------------
    // Validation
    // ---------------------------------------------------------------

    [Fact]
    public async Task UpsertAsync_MissingServer_OnSqlServerKind_Throws()
    {
        var spec = Spec("postreflow", password: "x") with { Server = "  " };
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpsertAsync(spec));
    }

    [Fact]
    public async Task UpsertAsync_UnknownKind_Throws()
    {
        var spec = Spec("postreflow", password: "x") with { Kind = "Oracle" };
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpsertAsync(spec));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public async Task UpsertAsync_ConnectTimeoutOutOfRange_Throws(int seconds)
    {
        var spec = Spec("postreflow", password: "x") with { ConnectTimeoutSeconds = seconds };
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpsertAsync(spec));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public async Task UpsertAsync_QueryTimeoutOutOfRange_Throws(int seconds)
    {
        var spec = Spec("postreflow", password: "x") with { QueryTimeoutSeconds = seconds };
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpsertAsync(spec));
    }

    [Fact]
    public async Task UpsertAsync_FakeKind_DoesNotRequireServerDatabaseUser()
    {
        var view = await _sut.UpsertAsync(new AoiSourceConfigSpec(
            Key: "fake",
            DisplayName: "Fake",
            Kind: AoiSourceKinds.Fake,
            Server: null,
            Database: null,
            User: null,
            Password: null,
            ConnectTimeoutSeconds: 15,
            QueryTimeoutSeconds: 30,
            TrustServerCertificate: true,
            Encrypt: false,
            IsEnabled: true));

        Assert.Equal("fake", view.Key);
        Assert.Equal(AoiSourceKinds.Fake, view.Kind);
        Assert.False(view.HasPassword);
    }

    // ---------------------------------------------------------------
    // Test (Fake kind path - the SqlServer path opens a real socket
    // and is exercised via integration tests instead).
    // ---------------------------------------------------------------

    [Fact]
    public async Task TestAsync_FakeKind_ReturnsOk_AndStampsLastTestedUtc()
    {
        var t0 = new DateTime(2026, 7, 23, 12, 34, 56, DateTimeKind.Utc);
        _time.Now = new DateTimeOffset(t0, TimeSpan.Zero);
        await _sut.UpsertAsync(Spec("fake", kind: AoiSourceKinds.Fake, password: null));

        var t1 = t0.AddMinutes(1);
        _time.Now = new DateTimeOffset(t1, TimeSpan.Zero);
        var result = await _sut.TestAsync(Spec("fake", kind: AoiSourceKinds.Fake, password: null));

        Assert.True(result.Ok);
        Assert.Null(result.ErrorMessage);
        var view = await _sut.GetAsync("fake");
        Assert.NotNull(view);
        Assert.Equal(t1, view!.LastTestedUtc);
        Assert.True(view.LastTestSucceeded);
        Assert.Null(view.LastTestError);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private async Task<byte[]?> LoadPasswordAsync(string key) =>
        await _db.AoiSourceConfigs
            .AsNoTracking()
            .Where(c => c.Key == key)
            .Select(c => c.EncryptedPassword)
            .SingleAsync();

    private static AoiSourceConfigSpec Spec(
        string key,
        string? password = "s3cr3t",
        string kind = "SqlServer",
        string? displayName = null) => new(
            Key: key,
            DisplayName: displayName ?? $"{key} display",
            Kind: kind,
            Server: kind == AoiSourceKinds.Fake ? null : "SERVER1",
            Database: kind == AoiSourceKinds.Fake ? null : "DB1",
            User: kind == AoiSourceKinds.Fake ? null : "svc_user",
            Password: password,
            ConnectTimeoutSeconds: 15,
            QueryTimeoutSeconds: 30,
            TrustServerCertificate: true,
            Encrypt: false,
            IsEnabled: true);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
