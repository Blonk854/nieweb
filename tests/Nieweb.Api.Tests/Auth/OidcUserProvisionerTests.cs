using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;

using Nieweb.Api.Auth;
using Nieweb.Api.Startup;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests.Auth;

/// <summary>
/// Exercises the four <see cref="OidcUserProvisioner"/> branches
/// (found-by-login, found-by-email OIDC user, found-by-email LOCAL
/// user, and net-new provisioning) against the real
/// <see cref="UserManager{TUser}"/> registered by the API host. Uses
/// the shared in-memory SQLite database so we cover the actual
/// Identity + EF Core round-trip.
/// </summary>
public sealed class OidcUserProvisionerTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public OidcUserProvisionerTests(NiewebApiFactory factory)
    {
        _factory = factory;
        // Make sure the roles the provisioner needs are seeded (the
        // startup hook that normally does this only runs when the host
        // actually starts servicing requests; instantiating
        // NiewebApiFactory alone is enough because tests hit the API
        // pipeline elsewhere - but we guard defensively here in case
        // this fixture is the first thing to touch the shared DB).
        EnsureRolesAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureRolesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<NiewebRole>>();
        foreach (var name in new[]
        {
            BootstrapAdmin.RoleReader,
            BootstrapAdmin.RoleAuthor,
            BootstrapAdmin.RoleAdmin,
        })
        {
            if (!await roles.RoleExistsAsync(name))
            {
                await roles.CreateAsync(new NiewebRole
                {
                    Name = name,
                    NormalizedName = name.ToUpperInvariant(),
                });
            }
        }
    }

    private static OidcUserProvisioner CreateSut(IServiceScope scope) => new(
        scope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>(),
        scope.ServiceProvider.GetRequiredService<TimeProvider>());

    private static ClaimsPrincipal MakePrincipal(string subject, string email, string? name = null) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("name", name ?? email),
        }, authenticationType: "TestOidc"));

    [Fact]
    public async Task Missing_subject_claim_returns_MissingRequiredClaim()
    {
        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Email, "nosub@example.com"),
        }, authenticationType: "TestOidc"));

        var result = await sut.LookupOrProvisionAsync(principal, "oidc", BootstrapAdmin.RoleReader);

        Assert.Equal(OidcUserProvisioner.ProvisionOutcome.MissingRequiredClaim, result.Outcome);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task Missing_email_claim_returns_MissingRequiredClaim()
    {
        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "sub-no-email"),
        }, authenticationType: "TestOidc"));

        var result = await sut.LookupOrProvisionAsync(principal, "oidc", BootstrapAdmin.RoleReader);

        Assert.Equal(OidcUserProvisioner.ProvisionOutcome.MissingRequiredClaim, result.Outcome);
    }

    [Fact]
    public async Task Unknown_user_is_provisioned_with_default_role_and_login_binding()
    {
        var subject = "sub-new-" + Guid.NewGuid().ToString("N");
        var email = $"new-{Guid.NewGuid():N}@example.com";
        var principal = MakePrincipal(subject, email, "Alice New");

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();

        var result = await sut.LookupOrProvisionAsync(principal, "oidc", BootstrapAdmin.RoleReader);

        Assert.Equal(OidcUserProvisioner.ProvisionOutcome.Provisioned, result.Outcome);
        Assert.NotNull(result.User);
        Assert.Equal(email, result.User!.Email);
        Assert.True(result.User.IsOidcProvisioned);
        Assert.Equal("Alice New", result.User.DisplayName);
        Assert.False(result.User.MustRotatePassword);

        var roles = await users.GetRolesAsync(result.User);
        Assert.Contains(BootstrapAdmin.RoleReader, roles);

        // Login binding must exist so a subsequent sign-in short-circuits
        // via FindByLoginAsync.
        var rebound = await users.FindByLoginAsync("oidc", subject);
        Assert.NotNull(rebound);
        Assert.Equal(result.User.Id, rebound!.Id);
    }

    [Fact]
    public async Task Second_signin_finds_user_by_login_binding()
    {
        var subject = "sub-repeat-" + Guid.NewGuid().ToString("N");
        var email = $"repeat-{Guid.NewGuid():N}@example.com";

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var first = await sut.LookupOrProvisionAsync(
            MakePrincipal(subject, email), "oidc", BootstrapAdmin.RoleReader);
        Assert.Equal(OidcUserProvisioner.ProvisionOutcome.Provisioned, first.Outcome);

        var second = await sut.LookupOrProvisionAsync(
            MakePrincipal(subject, email), "oidc", BootstrapAdmin.RoleReader);

        Assert.Equal(OidcUserProvisioner.ProvisionOutcome.ExistingSignedIn, second.Outcome);
        Assert.Equal(first.User!.Id, second.User!.Id);
    }

    [Fact]
    public async Task Existing_OIDC_user_matched_by_email_attaches_new_login_binding()
    {
        var email = $"attach-{Guid.NewGuid():N}@example.com";

        // Seed an existing OIDC-provisioned user WITHOUT a login binding,
        // as if the previous IdP's registration was lost / migrated.
        using (var seedScope = _factory.Services.CreateScope())
        {
            var users = seedScope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();
            var user = new NiewebUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = email,
                IsOidcProvisioned = true,
                CreatedUtc = DateTime.UtcNow,
            };
            await users.CreateAsync(user);
            await users.AddToRoleAsync(user, BootstrapAdmin.RoleReader);
        }

        var subject = "sub-attach-" + Guid.NewGuid().ToString("N");
        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();

        var result = await sut.LookupOrProvisionAsync(
            MakePrincipal(subject, email), "oidc", BootstrapAdmin.RoleReader);

        Assert.Equal(OidcUserProvisioner.ProvisionOutcome.ExistingSignedIn, result.Outcome);
        Assert.NotNull(result.User);
        Assert.Equal(email, result.User!.Email);

        // Binding must now exist so future sign-ins go through the fast path.
        var bound = await manager.FindByLoginAsync("oidc", subject);
        Assert.NotNull(bound);
        Assert.Equal(result.User.Id, bound!.Id);
    }

    [Fact]
    public async Task Local_account_with_same_email_is_rejected_to_prevent_hijack()
    {
        var email = $"local-{Guid.NewGuid():N}@example.com";

        using (var seedScope = _factory.Services.CreateScope())
        {
            var users = seedScope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();
            await users.CreateAsync(new NiewebUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = email,
                IsOidcProvisioned = false, // <-- local account
                CreatedUtc = DateTime.UtcNow,
            }, "LocalPa$$w0rd!");
        }

        var subject = "sub-hijack-" + Guid.NewGuid().ToString("N");
        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var result = await sut.LookupOrProvisionAsync(
            MakePrincipal(subject, email), "oidc", BootstrapAdmin.RoleReader);

        Assert.Equal(OidcUserProvisioner.ProvisionOutcome.LocalAccountConflict, result.Outcome);
        Assert.Null(result.User);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task Disabled_user_is_refused()
    {
        var subject = "sub-disabled-" + Guid.NewGuid().ToString("N");
        var email = $"disabled-{Guid.NewGuid():N}@example.com";

        // First-time provisioning succeeds.
        using (var scope1 = _factory.Services.CreateScope())
        {
            var sut1 = CreateSut(scope1);
            var first = await sut1.LookupOrProvisionAsync(
                MakePrincipal(subject, email), "oidc", BootstrapAdmin.RoleReader);
            Assert.Equal(OidcUserProvisioner.ProvisionOutcome.Provisioned, first.Outcome);

            var users = scope1.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();
            first.User!.IsDisabled = true;
            var updateResult = await users.UpdateAsync(first.User);
            Assert.True(updateResult.Succeeded);
        }

        // Second sign-in must be refused.
        using var scope2 = _factory.Services.CreateScope();
        var sut2 = CreateSut(scope2);
        var second = await sut2.LookupOrProvisionAsync(
            MakePrincipal(subject, email), "oidc", BootstrapAdmin.RoleReader);

        Assert.Equal(OidcUserProvisioner.ProvisionOutcome.LocalAccountConflict, second.Outcome);
        Assert.Null(second.User);
    }
}
