namespace Nieweb.DataSources.Sql;

/// <summary>
/// Runtime configuration for one <see cref="SqlServerAoiSourceBase"/> instance.
/// Populated by the host (e.g. from appsettings.json bound to environment variables
/// loaded from <c>.env</c> — the host is responsible for env loading, not the library).
/// </summary>
public sealed record AoiSourceOptions
{
    /// <summary>SQL Server host name or DNS alias.</summary>
    public required string Server { get; init; }

    /// <summary>Initial catalog / database name.</summary>
    public required string Database { get; init; }

    /// <summary>SQL Server login. Read-only account strongly preferred.</summary>
    public required string User { get; init; }

    /// <summary>Password for <see cref="User"/>. Never log this.</summary>
    public required string Password { get; init; }

    /// <summary>Connect timeout in seconds. Defaults to 15.</summary>
    public int ConnectTimeoutSeconds { get; init; } = 15;

    /// <summary>Per-command timeout in seconds. Defaults to 30 (the "keep it simple" cap we agreed on).</summary>
    public int QueryTimeoutSeconds { get; init; } = 30;

    /// <summary>Trust server certificate (needed for older archived servers with self-signed certs).</summary>
    public bool TrustServerCertificate { get; init; } = true;

    /// <summary>Encrypt transport. Kept configurable; defaults to <c>false</c> to match the probe scripts.</summary>
    public bool Encrypt { get; init; }
}
