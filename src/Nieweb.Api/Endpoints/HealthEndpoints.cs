using System.Globalization;
using System.Text.Json;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Health probe endpoints for load-balancers, Kubernetes-style
/// orchestrators, and Windows service monitoring. Three levels:
/// <list type="bullet">
/// <item><description><c>/health/live</c> - liveness only (process is up).</description></item>
/// <item><description><c>/health/ready</c> - readiness (self + Nieweb internal DB).</description></item>
/// <item><description><c>/health/db</c> - Nieweb internal DB round-trip.</description></item>
/// </list>
/// All three return a compact JSON body <c>{ "status": "...", "totalDurationMs": N,
/// "checks": { "name": { "status": "...", "description": "..." }, ... } }</c>
/// with HTTP 200 for Healthy, 200 for Degraded, and 503 for Unhealthy.
/// AOI Superviseur databases are intentionally NOT probed here to
/// avoid adding periodic queries onto the SMT-line critical path.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
            ResponseWriter = WriteJsonAsync,
            AllowCachingResponses = false,
        }).AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteJsonAsync,
            AllowCachingResponses = false,
        }).AllowAnonymous();

        endpoints.MapHealthChecks("/health/db", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("db"),
            ResponseWriter = WriteJsonAsync,
            AllowCachingResponses = false,
        }).AllowAnonymous();

        return endpoints;
    }

    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture),
            checks = report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)new
                {
                    status = kvp.Value.Status.ToString(),
                    description = kvp.Value.Description,
                    durationMs = kvp.Value.Duration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture),
                    tags = kvp.Value.Tags,
                }),
        };
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, JsonOptions),
            context.RequestAborted);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
