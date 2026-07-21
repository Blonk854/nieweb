using System.Diagnostics;
using System.Globalization;
using System.Reflection;

using Microsoft.EntityFrameworkCore;

using Nieweb.Data;

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

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

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

    var app = builder.Build();

    app.UseSerilogRequestLogging();

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
