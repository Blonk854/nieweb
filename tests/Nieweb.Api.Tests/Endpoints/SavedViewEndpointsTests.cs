using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Nieweb.Api.Endpoints;
using Nieweb.Data;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests;

public sealed class SavedViewEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public SavedViewEndpointsTests(NiewebApiFactory factory)
    {
        _factory = factory;
        EnsureDatabaseAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private async Task<NiewebUser> CreateUserAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }
        var user = new NiewebUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = email.Split('@')[0],
            CreatedUtc = DateTime.UtcNow,
        };
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded,
            "CreateAsync failed: " + string.Join("; ", result.Errors.Select(e => e.Code + " " + e.Description)));
        return user;
    }

    private async Task<HttpClient> LoggedInClientAsync(string email, string password)
    {
        _ = await CreateUserAsync(email, password);
        using var anonymous = _factory.CreateClient();
        var login = new AuthEndpoints.LoginRequest { Email = email, Password = password };
        using var loginResponse = await anonymous.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var payload = await loginResponse.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>();
        Assert.NotNull(payload);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return client;
    }

    private async Task ClearSavedViewsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        db.SavedViews.RemoveRange(db.SavedViews);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/saved-views", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_And_List_RoundTrip_ForOwner()
    {
        await ClearSavedViewsAsync();
        using var client = await LoggedInClientAsync("owner1@nieweb.test", "correctpassword123");

        var body = new SavedViewEndpoints.CreateSavedViewRequest
        {
            Name = "My yield today",
            ReportKey = "panel-yield",
            FilterJson = """{"sourceId":"postreflow","dateFromLocal":"2026-01-01"}""",
            IsShared = false,
        };
        using var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/saved-views", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<SavedViewEndpoints.SavedViewDto>();
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.True(created.IsOwner);
        Assert.False(created.IsShared);
        Assert.Equal("panel-yield", created.ReportKey);
        Assert.Equal("My yield today", created.Name);

        using var listResponse = await client.GetAsync(
            new Uri("/api/saved-views?reportKey=panel-yield", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<SavedViewEndpoints.SavedViewDto[]>();
        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal(created.Id, list![0].Id);
    }

    [Fact]
    public async Task Create_WithMalformedJson_Returns400()
    {
        using var client = await LoggedInClientAsync("badjson@nieweb.test", "correctpassword123");

        var body = new SavedViewEndpoints.CreateSavedViewRequest
        {
            Name = "Broken",
            ReportKey = "panel-yield",
            FilterJson = "{not-json",
            IsShared = false,
        };
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/saved-views", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ScopedToReportKey_ExcludesOtherReports()
    {
        await ClearSavedViewsAsync();
        using var client = await LoggedInClientAsync("scoped@nieweb.test", "correctpassword123");

        foreach (var (name, report) in new[]
        {
            ("Yield A", "panel-yield"),
            ("Defect B", "defect-pareto"),
        })
        {
            var body = new SavedViewEndpoints.CreateSavedViewRequest
            {
                Name = name,
                ReportKey = report,
                FilterJson = "{}",
            };
            using var r = await client.PostAsJsonAsync(new Uri("/api/saved-views", UriKind.Relative), body);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        }

        using var listResponse = await client.GetAsync(
            new Uri("/api/saved-views?reportKey=panel-yield", UriKind.Relative));
        var list = await listResponse.Content.ReadFromJsonAsync<SavedViewEndpoints.SavedViewDto[]>();
        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal("Yield A", list![0].Name);
    }

    [Fact]
    public async Task List_IncludesSharedViewsFromOtherUser_MarkedNotOwner()
    {
        await ClearSavedViewsAsync();

        using var owner = await LoggedInClientAsync("shareowner@nieweb.test", "correctpassword123");
        var shared = new SavedViewEndpoints.CreateSavedViewRequest
        {
            Name = "Shared view",
            ReportKey = "panel-yield",
            FilterJson = "{}",
            IsShared = true,
        };
        using var sharedCreate = await owner.PostAsJsonAsync(
            new Uri("/api/saved-views", UriKind.Relative), shared);
        Assert.Equal(HttpStatusCode.Created, sharedCreate.StatusCode);

        var privateBody = new SavedViewEndpoints.CreateSavedViewRequest
        {
            Name = "Private view",
            ReportKey = "panel-yield",
            FilterJson = "{}",
            IsShared = false,
        };
        using var privateCreate = await owner.PostAsJsonAsync(
            new Uri("/api/saved-views", UriKind.Relative), privateBody);
        Assert.Equal(HttpStatusCode.Created, privateCreate.StatusCode);

        using var other = await LoggedInClientAsync("shareguest@nieweb.test", "correctpassword123");
        using var listResponse = await other.GetAsync(
            new Uri("/api/saved-views?reportKey=panel-yield", UriKind.Relative));
        var list = await listResponse.Content.ReadFromJsonAsync<SavedViewEndpoints.SavedViewDto[]>();
        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal("Shared view", list![0].Name);
        Assert.False(list[0].IsOwner);
        Assert.True(list[0].IsShared);
    }

    [Fact]
    public async Task Update_ByNonOwner_Returns403()
    {
        await ClearSavedViewsAsync();
        using var owner = await LoggedInClientAsync("upowner@nieweb.test", "correctpassword123");
        var body = new SavedViewEndpoints.CreateSavedViewRequest
        {
            Name = "Owned",
            ReportKey = "panel-yield",
            FilterJson = "{}",
            IsShared = true,
        };
        using var createResponse = await owner.PostAsJsonAsync(
            new Uri("/api/saved-views", UriKind.Relative), body);
        var created = await createResponse.Content.ReadFromJsonAsync<SavedViewEndpoints.SavedViewDto>();
        Assert.NotNull(created);

        using var stranger = await LoggedInClientAsync("upstranger@nieweb.test", "correctpassword123");
        var update = new SavedViewEndpoints.UpdateSavedViewRequest
        {
            Name = "Hacked",
            FilterJson = "{}",
            IsShared = false,
        };
        using var updateResponse = await stranger.PutAsJsonAsync(
            new Uri($"/api/saved-views/{created!.Id}", UriKind.Relative), update);
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Update_ByOwner_ReplacesFields_AndBumpsLastModified()
    {
        await ClearSavedViewsAsync();
        using var owner = await LoggedInClientAsync("upowner2@nieweb.test", "correctpassword123");
        var body = new SavedViewEndpoints.CreateSavedViewRequest
        {
            Name = "V1",
            ReportKey = "panel-yield",
            FilterJson = """{"a":1}""",
            IsShared = false,
        };
        using var createResponse = await owner.PostAsJsonAsync(
            new Uri("/api/saved-views", UriKind.Relative), body);
        var created = await createResponse.Content.ReadFromJsonAsync<SavedViewEndpoints.SavedViewDto>();
        Assert.NotNull(created);

        // Ensure at least one wall-clock tick between create and update.
        await Task.Delay(15);

        var update = new SavedViewEndpoints.UpdateSavedViewRequest
        {
            Name = "V2",
            FilterJson = """{"a":2}""",
            IsShared = true,
        };
        using var updateResponse = await owner.PutAsJsonAsync(
            new Uri($"/api/saved-views/{created!.Id}", UriKind.Relative), update);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<SavedViewEndpoints.SavedViewDto>();
        Assert.NotNull(updated);
        Assert.Equal("V2", updated!.Name);
        Assert.Equal("""{"a":2}""", updated.FilterJson);
        Assert.True(updated.IsShared);
        Assert.True(updated.LastModifiedUtc >= created.LastModifiedUtc);
    }

    [Fact]
    public async Task Delete_ByNonOwner_Returns403_AndRowStillPresent()
    {
        await ClearSavedViewsAsync();
        using var owner = await LoggedInClientAsync("delowner@nieweb.test", "correctpassword123");
        var body = new SavedViewEndpoints.CreateSavedViewRequest
        {
            Name = "Keep",
            ReportKey = "panel-yield",
            FilterJson = "{}",
            IsShared = true,
        };
        using var createResponse = await owner.PostAsJsonAsync(
            new Uri("/api/saved-views", UriKind.Relative), body);
        var created = await createResponse.Content.ReadFromJsonAsync<SavedViewEndpoints.SavedViewDto>();
        Assert.NotNull(created);

        using var stranger = await LoggedInClientAsync("delstranger@nieweb.test", "correctpassword123");
        using var deleteResponse = await stranger.DeleteAsync(
            new Uri($"/api/saved-views/{created!.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.True(await db.SavedViews.AnyAsync(v => v.Id == created.Id));
    }

    [Fact]
    public async Task Delete_ByOwner_Returns204_AndRowGone()
    {
        await ClearSavedViewsAsync();
        using var owner = await LoggedInClientAsync("delowner2@nieweb.test", "correctpassword123");
        var body = new SavedViewEndpoints.CreateSavedViewRequest
        {
            Name = "Bye",
            ReportKey = "panel-yield",
            FilterJson = "{}",
        };
        using var createResponse = await owner.PostAsJsonAsync(
            new Uri("/api/saved-views", UriKind.Relative), body);
        var created = await createResponse.Content.ReadFromJsonAsync<SavedViewEndpoints.SavedViewDto>();
        Assert.NotNull(created);

        using var deleteResponse = await owner.DeleteAsync(
            new Uri($"/api/saved-views/{created!.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.False(await db.SavedViews.AnyAsync(v => v.Id == created.Id));
    }

    [Fact]
    public async Task Delete_UnknownId_Returns404()
    {
        using var client = await LoggedInClientAsync("del404@nieweb.test", "correctpassword123");
        using var response = await client.DeleteAsync(
            new Uri("/api/saved-views/999999", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
