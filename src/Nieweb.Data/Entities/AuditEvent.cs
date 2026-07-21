namespace Nieweb.Data.Entities;

/// <summary>
/// Immutable audit record. One row per notable action taken by a user or
/// by the system on Nieweb's behalf.
/// </summary>
/// <remarks>
/// Audit events are append-only - they are never updated or deleted from
/// application code. Retention/archival is a separate ops concern.
/// </remarks>
public sealed class AuditEvent
{
    /// <summary>Auto-generated surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    public DateTime EventTimeUtc { get; set; }

    /// <summary>
    /// User who performed the action, or null for actions taken by the
    /// system (batch jobs, startup migrations, etc.).
    /// </summary>
    public int? ActorUserId { get; set; }

    /// <summary>
    /// Snapshot of the actor's display name at the moment of the event -
    /// preserved even if the user is later renamed or deleted.
    /// </summary>
    public string ActorDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Stable event-type key (e.g. <c>"user.created"</c>,
    /// <c>"user.role.changed"</c>, <c>"savedview.deleted"</c>).
    /// Referenced by the UI for grouping/filtering.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Domain of the target entity (e.g. <c>"User"</c>,
    /// <c>"SavedView"</c>, <c>"Role"</c>).
    /// </summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>
    /// String form of the target entity's PK.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// JSON blob with structured details: before/after values, action
    /// context, request IDs, etc. Consumers parse on demand.
    /// </summary>
    public string DetailsJson { get; set; } = "{}";

    /// <summary>
    /// Remote IP address of the request that triggered the event, or
    /// null for system events.
    /// </summary>
    public string? IpAddress { get; set; }
}
