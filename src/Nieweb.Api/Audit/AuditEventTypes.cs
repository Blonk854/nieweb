namespace Nieweb.Api.Audit;

/// <summary>
/// Stable, dot-separated event-type keys written to
/// <c>AuditEvents.EventType</c>. Consumers filter and group on these
/// strings so any rename is a breaking change to historical queries.
/// </summary>
public static class AuditEventTypes
{
    /// <summary>Admin created a local user account.</summary>
    public const string UserCreated = "user.created";

    /// <summary>Admin updated a user's display name / disabled flag / roles.</summary>
    public const string UserUpdated = "user.updated";

    /// <summary>Admin changed at least one role assignment.</summary>
    public const string UserRoleChanged = "user.role.changed";

    /// <summary>Admin reset a user's password out-of-band.</summary>
    public const string UserPasswordReset = "user.password.reset";

    /// <summary>OIDC sign-in auto-provisioned a new user.</summary>
    public const string UserOidcProvisioned = "user.oidc.provisioned";

    /// <summary>OIDC sign-in attached a new external-login binding to an existing user.</summary>
    public const string UserOidcLinked = "user.oidc.linked";

    /// <summary>OIDC sign-in was refused because a local account with the same email already exists.</summary>
    public const string OidcConflict = "user.oidc.conflict";

    /// <summary>Local username + password sign-in succeeded.</summary>
    public const string AuthSignInOk = "auth.signin.ok";

    /// <summary>Sign-in was refused (bad credentials, disabled account, forced-rotation gate, ...).</summary>
    public const string AuthSignInFailed = "auth.signin.failed";

    /// <summary>OIDC sign-in completed and a Nieweb JWT was issued.</summary>
    public const string AuthSsoSignInOk = "auth.sso.signin.ok";

    /// <summary>User changed their own password.</summary>
    public const string AuthPasswordChanged = "auth.password.changed";

    /// <summary>Admin created an application parameter row.</summary>
    public const string AppParameterCreated = "app.parameter.created";

    /// <summary>Admin updated an application parameter value or description.</summary>
    public const string AppParameterUpdated = "app.parameter.updated";

    /// <summary>Admin deleted a non-system application parameter row.</summary>
    public const string AppParameterDeleted = "app.parameter.deleted";

    /// <summary>Admin created a production line.</summary>
    public const string ProductionLineCreated = "production.line.created";

    /// <summary>Admin renamed or re-sorted a production line.</summary>
    public const string ProductionLineUpdated = "production.line.updated";

    /// <summary>Admin deleted a production line (cascading its machine assignments).</summary>
    public const string ProductionLineDeleted = "production.line.deleted";

    /// <summary>Admin attached a machine to a production line.</summary>
    public const string ProductionLineMachineAdded = "production.line.machine.added";

    /// <summary>Admin detached a machine from a production line.</summary>
    public const string ProductionLineMachineRemoved = "production.line.machine.removed";

    /// <summary>Admin replaced the site-wide shift cycle.</summary>
    public const string ShiftsReplaced = "shifts.replaced";

    /// <summary>Admin created a report group.</summary>
    public const string ReportGroupCreated = "report.group.created";

    /// <summary>Admin updated a report group.</summary>
    public const string ReportGroupUpdated = "report.group.updated";

    /// <summary>Admin deleted a report group.</summary>
    public const string ReportGroupDeleted = "report.group.deleted";

    /// <summary>Admin created a report.</summary>
    public const string ReportCreated = "report.created";

    /// <summary>Admin updated a report header.</summary>
    public const string ReportUpdated = "report.updated";

    /// <summary>Admin deleted a report.</summary>
    public const string ReportDeleted = "report.deleted";

    /// <summary>Admin appended a tile to a report.</summary>
    public const string ReportEntityAdded = "report.entity.added";

    /// <summary>Admin updated a tile inside a report.</summary>
    public const string ReportEntityUpdated = "report.entity.updated";

    /// <summary>Admin removed a tile from a report.</summary>
    public const string ReportEntityRemoved = "report.entity.removed";

    /// <summary>Owner or admin set a lock password on a report (RC3).</summary>
    public const string ReportLocked = "report.locked";

    /// <summary>Owner or admin cleared a report's lock password (RC3).</summary>
    public const string ReportUnlocked = "report.unlocked";

    /// <summary>User cloned a report into a new unlocked copy (RC3).</summary>
    public const string ReportDuplicated = "report.duplicated";

    /// <summary>Admin pinned a report to the shared home page (F14).</summary>
    public const string ReportPinned = "report.pinned";

    /// <summary>Admin unpinned a report from the shared home page (F14).</summary>
    public const string ReportUnpinned = "report.unpinned";

    /// <summary>Admin registered a board-SVG source folder (TC4).</summary>
    public const string BoardSvgSourceAdded = "board.svg.source.added";

    /// <summary>Admin edited a board-SVG source folder (name/path/enabled) (TC4).</summary>
    public const string BoardSvgSourceUpdated = "board.svg.source.updated";

    /// <summary>Admin removed a board-SVG source folder (TC4).</summary>
    public const string BoardSvgSourceRemoved = "board.svg.source.removed";

    /// <summary>Sync worker copied a fresh SVG into the local cache (TC4 Phase B).</summary>
    public const string BoardSvgSynced = "board.svg.synced";

    /// <summary>Sync worker failed to pull an SVG for a product (TC4 Phase B).</summary>
    public const string BoardSvgSyncFailed = "board.svg.sync.failed";

    /// <summary>Admin created an AOI data-source configuration (Phase C).</summary>
    public const string DataSourceCreated = "data-source.created";

    /// <summary>Admin updated an AOI data-source configuration (Phase C).</summary>
    public const string DataSourceUpdated = "data-source.updated";

    /// <summary>Admin deleted an AOI data-source configuration (Phase C).</summary>
    public const string DataSourceDeleted = "data-source.deleted";

    /// <summary>Admin ran a "Test connection" against an AOI data-source spec (Phase C).</summary>
    public const string DataSourceTested = "data-source.tested";

    /// <summary>Admin requested a process restart from the Databases screen (Phase C).</summary>
    public const string DataSourceRestartRequested = "data-source.restart.requested";
}

/// <summary>
/// Stable target-type keys written to <c>AuditEvents.TargetType</c>.
/// </summary>
public static class AuditTargetTypes
{
    public const string User = "User";
    public const string Session = "Session";
    public const string SavedView = "SavedView";
    public const string AppParameter = "AppParameter";
    public const string ProductionLine = "ProductionLine";
    public const string ProductionLineMachine = "ProductionLineMachine";
    public const string ShiftCycle = "ShiftCycle";
    public const string ReportGroup = "ReportGroup";
    public const string Report = "Report";
    public const string ReportEntity = "ReportEntity";
    public const string BoardSvgSource = "BoardSvgSource";
    public const string BoardSvg = "BoardSvg";
    public const string DataSource = "DataSource";
}
