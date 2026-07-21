using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nieweb.DataSources.Sql;

/// <summary>
/// DI helpers for registering the two live AOI Superviseur data sources
/// (<see cref="HlyaoiSource"/> post-reflow and <see cref="MeaoiSource"/>
/// pre-reflow) into a host's service collection.
/// </summary>
public static class AoiSourceServiceCollectionExtensions
{
    /// <summary>
    /// Configuration section root for AOI sources.
    /// Individual sources bind from <c>{Root}:Postreflow</c> and
    /// <c>{Root}:Prereflow</c>.
    /// </summary>
    public const string ConfigurationRoot = "Nieweb:Aoi";

    /// <summary>
    /// Registers <see cref="HlyaoiSource"/> and <see cref="MeaoiSource"/> as
    /// singletons if their configuration sections are populated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A source is considered "configured" when its <c>Server</c>,
    /// <c>Database</c>, <c>User</c>, and <c>Password</c> entries are all
    /// present under the appropriate subsection. Sources without credentials
    /// are skipped without throwing so a developer machine that lacks
    /// pre-reflow access can still boot the API.
    /// </para>
    /// <para>
    /// The recommended layout in <c>appsettings.json</c> (values themselves
    /// come from environment variables like <c>AOI_POSTREFLOW_SERVER</c>):
    /// <code>
    /// "Nieweb": {
    ///   "Aoi": {
    ///     "Postreflow": { "Server": "...", "Database": "HLYAOI2024", ... },
    ///     "Prereflow":  { "Server": "...", "Database": "MEAOI",   ... }
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddNiewebAoiSources(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        TryRegister<HlyaoiSource>(services, configuration, "Postreflow");
        TryRegister<MeaoiSource>(services, configuration, "Prereflow");
        return services;
    }

    private static void TryRegister<TSource>(
        IServiceCollection services,
        IConfiguration configuration,
        string subsectionName)
        where TSource : SqlServerAoiSourceBase
    {
        var section = configuration.GetSection($"{ConfigurationRoot}:{subsectionName}");
        if (!IsPopulated(section))
        {
            return;
        }

        // Named options so the correct set is applied per source type.
        services.Configure<AoiSourceOptions>(typeof(TSource).Name, section);

        services.AddSingleton<TSource>(sp =>
        {
            var monitor = sp.GetRequiredService<IOptionsMonitor<AoiSourceOptions>>();
            var opts = monitor.Get(typeof(TSource).Name);
            // Logger is optional so hosts without a logging provider (unit
            // tests, one-shot tools) still resolve. When present, every
            // AOI query is audited (source, DB, sql tag, duration, rows).
            var logger = sp.GetService<ILogger<SqlServerAoiSourceBase>>();
            return (TSource)Activator.CreateInstance(typeof(TSource), opts, logger)!;
        });

        // Second registration under the interface so consumers that just
        // want "give me every configured source" (e.g. GET /api/sources)
        // can resolve IEnumerable<IAoiSource> without having to enumerate
        // concrete types. The lambda forwards to the concrete singleton
        // above so both resolves return the exact same instance.
        services.AddSingleton<IAoiSource>(sp => sp.GetRequiredService<TSource>());
    }

    private static bool IsPopulated(IConfigurationSection section)
        => !string.IsNullOrWhiteSpace(section["Server"])
        && !string.IsNullOrWhiteSpace(section["Database"])
        && !string.IsNullOrWhiteSpace(section["User"])
        && !string.IsNullOrWhiteSpace(section["Password"]);
}
