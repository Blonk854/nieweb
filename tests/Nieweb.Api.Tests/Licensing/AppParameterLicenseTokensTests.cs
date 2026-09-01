using Nieweb.Api.Licensing;
using Nieweb.Api.Parameters;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests.Licensing;

public sealed class AppParameterLicenseTokensTests
{
    [Fact]
    public async Task IsEnabledAsync_MissingRow_DefaultsTrue()
    {
        var service = new AppParameterLicenseTokens(new FakeAppParameters());

        var enabled = await service.IsEnabledAsync(LicenseTokenNames.Analyse, TestContext.Current.CancellationToken);

        Assert.True(enabled);
    }

    [Fact]
    public async Task IsEnabledAsync_ExplicitFalse_DisablesToken()
    {
        var fake = new FakeAppParameters()
            .With("license.analyse.enabled", AppParameterValueTypes.Bool, "false");
        var service = new AppParameterLicenseTokens(fake);

        var enabled = await service.IsEnabledAsync(LicenseTokenNames.Analyse, TestContext.Current.CancellationToken);

        Assert.False(enabled);
    }

    [Fact]
    public async Task IsEnabledAsync_NonBoolType_DefaultsTrue()
    {
        var fake = new FakeAppParameters()
            .With("license.analyse.enabled", AppParameterValueTypes.String, "nope");
        var service = new AppParameterLicenseTokens(fake);

        var enabled = await service.IsEnabledAsync(LicenseTokenNames.Analyse, TestContext.Current.CancellationToken);

        Assert.True(enabled);
    }

    [Fact]
    public void BuildParameterKey_NormalizesToken()
    {
        Assert.Equal("license.analyse.enabled", AppParameterLicenseTokens.BuildParameterKey(" Analyse "));
    }

    private sealed class FakeAppParameters : IAppParameters
    {
        private readonly Dictionary<string, AppParameterRow> _rows = new(StringComparer.Ordinal);

        public FakeAppParameters With(string key, string valueType, string value)
        {
            _rows[key] = new AppParameterRow(
                key,
                valueType,
                value,
                Description: null,
                IsSystem: true,
                CreatedUtc: DateTime.UtcNow,
                LastModifiedUtc: DateTime.UtcNow);
            return this;
        }

        public Task<IReadOnlyList<AppParameterRow>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<AppParameterRow>)_rows.Values.ToList());

        public Task<AppParameterRow?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_rows.TryGetValue(key, out var row) ? row : null);

        public Task<AppParameterUpsertResult> UpsertAsync(
            string key,
            string valueType,
            string value,
            string? description,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnsureSeededAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
