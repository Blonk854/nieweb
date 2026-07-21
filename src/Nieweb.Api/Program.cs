using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Nieweb.Api.Auth;
using Nieweb.Api.Endpoints;
using Nieweb.Data;
using Nieweb.DataSources.Sql;
using Nieweb.Identity.DependencyInjection;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Serilog;

// Two-stage Serilog init: the bootstrap logger writes to console during host
// construction so we can log failures that happen before the app is built.
// Once the host is running the final logger is reloaded from configuration
// (appsettings.json / appsettings.{Environment}.json), which lets ops tune
// levels and sinks without a recompile.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Nieweb.Api host");

    var builder = WebApplication.CreateBuilder(args);

    // Fail fast on DI misconfiguration: every registered service is
    // constructed once at host build time, and every resolve validates
    // that scoped services are not captured by singletons. Small cost at
    // startup, catches wiring bugs before they hit a request path.
    builder.Host.UseDefaultServiceProvider(options =>
    {
        options.ValidateOnBuild = true;
        options.ValidateScopes = true;
    });

    builder.Host.UseSerilog(
        (context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext(),
        // preserveStaticLogger: keep the bootstrap Log.Logger intact so that
        // WebApplicationFactory<Program>-based integration tests, which
        // build multiple hosts in a single process, do not re-freeze the
        // reloadable logger (which throws "The logger is already frozen").
        // Runtime logging still flows through ILogger<T> resolved from DI,
        // so behaviour in a single-host production process is unchanged.
        preserveStaticLogger: true);

    // Assembly-derived resource attributes surface in every trace, metric,
    // and log record OpenTelemetry produces.
    var apiAssembly = Assembly.GetExecutingAssembly();
    var apiVersion = apiAssembly.GetName().Version?.ToString() ?? "0.0.0";

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r
            .AddService(serviceName: "Nieweb.Api", serviceVersion: apiVersion)
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("deployment.environment", builder.Environment.EnvironmentName),
                new("host.name", Environment.MachineName),
            }))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource(NiewebDiagnostics.ActivitySourceName)
            .AddConsoleExporter())
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(NiewebDiagnostics.MeterName)
            .AddConsoleExporter());

    // Nieweb's internal database (users, roles, saved views, audit log).
    // The provider is selected by Nieweb:Db:Provider ("Sqlite" or "Npgsql");
    // MigrationsAssembly is pinned per provider so EF picks up only the
    // matching migration set from Nieweb.Data.Migrations.{Sqlite,Npgsql}.
    var dbProvider = builder.Configuration["Nieweb:Db:Provider"] ?? "Sqlite";
    var dbConnection = builder.Configuration.GetConnectionString("NiewebDb")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:NiewebDb is not configured. Set it in appsettings, "
            + "user secrets, or the NIEWEB__CONNECTIONSTRINGS__NIEWEBDB env var.");

    builder.Services.AddDbContext<NiewebDbContext>(options =>
    {
        if (string.Equals(dbProvider, "Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            options.UseNpgsql(
                dbConnection,
                b => b.MigrationsAssembly("Nieweb.Data.Migrations.Npgsql"));
        }
        else if (string.Equals(dbProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlite(
                dbConnection,
                b => b.MigrationsAssembly("Nieweb.Data.Migrations.Sqlite"));
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown Nieweb:Db:Provider '{dbProvider}'. Use 'Sqlite' or 'Npgsql'.");
        }
    });

    // ASP.NET Core Identity for NiewebUser/NiewebRole, wired to
    // NiewebDbContext and backed by an Argon2id password hasher. Options
    // (password rules, lockout, Argon2id cost) come from configuration
    // section Nieweb:Identity - see appsettings.json for defaults.
    builder.Services.AddNiewebIdentity(builder.Configuration);

    // JWT bearer authentication for /auth/* and every future
    // [Authorize]-protected endpoint. Validation parameters are wired
    // through the options pipeline so a test host (or a future secret
    // rotation) that overrides Nieweb:Auth:Jwt after container
    // construction still takes effect.
    var jwtConfigSection = builder.Configuration.GetSection("Nieweb:Auth:Jwt");
    builder.Services.Configure<JwtOptions>(jwtConfigSection);

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();

    builder.Services
        .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IOptionsMonitor<JwtOptions>>((bearer, jwt) =>
        {
            var opts = jwt.CurrentValue;
            if (string.IsNullOrWhiteSpace(opts.SigningKey)
                || Encoding.UTF8.GetByteCount(opts.SigningKey) < 32)
            {
                throw new InvalidOperationException(
                    "Nieweb:Auth:Jwt:SigningKey must be at least 32 UTF-8 bytes. "
                    + "Override it via user-secrets, an environment variable, or a "
                    + "secret store - never commit a production key to source control.");
            }
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = opts.Issuer,
                ValidateAudience = true,
                ValidAudience = opts.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = opts.ClockSkew,
                NameClaimType = System.Security.Claims.ClaimTypes.Name,
                RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            };
        });
    builder.Services.AddAuthorization();

    // AOI Superviseur read-only data sources. Only the sources whose
    // credentials are populated in Nieweb:Aoi:{Postreflow,Prereflow}
    // are registered; missing credentials are treated as "not
    // available on this host" (developer machines that only see one
    // DB, CI hosts that see none). Every wire query enforces the
    // read-only discipline documented in copilot-instructions.md.
    builder.Services.AddNiewebAoiSources(builder.Configuration);

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // Serve the built React SPA (Nieweb.Web) from wwwroot/app when it is
    // present. The bundle lands there via `npm run build`, either during
    // developer smoke or automatically at publish time via the
    // BuildNiewebSpa MSBuild target in Nieweb.Api.csproj. If wwwroot/app
    // does not exist (fresh clone, API-only test host) both middlewares
    // simply no-op.
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        RequestPath = "/app",
        DefaultFileNames = ["index.html"],
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        RequestPath = "/app",
    });

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapAuthEndpoints();
    app.MapSourceEndpoints();
    app.MapReportEndpoints();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Nieweb.Api host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Well-known ActivitySource / Meter names for Nieweb-owned instrumentation.
/// Report / adapter code creates activities and metrics against these names
/// so the tracing/metrics pipelines above pick them up automatically.
/// </summary>
internal static class NiewebDiagnostics
{
    public const string ActivitySourceName = "Nieweb.Api";
    public const string MeterName = "Nieweb.Api";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
}

/// <summary>
/// Top-level Program placeholder. Exposed so integration tests can use
/// <c>WebApplicationFactory&lt;Program&gt;</c> against the real host.
/// </summary>
#pragma warning disable CA1050 // Declare types in namespaces - top-level Program has no namespace by design.
public partial class Program
{
}
#pragma warning restore CA1050
