using Nieweb.Api.Parameters;
using Nieweb.Api.SkipClassification;
using Nieweb.Data.Entities;
using Nieweb.Reports.Common.Skips;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// Unit tests for <see cref="SkipClassificationConfigProvider"/>: it
/// reads the four <c>skip.*</c> app parameters into a
/// <see cref="SkipClassificationConfig"/> and falls back per-field to
/// <see cref="SkipClassificationConfig.Default"/> on any missing or
/// malformed value.
/// </summary>
public sealed class SkipClassificationConfigProviderTests
{
    [Fact]
    public async Task Empty_ReturnsDefault()
    {
        var provider = new SkipClassificationConfigProvider(new FakeAppParameters());
        var config = await provider.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0.50, config.MissingRatioThreshold);
        Assert.Equal(8, config.MinComponentFloor);
        Assert.Equal(4, config.AbsoluteMissingFloor);
        Assert.Equal(RepairButtonMeaning.ManualSkip, config.MeaningOf("X-OUT"));
        Assert.Equal(RepairButtonMeaning.ManualSkip, config.MeaningOf("x-out")); // case-insensitive
    }

    [Fact]
    public async Task Valid_ReturnsPersistedValues()
    {
        var fake = new FakeAppParameters()
            .With(SkipClassificationConfigProvider.MissingRatioThresholdKey, AppParameterValueTypes.Decimal, "0.75")
            .With(SkipClassificationConfigProvider.MinComponentFloorKey, AppParameterValueTypes.Int, "12")
            .With(SkipClassificationConfigProvider.AbsoluteMissingFloorKey, AppParameterValueTypes.Int, "6")
            .With(SkipClassificationConfigProvider.RepairButtonMeaningsKey, AppParameterValueTypes.String,
                "{\"X-OUT\":\"ManualSkip\",\"MY_MISSING\":\"ConfirmedRealMissing\",\"FC\":\"FalseCall\"}");

        var config = await new SkipClassificationConfigProvider(fake)
            .GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0.75, config.MissingRatioThreshold);
        Assert.Equal(12, config.MinComponentFloor);
        Assert.Equal(6, config.AbsoluteMissingFloor);
        Assert.Equal(RepairButtonMeaning.ManualSkip, config.MeaningOf("X-OUT"));
        Assert.Equal(RepairButtonMeaning.ConfirmedRealMissing, config.MeaningOf("MY_MISSING"));
        Assert.Equal(RepairButtonMeaning.FalseCall, config.MeaningOf("FC"));
        Assert.Equal(RepairButtonMeaning.Normal, config.MeaningOf("UNKNOWN"));
    }

    [Fact]
    public async Task OutOfRangeRatio_FallsBackToDefault()
    {
        var fake = new FakeAppParameters()
            .With(SkipClassificationConfigProvider.MissingRatioThresholdKey, AppParameterValueTypes.Decimal, "2.0");
        var config = await new SkipClassificationConfigProvider(fake)
            .GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0.50, config.MissingRatioThreshold);
    }

    [Fact]
    public async Task NonPositiveFloor_FallsBackToDefault()
    {
        var fake = new FakeAppParameters()
            .With(SkipClassificationConfigProvider.MinComponentFloorKey, AppParameterValueTypes.Int, "0")
            .With(SkipClassificationConfigProvider.AbsoluteMissingFloorKey, AppParameterValueTypes.Int, "-3");
        var config = await new SkipClassificationConfigProvider(fake)
            .GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(8, config.MinComponentFloor);
        Assert.Equal(4, config.AbsoluteMissingFloor);
    }

    [Fact]
    public async Task MalformedButtonMap_FallsBackToDefault()
    {
        var fake = new FakeAppParameters()
            .With(SkipClassificationConfigProvider.RepairButtonMeaningsKey, AppParameterValueTypes.String, "not-json");
        var config = await new SkipClassificationConfigProvider(fake)
            .GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RepairButtonMeaning.ManualSkip, config.MeaningOf("X-OUT"));
    }

    [Fact]
    public async Task ButtonMap_DropsUnknownMeaningsButKeepsValidOnes()
    {
        var fake = new FakeAppParameters()
            .With(SkipClassificationConfigProvider.RepairButtonMeaningsKey, AppParameterValueTypes.String,
                "{\"X-OUT\":\"ManualSkip\",\"BOGUS\":\"NotAMeaning\"}");
        var config = await new SkipClassificationConfigProvider(fake)
            .GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RepairButtonMeaning.ManualSkip, config.MeaningOf("X-OUT"));
        Assert.Equal(RepairButtonMeaning.Normal, config.MeaningOf("BOGUS"));
    }

    private sealed class FakeAppParameters : IAppParameters
    {
        private readonly Dictionary<string, AppParameterRow> _rows = new(StringComparer.Ordinal);

        public FakeAppParameters With(string key, string valueType, string value)
        {
            _rows[key] = new AppParameterRow(
                key, valueType, value, Description: null, IsSystem: true,
                CreatedUtc: DateTime.UtcNow, LastModifiedUtc: DateTime.UtcNow);
            return this;
        }

        public Task<IReadOnlyList<AppParameterRow>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<AppParameterRow>)_rows.Values.ToList());

        public Task<AppParameterRow?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_rows.TryGetValue(key, out var row) ? row : null);

        public Task<AppParameterUpsertResult> UpsertAsync(
            string key, string valueType, string value, string? description,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnsureSeededAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
