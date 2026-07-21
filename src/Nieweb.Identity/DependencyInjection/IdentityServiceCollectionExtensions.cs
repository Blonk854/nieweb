using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Nieweb.Data;
using Nieweb.Data.Entities;
using Nieweb.Identity.Passwords;

namespace Nieweb.Identity.DependencyInjection;

/// <summary>
/// DI helpers for wiring Nieweb's Identity stack (users, roles, Argon2id
/// password hasher) into an ASP.NET Core host.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers ASP.NET Core Identity for <see cref="NiewebUser"/> and
    /// <see cref="NiewebRole"/> against <see cref="NiewebDbContext"/>,
    /// with Argon2id password hashing (see
    /// <see cref="Argon2idPasswordHasher{TUser}"/>).
    /// </summary>
    /// <remarks>
    /// The following configuration sections are consumed (all optional
    /// - each falls back to Identity's built-in defaults):
    /// <list type="bullet">
    ///   <item><description><c>Nieweb:Identity:Password</c> -> <see cref="PasswordOptions"/></description></item>
    ///   <item><description><c>Nieweb:Identity:Lockout</c>  -> <see cref="LockoutOptions"/></description></item>
    ///   <item><description><c>Nieweb:Identity:User</c>     -> <see cref="UserOptions"/></description></item>
    ///   <item><description><c>Nieweb:Identity:Argon2id</c> -> <see cref="Argon2idOptions"/></description></item>
    /// </list>
    /// The caller must have already registered <see cref="NiewebDbContext"/>
    /// (typically via <c>AddDbContext&lt;NiewebDbContext&gt;</c>).
    /// </remarks>
    public static IServiceCollection AddNiewebIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<Argon2idOptions>(
            configuration.GetSection("Nieweb:Identity:Argon2id"));

        services
            .AddIdentityCore<NiewebUser>(options =>
            {
                configuration.GetSection("Nieweb:Identity:Password").Bind(options.Password);
                configuration.GetSection("Nieweb:Identity:Lockout").Bind(options.Lockout);
                configuration.GetSection("Nieweb:Identity:User").Bind(options.User);
                // Nieweb keys off email for unique login regardless of
                // whether the user is local or OIDC-provisioned.
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<NiewebRole>()
            .AddEntityFrameworkStores<NiewebDbContext>();

        // AddIdentityCore registers PasswordHasher<NiewebUser> as Scoped
        // via TryAddScoped; swap in the Argon2id implementation.
        services.Replace(ServiceDescriptor.Scoped<
            IPasswordHasher<NiewebUser>,
            Argon2idPasswordHasher<NiewebUser>>());

        return services;
    }
}
