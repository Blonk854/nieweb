using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

using Nieweb.Data;
using Nieweb.Data.Entities;
using Nieweb.DataSources;
using Nieweb.DataSources.Fake;
using Nieweb.DataSources.Sql;
namespace Nieweb.Api.DataSources;

/// <summary>
/// Boot-time bridge that reads persisted
/// <see cref="AoiSourceConfig"/> rows and registers the matching
/// concrete <see cref="IAoiSource"/> singletons in the host's service
/// collection.
/// </summary>
/// <remarks>
/// <para>
/// Called from <c>Program.cs</c> before <c>builder.Build()</c>.
/// Composes a throwaway <see cref="IServiceProvider"/> to open the
/// Nieweb DB, apply pending migrations, seed the table from
/// <c>Nieweb:Aoi:*</c> configuration on first run, and read back the
/// enabled rows. Each enabled row is then translated to a
/// <see cref="AoiSourceOptions"/> value and its concrete singleton
/// (<see cref="HlyaoiSource"/> / <see cref="MeaoiSource"/> /
/// <see cref="FakeAoiSource"/>) is registered.
/// </para>
/// <para>
/// Row edits at runtime therefore do not affect the currently running
/// process; an API restart is required. The UI surfaces this via the
/// pending-restart banner (see <see cref="IPendingRestartSignal"/>).
/// </para>
/// <para>
/// The seeder is idempotent — it only inserts rows when the table is
/// empty, so a redeploy that ships new <c>Nieweb:Aoi:*</c> values
/// will never overwrite an operator's DB edits.
/// </para>
/// </remarks>
public static partial class AoiSourceBootstrapper
{
    /// <summary>Well-known key for the post-reflow HLYAOI source.</summary>
    public const string KeyPostreflow = "postreflow";
    /// <summary>Well-known key for the pre-reflow MEAOI source.</summary>
    public const string KeyPrereflow = "prereflow";
    /// <summary>Well-known key for the in-memory Fake source (E2E / demos).</summary>
    public const string KeyFake = "fake";

    /// <summary>
    /// Migrate + seed the AOI configs table, then register enabled
    /// sources into <paramref name="builder"/>.
    /// </summary>
    public static void RegisterFromDatabase(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var provider = builder.Configuration["Nieweb:Db:Provider"] ?? "Sqlite";
        var conn = builder.Configuration.GetConnectionString("NiewebDb")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:NiewebDb is not configured; cannot bootstrap AOI sources.");
        var dataProtectionKeysDir = builder.Configuration["Nieweb:DataProtection:KeysDirectory"]
            ?? Path.Combine(builder.Environment.ContentRootPath, "data", "data-protection-keys");
        Directory.CreateDirectory(dataProtectionKeysDir);

        // Throwaway container. We only need DbContext + IDataProtection
        // to migrate, seed, and decrypt passwords before wiring the
        // concrete IAoiSource singletons back into the host.
        var tempServices = new ServiceCollection();
        tempServices.AddLogging(b => b.AddConsole());
        tempServices.AddDbContext<NiewebDbContext>(opts =>
        {
            if (string.Equals(provider, "Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                opts.UseNpgsql(conn, b => b.MigrationsAssembly("Nieweb.Data.Migrations.Npgsql"));
            }
            else
            {
                opts.UseSqlite(conn, b => b.MigrationsAssembly("Nieweb.Data.Migrations.Sqlite"));
            }
        });
        tempServices.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir))
            .SetApplicationName("Nieweb");
        tempServices.AddSingleton<IAoiPasswordProtector, AoiPasswordProtector>();

        using var sp = tempServices.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IAoiPasswordProtector>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AoiSourceBootstrapperMarker>>();

        db.Database.Migrate();

        if (!db.AoiSourceConfigs.Any())
        {
            SeedFromConfiguration(db, builder.Configuration, protector, logger);
            db.SaveChanges();
        }

        var enabled = db.AoiSourceConfigs
            .AsNoTracking()
            .Where(c => c.IsEnabled)
            .ToList();

        foreach (var row in enabled)
        {
            RegisterSource(builder.Services, row, protector, logger);
        }
    }

    /// <summary>Marker for <see cref="ILogger{T}"/>.</summary>
    public sealed class AoiSourceBootstrapperMarker;

    private static void SeedFromConfiguration(
        NiewebDbContext db,
        ConfigurationManager configuration,
        IAoiPasswordProtector protector,
        ILogger logger)
    {
        var now = DateTime.UtcNow;

        void SeedSqlServer(string key, string displayName, string subsection)
        {
            var section = configuration.GetSection($"Nieweb:Aoi:{subsection}");
            var server = section["Server"];
            var database = section["Database"];
            var user = section["User"];
            var password = section["Password"];
            if (string.IsNullOrWhiteSpace(server)
                || string.IsNullOrWhiteSpace(database)
                || string.IsNullOrWhiteSpace(user)
                || string.IsNullOrWhiteSpace(password))
            {
                LogSkippedSeed(logger, key, subsection);
                return;
            }
            db.AoiSourceConfigs.Add(new AoiSourceConfig
            {
                Key = key,
                DisplayName = displayName,
                Kind = AoiSourceKinds.SqlServer,
                Server = server,
                Database = database,
                User = user,
                EncryptedPassword = protector.Protect(password),
                ConnectTimeoutSeconds = int.TryParse(section["ConnectTimeoutSeconds"], out var ct) ? ct : 15,
                QueryTimeoutSeconds = int.TryParse(section["QueryTimeoutSeconds"], out var qt) ? qt : 30,
                TrustServerCertificate = !bool.TryParse(section["TrustServerCertificate"], out var tsc) || tsc,
                Encrypt = bool.TryParse(section["Encrypt"], out var enc) && enc,
                IsEnabled = true,
                CreatedUtc = now,
                LastModifiedUtc = now,
            });
            LogSeededSqlSource(logger, key);
        }

        SeedSqlServer(KeyPostreflow, "Post-reflow (HLYAOI)", "Postreflow");
        SeedSqlServer(KeyPrereflow, "Pre-reflow (MEAOI)", "Prereflow");

        // Fake source is opt-in via Nieweb:Aoi:Fake:Enabled=true. Seed
        // a disabled row when the flag is absent so operators can see
        // it in the UI and turn it on for local demos.
        var fakeEnabled = bool.TryParse(configuration["Nieweb:Aoi:Fake:Enabled"], out var fe) && fe;
        db.AoiSourceConfigs.Add(new AoiSourceConfig
        {
            Key = KeyFake,
            DisplayName = "Fake AOI (in-memory, for demos and E2E)",
            Kind = AoiSourceKinds.Fake,
            IsEnabled = fakeEnabled,
            CreatedUtc = now,
            LastModifiedUtc = now,
        });
        LogSeededFakeSource(logger, fakeEnabled);
    }

    private static void RegisterSource(
        IServiceCollection services,
        AoiSourceConfig row,
        IAoiPasswordProtector protector,
        ILogger logger)
    {
        if (string.Equals(row.Kind, AoiSourceKinds.Fake, StringComparison.Ordinal))
        {
            services.AddSingleton<FakeAoiSource>();
            services.AddSingleton<IAoiSource>(sp => sp.GetRequiredService<FakeAoiSource>());
            LogRegisteredFake(logger, row.Key);
            return;
        }

        if (!string.Equals(row.Kind, AoiSourceKinds.SqlServer, StringComparison.Ordinal))
        {
            LogUnknownKind(logger, row.Kind, row.Key);
            return;
        }

        var password = protector.Unprotect(row.EncryptedPassword);
        if (string.IsNullOrEmpty(row.Server)
            || string.IsNullOrEmpty(row.Database)
            || string.IsNullOrEmpty(row.User)
            || string.IsNullOrEmpty(password))
        {
            LogMissingFields(logger, row.Key);
            return;
        }

        var options = new AoiSourceOptions
        {
            Server = row.Server!,
            Database = row.Database!,
            User = row.User!,
            Password = password!,
            ConnectTimeoutSeconds = row.ConnectTimeoutSeconds,
            QueryTimeoutSeconds = row.QueryTimeoutSeconds,
            TrustServerCertificate = row.TrustServerCertificate,
            Encrypt = row.Encrypt,
        };

        switch (row.Key)
        {
            case KeyPostreflow:
                services.AddSingleton<HlyaoiSource>(sp =>
                    new HlyaoiSource(options, sp.GetService<ILogger<SqlServerAoiSourceBase>>()));
                services.AddSingleton<IAoiSource>(sp => sp.GetRequiredService<HlyaoiSource>());
                LogRegisteredHlyaoi(logger, row.Key, row.Server!, row.Database!);
                break;
            case KeyPrereflow:
                services.AddSingleton<MeaoiSource>(sp =>
                    new MeaoiSource(options, sp.GetService<ILogger<SqlServerAoiSourceBase>>()));
                services.AddSingleton<IAoiSource>(sp => sp.GetRequiredService<MeaoiSource>());
                LogRegisteredMeaoi(logger, row.Key, row.Server!, row.Database!);
                break;
            default:
                LogUnrecognisedKey(logger, row.Key);
                break;
        }
    }

    [LoggerMessage(EventId = 3610, Level = LogLevel.Information,
        Message = "Skipping seed for AOI source '{Key}' - Nieweb:Aoi:{Subsection} is incomplete.")]
    private static partial void LogSkippedSeed(ILogger logger, string key, string subsection);

    [LoggerMessage(EventId = 3611, Level = LogLevel.Information,
        Message = "Seeded AOI source '{Key}' from configuration.")]
    private static partial void LogSeededSqlSource(ILogger logger, string key);

    [LoggerMessage(EventId = 3612, Level = LogLevel.Information,
        Message = "Seeded AOI source 'fake' (enabled={Enabled}).")]
    private static partial void LogSeededFakeSource(ILogger logger, bool enabled);

    [LoggerMessage(EventId = 3613, Level = LogLevel.Information,
        Message = "Registered FakeAoiSource for key '{Key}'.")]
    private static partial void LogRegisteredFake(ILogger logger, string key);

    [LoggerMessage(EventId = 3614, Level = LogLevel.Warning,
        Message = "Unknown AOI source kind '{Kind}' for key '{Key}'; skipping.")]
    private static partial void LogUnknownKind(ILogger logger, string kind, string key);

    [LoggerMessage(EventId = 3615, Level = LogLevel.Warning,
        Message = "AOI source '{Key}' is enabled but missing required fields; skipping registration.")]
    private static partial void LogMissingFields(ILogger logger, string key);

    [LoggerMessage(EventId = 3616, Level = LogLevel.Information,
        Message = "Registered HlyaoiSource for key '{Key}' -> {Server}/{Database}.")]
    private static partial void LogRegisteredHlyaoi(ILogger logger, string key, string server, string database);

    [LoggerMessage(EventId = 3617, Level = LogLevel.Information,
        Message = "Registered MeaoiSource for key '{Key}' -> {Server}/{Database}.")]
    private static partial void LogRegisteredMeaoi(ILogger logger, string key, string server, string database);

    [LoggerMessage(EventId = 3618, Level = LogLevel.Warning,
        Message = "AOI source '{Key}' uses SqlServer kind but is not a recognised well-known key; supported keys are 'postreflow' and 'prereflow'.")]
    private static partial void LogUnrecognisedKey(ILogger logger, string key);
}
