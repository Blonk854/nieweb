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
using Nieweb.Api.Startup;
using Nieweb.Data;
using Nieweb.DataSources.Fake.DependencyInjection;
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

    // When Windows launches Nieweb.Api under the Service Control
    // Manager (see tools/deploy/install-service.ps1), UseWindowsService()
    // wires the host lifetime to SCM start/stop signals, routes ETW
    // events to the Windows Event Log, and no-ops when the exe is
    // launched from a console. Safe to call unconditionally.
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = "Nieweb";
    });

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

    // OIDC / SSO configuration (I2). Bind unconditionally so
    // /auth/config can inspect it; the actual OpenID Connect handler
    // is only registered when Nieweb:Auth:Oidc:Enabled=true, keeping
    // the SPA "Sign in with SSO" button hidden on hosts without an
    // Entra registration.
    var oidcSection = builder.Configuration.GetSection("Nieweb:Auth:Oidc");
    builder.Services.Configure<OidcOptions>(oidcSection);
    var oidcOpts = oidcSection.Get<OidcOptions>() ?? new OidcOptions();
    builder.Services.AddScoped<OidcUserProvisioner>();

    var authBuilder = builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();

    if (oidcOpts.Enabled)
    {
        // Fail loud at boot rather than at first sign-in.
        if (string.IsNullOrWhiteSpace(oidcOpts.Authority)
            || string.IsNullOrWhiteSpace(oidcOpts.ClientId)
            || string.IsNullOrWhiteSpace(oidcOpts.ClientSecret))
        {
            throw new InvalidOperationException(
                "Nieweb:Auth:Oidc:Enabled=true but Authority / ClientId / "
                + "ClientSecret are not all populated. Supply the three via "
                + "environment variables (NIEWEB__AUTH__OIDC__CLIENTSECRET etc.) "
                + "or a secret store, or set Enabled=false to disable SSO.");
        }

        // Cookie scheme is used ONLY as a short-lived handoff channel:
        // the OIDC middleware writes the sign-in principal there, our
        // /auth/oidc/callback-return endpoint reads it once, provisions
        // the user, issues a JWT, and immediately SignOutAsync-s the
        // cookie. Nothing else on the API relies on the cookie so it
        // never bleeds into other endpoints.
        authBuilder
            .AddCookie(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                cookie =>
                {
                    cookie.Cookie.Name = "nieweb.oidc-handoff";
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.SameSite = SameSiteMode.Lax;
                    cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    cookie.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                    cookie.SlidingExpiration = false;
                })
            .AddOpenIdConnect(oidc =>
            {
                oidc.Authority = oidcOpts.Authority;
                oidc.ClientId = oidcOpts.ClientId;
                oidc.ClientSecret = oidcOpts.ClientSecret;
                oidc.CallbackPath = oidcOpts.CallbackPath;
                oidc.SignedOutCallbackPath = oidcOpts.SignedOutCallbackPath;
                oidc.ResponseType = "code";
                oidc.UsePkce = true;
                oidc.SaveTokens = false;
                oidc.GetClaimsFromUserInfoEndpoint = true;
                oidc.SignInScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
                oidc.Scope.Clear();
                foreach (var scope in oidcOpts.Scopes.Split(' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    oidc.Scope.Add(scope);
                }
                oidc.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                };
            });
    }

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
    //
    // Credentials never live in appsettings.json - they come from
    // AOI_{POSTREFLOW,PREREFLOW}_{SERVER,DATABASE,USER,PASSWORD} env
    // vars, optionally seeded from a .env file at the repo root (see
    // .env.example). AddNiewebAoiEnvironment walks up from the
    // ContentRoot to find a .env, loads any missing vars into the
    // process env, and layers them onto the configuration under
    // Nieweb:Aoi:*. AddNiewebAoiSources then binds the standard
    // sections. Both are safe no-ops when no credentials are present.
    //
    // The "Testing" environment (set by NiewebApiFactory) short-circuits
    // .env loading so integration tests do not accidentally register the
    // live post-reflow / pre-reflow sources when a developer's repo has
    // a populated .env on disk.
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        var aoiEnvFile = AoiEnvironmentConfigurationExtensions
            .FindEnvFile(builder.Environment.ContentRootPath);
        builder.Configuration.AddNiewebAoiEnvironment(aoiEnvFile);
    }
    builder.Services.AddNiewebAoiSources(builder.Configuration);

    // Opt-in in-memory fake source for the Playwright E2E harness (T2).
    // Enabled only when Nieweb:Aoi:Fake:Enabled=true - stays dormant on
    // every other host.
    builder.Services.AddNiewebFakeAoiSource(builder.Configuration);

    // Health checks for orchestration probes / load-balancers:
    //   /health/live  -> process is alive (always healthy while
    //                    the pipeline is running; no dependencies).
    //   /health/ready -> app is ready to serve traffic (self + the
    //                    Nieweb internal DB responded).
    //   /health/db    -> targeted probe of the Nieweb internal DB
    //                    only. We deliberately DO NOT health-check
    //                    the AOI Superviseur DBs from here to
    //                    avoid adding periodic queries onto the
    //                    SMT-line critical-path server.
    builder.Services.AddHealthChecks()
        .AddCheck(
            "self",
            () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
            tags: ["live", "ready"])
        .AddDbContextCheck<NiewebDbContext>(
            name: "nieweb-db",
            tags: ["ready", "db"]);

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // Serve the built React SPA (Nieweb.Web) from wwwroot/app when it is
    // present. The bundle lands there via `npm run build`, either during
    // developer smoke or automatically at publish time via the
    // BuildNiewebSpa MSBuild target in Nieweb.Api.csproj. If wwwroot/app
    // does not exist (fresh clone, API-only test host) both middlewares
    // simply no-op.
    //
    // IMPORTANT: register these BEFORE the explicit UseRouting() call
    // below. WebApplication would otherwise auto-insert UseRouting at
    // the top of the pipeline, matching the /app/{*catchall} fallback
    // endpoint for every /app/assets/*.js request. StaticFileMiddleware
    // then sees a matched endpoint and defers to it, so hashed asset
    // URLs would incorrectly receive the SPA shell (HTML) instead of
    // their .js/.css bytes — which trips the browser's strict module
    // MIME check. Serving files before routing sidesteps that.
    var spaContentRoot = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "app");
    if (Directory.Exists(spaContentRoot))
    {
        var spaFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(spaContentRoot);
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            RequestPath = "/app",
            FileProvider = spaFileProvider,
            DefaultFileNames = ["index.html"],
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = "/app",
            FileProvider = spaFileProvider,
        });
    }

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapAuthEndpoints();
    app.MapOidcEndpoints();
    app.MapSourceEndpoints();
    app.MapReportEndpoints();
    app.MapSavedViewEndpoints();
    app.MapAdminUsersEndpoints();
    app.MapHealthEndpoints();

    // SPA fallback: TanStack Router uses HTML5 history, so a hard
    // refresh on /app/report/panel-yield needs to serve the SPA shell
    // (wwwroot/app/index.html) rather than 404. We only register the
    // fallback if the built SPA is actually present on disk so an
    // API-only test host (or a fresh clone that hasn't run
    // `npm run build`) keeps returning 404 for unknown routes as
    // expected. Redirect / -> /app/ so browsers hitting the bare host
    // land on the SPA. Hashed asset URLs never reach this fallback
    // because UseStaticFiles is registered before UseRouting above.
    var spaIndexPath = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "app", "index.html");
    if (File.Exists(spaIndexPath))
    {
        app.MapGet("/", () => Results.Redirect("/app/", permanent: false))
            .ExcludeFromDescription();
        app.MapFallback("/app/{*catchall}", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(spaIndexPath);
        });
    }

    // Apply pending migrations, ensure built-in roles exist, and
    // (if configured) create the bootstrap administrator. Runs
    // synchronously before Kestrel starts accepting requests so a
    // partially-provisioned host never serves traffic.
    await app.EnsureBootstrapAsync().ConfigureAwait(false);

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
