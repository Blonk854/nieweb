using System.Security.Claims;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Nieweb.Data;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Audit;

/// <summary>
/// Append-only audit log used to record every notable admin or
/// authentication action taken against a Nieweb tenant.
///
/// The interface exists so tests can substitute a fake and so the
/// production implementation (<see cref="EfAuditLog"/>) can be swapped
/// for a different sink later (e.g. a message bus) without touching the
/// call sites.
/// </summary>
public interface IAuditLog
{
    /// <summary>
    /// Writes a single audit record. Actor identity and IP address are
    /// resolved automatically from the current HTTP context — callers
    /// only need to describe *what* happened.
    /// </summary>
    /// <param name="eventType">
    /// Stable, dot-separated event key (e.g. <c>"user.created"</c>,
    /// <c>"user.role.changed"</c>, <c>"auth.signin.ok"</c>). Consumers
    /// group and filter on this value.
    /// </param>
    /// <param name="targetType">
    /// Domain of the entity the event pertains to (e.g. <c>"User"</c>,
    /// <c>"SavedView"</c>, <c>"Session"</c>).
    /// </param>
    /// <param name="targetId">String form of the target's PK.</param>
    /// <param name="details">
    /// Optional structured payload. Serialised with
    /// <see cref="JsonSerializerOptions.Web"/>. Pass <c>null</c> to emit
    /// an empty object.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(
        string eventType,
        string targetType,
        string targetId,
        object? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Variant that lets the caller specify actor identity explicitly.
    /// Required for flows that record events for an as-yet-not-signed-in
    /// user (e.g. an OIDC provisioning event fired before the JWT is
    /// issued, or a failed sign-in where there is no authenticated
    /// principal).
    /// </summary>
    Task WriteAsync(
        string eventType,
        string targetType,
        string targetId,
        int? actorUserId,
        string actorDisplayName,
        object? details = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IAuditLog"/> that persists rows through
/// <see cref="NiewebDbContext"/>. Shares the caller's scoped DbContext,
/// so audit rows commit inside the same unit of work as the operation
/// they describe.
/// </summary>
public sealed class EfAuditLog : IAuditLog
{
    // Details payloads are typically small and hand-shaped by call
    // sites; use the Web preset so property names are camelCase and
    // consistent with the rest of the API surface.
    private static readonly JsonSerializerOptions SerializerOptions
        = new(JsonSerializerDefaults.Web);

    private readonly NiewebDbContext _db;
    private readonly IHttpContextAccessor _httpContext;
    private readonly TimeProvider _time;

    public EfAuditLog(
        NiewebDbContext db,
        IHttpContextAccessor httpContext,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(time);
        _db = db;
        _httpContext = httpContext;
        _time = time;
    }

    public Task WriteAsync(
        string eventType,
        string targetType,
        string targetId,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        var principal = _httpContext.HttpContext?.User;
        var (actorId, actorName) = ResolveActor(principal);
        return WriteAsync(eventType, targetType, targetId, actorId, actorName, details, cancellationToken);
    }

    public async Task WriteAsync(
        string eventType,
        string targetType,
        string targetId,
        int? actorUserId,
        string actorDisplayName,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventType);
        ArgumentException.ThrowIfNullOrEmpty(targetType);
        ArgumentException.ThrowIfNullOrEmpty(targetId);
        ArgumentNullException.ThrowIfNull(actorDisplayName);

        var row = new AuditEvent
        {
            EventTimeUtc = _time.GetUtcNow().UtcDateTime,
            ActorUserId = actorUserId,
            ActorDisplayName = Truncate(actorDisplayName, 200),
            EventType = Truncate(eventType, 100),
            TargetType = Truncate(targetType, 100),
            TargetId = Truncate(targetId, 100),
            DetailsJson = details is null
                ? "{}"
                : JsonSerializer.Serialize(details, SerializerOptions),
            IpAddress = ResolveIp(),
        };

        _db.AuditEvents.Add(row);
        // Detach: audit rows are write-once, so we don't want them to
        // remain change-tracked once persisted. This also protects the
        // caller from an EF cascade where a subsequent SaveChangesAsync
        // in the same request accidentally re-writes the audit row.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.Entry(row).State = EntityState.Detached;
    }

    private static (int? UserId, string DisplayName) ResolveActor(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is null || !principal.Identity.IsAuthenticated)
        {
            return (null, "system");
        }
        var idStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        int? id = int.TryParse(idStr, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
        var name = principal.FindFirstValue("name")
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.Identity.Name
            ?? "unknown";
        return (id, name);
    }

    private string? ResolveIp()
    {
        var http = _httpContext.HttpContext;
        var remote = http?.Connection.RemoteIpAddress;
        return remote?.ToString();
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }
        return value[..max];
    }
}
