using System.Text.RegularExpressions;

namespace Nieweb.DataSources.Sql;

/// <summary>
/// Compile-time constants and helpers that enforce the read-only discipline
/// mandated by <c>.github/copilot-instructions.md</c>. Any SQL text that
/// reaches the wire passes through <see cref="EnsureReadOnly"/> first.
/// </summary>
public static partial class SqlGuards
{
    /// <summary>
    /// Prelude prepended to every batch. Read-uncommitted so we neither block
    /// nor get blocked by the AOI Superviseur writers; NOCOUNT so trailing
    /// affected-rows messages don't confuse ADO readers.
    /// </summary>
    public const string IsolationPrelude =
        "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;\nSET NOCOUNT ON;\n";

    [GeneratedRegex(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|CREATE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenKeywords();

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if <paramref name="sql"/>
    /// contains any DDL/DML write keyword. Purely defensive: the account
    /// should also be read-only at the server side.
    /// </summary>
    public static void EnsureReadOnly(string sql)
    {
        if (ForbiddenKeywords().IsMatch(sql))
        {
            throw new InvalidOperationException(
                "Refusing to execute SQL containing a write keyword. This process is read-only.");
        }
    }
}
