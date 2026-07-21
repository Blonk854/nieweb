using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Nieweb.DataSources.Fake.DependencyInjection;

/// <summary>
/// DI helper that opt-in registers <see cref="FakeAoiSource"/> when
/// <c>Nieweb:Aoi:Fake:Enabled</c> is set to <c>true</c>. Used by the
/// Playwright E2E harness so a smoke run has a deterministic source
/// without needing SQL Server. Any other host (production, dev, CI
/// running the .NET test suites) leaves the flag false and gets no
/// fake source.
/// </summary>
public static class FakeAoiSourceServiceCollectionExtensions
{
    /// <summary>Configuration key that toggles registration.</summary>
    public const string EnabledConfigKey = "Nieweb:Aoi:Fake:Enabled";

    /// <summary>
    /// Registers <see cref="FakeAoiSource"/> as an <see cref="IAoiSource"/>
    /// singleton when <see cref="EnabledConfigKey"/> is truthy. No-op
    /// otherwise so the same call is safe in every host.
    /// </summary>
    public static IServiceCollection AddNiewebFakeAoiSource(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!bool.TryParse(configuration[EnabledConfigKey], out var enabled) || !enabled)
        {
            return services;
        }

        services.AddSingleton<FakeAoiSource>();
        services.AddSingleton<IAoiSource>(sp => sp.GetRequiredService<FakeAoiSource>());
        return services;
    }
}
