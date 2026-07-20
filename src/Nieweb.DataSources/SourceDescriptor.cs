namespace Nieweb.DataSources;

/// <summary>
/// Static metadata about a data source. Consumers use <see cref="Caps"/>
/// to decide which optional widgets / metrics to expose.
/// </summary>
/// <param name="Id">Stable identifier, e.g. "postreflow", "prereflow". Used in URLs and configs.</param>
/// <param name="DisplayName">Human-readable name shown in the source picker.</param>
/// <param name="SchemaVersion">Superviseur schema version reported by the source, e.g. "5.0".</param>
/// <param name="Caps">Bitset of optional capabilities this source supports.</param>
public sealed record SourceDescriptor(
    string Id,
    string DisplayName,
    string SchemaVersion,
    Capabilities Caps);
