namespace Nieweb.Api.Auth;

/// <summary>
/// Site security master switch, bound from the <c>Nieweb:Security</c>
/// configuration section.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="RelaxedLogin"/> is <c>true</c> the login stack is
/// deliberately loosened for early-adoption / on-prem pilots:
/// </para>
/// <list type="bullet">
///   <item><description>Password complexity is minimised (length 1, no digit / case / symbol requirement) and account lockout is disabled — applied in <c>AddNiewebIdentity</c>.</description></item>
///   <item><description>Forced password rotation is bypassed — the login / whoami responses report <c>MustRotatePassword = false</c> even when the flag is set on the account.</description></item>
/// </list>
/// <para>
/// The strict machinery is left fully in place (validators, the
/// <c>MustRotatePassword</c> column, lockout options) so flipping this
/// flag back to <c>false</c> restores the hardened behaviour with no
/// code change. Login accepts an email <b>or</b> a username regardless
/// of this flag.
/// </para>
/// </remarks>
public sealed class SecurityOptions
{
    /// <summary>Configuration section that binds to this options type.</summary>
    public const string SectionName = "Nieweb:Security";

    /// <summary>
    /// When <c>true</c>, relax password rules + lockout and bypass forced
    /// password rotation. Defaults to <c>false</c> (hardened).
    /// </summary>
    public bool RelaxedLogin { get; set; }
}
