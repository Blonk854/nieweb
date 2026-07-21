using System.Collections;

using Microsoft.Extensions.Configuration;

namespace Nieweb.DataSources.Sql;

/// <summary>
/// Bridges the flat <c>AOI_POSTREFLOW_*</c> / <c>AOI_PREREFLOW_*</c>
/// environment variables (as documented in <c>.env.example</c> and consumed by
/// <c>tools/db/probe-schema.ps1</c>) into the structured
/// <c>Nieweb:Aoi:{Postreflow,Prereflow}:*</c> configuration section that
/// <see cref="AoiSourceServiceCollectionExtensions.AddNiewebAoiSources"/>
/// binds against.
/// </summary>
/// <remarks>
/// <para>
/// The AOI Superviseur credentials are intentionally never committed to source
/// control. They live in an operator-managed <c>.env</c> file at the repo root
/// (or wherever the host is deployed). This extension:
/// </para>
/// <list type="number">
///   <item><description>Optionally loads a <c>.env</c> file into process env
///     vars (without overwriting anything already set — real environment wins,
///     as it should).</description></item>
///   <item><description>Maps the four required suffixes (<c>SERVER</c>,
///     <c>DATABASE</c>, <c>USER</c>, <c>PASSWORD</c>) for each source, plus the
///     two shared timeout knobs, into an in-memory configuration source layered
///     onto the builder.</description></item>
/// </list>
/// <para>
/// Any AOI env var that is unset or blank is simply not mapped — the downstream
/// <see cref="AoiSourceServiceCollectionExtensions.AddNiewebAoiSources"/> skips
/// sources whose section is not fully populated, so a developer machine that
/// has only post-reflow credentials still boots.
/// </para>
/// </remarks>
public static class AoiEnvironmentConfigurationExtensions
{
    // Suffix -> config-property-name lookup. Explicit rather than
    // ToLower/PascalCase gymnastics so a typo can't create phantom keys.
    private static readonly (string Suffix, string PropertyName)[] SourceKeys =
    [
        ("SERVER", "Server"),
        ("DATABASE", "Database"),
        ("USER", "User"),
        ("PASSWORD", "Password"),
    ];

    private const string PostreflowEnvPrefix = "AOI_POSTREFLOW_";
    private const string PrereflowEnvPrefix = "AOI_PREREFLOW_";
    private const string PostreflowConfigPrefix = "Nieweb:Aoi:Postreflow";
    private const string PrereflowConfigPrefix = "Nieweb:Aoi:Prereflow";
    private const string ConnectTimeoutEnv = "AOI_CONNECT_TIMEOUT";
    private const string QueryTimeoutEnv = "AOI_QUERY_TIMEOUT";
    private const string ConnectTimeoutProp = "ConnectTimeoutSeconds";
    private const string QueryTimeoutProp = "QueryTimeoutSeconds";

    /// <summary>
    /// Loads AOI credentials from environment variables (optionally seeded from
    /// a <c>.env</c> file) and layers them onto <paramref name="builder"/>
    /// under <c>Nieweb:Aoi:*</c>.
    /// </summary>
    /// <param name="builder">The configuration builder to extend.</param>
    /// <param name="envFilePath">Optional path to a <c>.env</c> file to load
    /// into <see cref="Environment"/> before mapping. If <c>null</c> or the
    /// file does not exist, only the current process environment is consulted.
    /// Variables already set in the environment are never overwritten by the
    /// file — real env wins.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static IConfigurationBuilder AddNiewebAoiEnvironment(
        this IConfigurationBuilder builder,
        string? envFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (envFilePath is not null && File.Exists(envFilePath))
        {
            LoadEnvFile(envFilePath);
        }

        var map = BuildMap(Environment.GetEnvironmentVariables());
        if (map.Count > 0)
        {
            builder.AddInMemoryCollection(map);
        }
        return builder;
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> looking for a
    /// <c>.env</c> file, up to 8 levels deep. Returns the absolute path
    /// or <c>null</c> if no <c>.env</c> is found. Handy for hosts where
    /// the working directory (or <c>ContentRootPath</c>) sits several
    /// levels inside a repo (e.g. <c>src/Nieweb.Api</c>).
    /// </summary>
    public static string? FindEnvFile(string? startDirectory = null)
    {
        var current = new DirectoryInfo(
            startDirectory is null || string.IsNullOrWhiteSpace(startDirectory)
                ? Directory.GetCurrentDirectory()
                : startDirectory);

        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            var candidate = Path.Combine(current.FullName, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }
        return null;
    }

    // Internal for direct unit-testing without touching the ambient process
    // environment. The input is intentionally IDictionary (not
    // IDictionary<string,string?>) so tests can pass either a Hashtable or
    // the raw output of Environment.GetEnvironmentVariables().
    internal static Dictionary<string, string?> BuildMap(IDictionary env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        MapSubsection(env, PostreflowEnvPrefix, PostreflowConfigPrefix, result);
        MapSubsection(env, PrereflowEnvPrefix, PrereflowConfigPrefix, result);

        // Shared timeouts apply to whichever source is actually configured.
        // We write both keys unconditionally when the env var is set - the
        // one that lands under an unconfigured source is harmless (that
        // source will be skipped by AddNiewebAoiSources regardless).
        if (TryGetString(env, ConnectTimeoutEnv, out var connect))
        {
            result[$"{PostreflowConfigPrefix}:{ConnectTimeoutProp}"] = connect;
            result[$"{PrereflowConfigPrefix}:{ConnectTimeoutProp}"] = connect;
        }
        if (TryGetString(env, QueryTimeoutEnv, out var query))
        {
            result[$"{PostreflowConfigPrefix}:{QueryTimeoutProp}"] = query;
            result[$"{PrereflowConfigPrefix}:{QueryTimeoutProp}"] = query;
        }

        return result;
    }

    private static void MapSubsection(
        IDictionary env,
        string envPrefix,
        string configPrefix,
        Dictionary<string, string?> result)
    {
        foreach (var (suffix, propertyName) in SourceKeys)
        {
            if (TryGetString(env, envPrefix + suffix, out var value))
            {
                result[$"{configPrefix}:{propertyName}"] = value;
            }
        }
    }

    private static bool TryGetString(IDictionary env, string key, out string value)
    {
        if (env.Contains(key) && env[key] is object raw)
        {
            var s = raw.ToString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                value = s;
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Minimal .env parser: KEY=VALUE per line, blank lines and lines starting
    /// with '#' are skipped, surrounding matching single or double quotes are
    /// stripped. Existing environment variables are never overwritten.
    /// </summary>
    internal static void LoadEnvFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq < 1)
            {
                continue;
            }

            var key = line[..eq].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2)
            {
                var first = value[0];
                var last = value[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    value = value[1..^1];
                }
            }

            // Real environment always wins over the file.
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
