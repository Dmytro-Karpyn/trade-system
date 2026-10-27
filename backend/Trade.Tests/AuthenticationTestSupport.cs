using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Trade.Application.Authentication;
using Trade.Infrastructure.Authentication;
using Xunit;

namespace Trade.Tests;

internal static class AuthenticationTestSupport
{
    // Test-only credentials for isolated Testcontainers databases. No application password default exists.
    public const string Password = "Test-only.Password2026!";
    public static Task<ApiTestHost> IdentityApiAsync(this DatabaseFixture fixture, string environment = "Development", bool seed = true) =>
        fixture.CreateApiAsync(environment, testAuthentication: false, settings: new Dictionary<string, string?>
        {
            ["DevelopmentSeed:AdminPassword"] = seed ? Password : null
        });

    public static async Task CsrfAsync(this HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var token = await client.GetFromJsonAsync<CsrfResponse>("/auth/csrf");
        client.DefaultRequestHeaders.Add(token!.HeaderName, token.RequestToken);
    }
    public static async Task<HttpResponseMessage> LoginAsync(this HttpClient client, string name = "admin", string password = Password)
    {
        await client.CsrfAsync();
        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(name, password), ApiTestHost.Json);
        if (response.IsSuccessStatusCode) await client.CsrfAsync();
        return response;
    }
    public static async Task<CurrentUserResponse> LoginAdminAsync(this ApiTestHost api)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.LoginAsync()).StatusCode);
        var me = await api.ReadAsync<CurrentUserResponse>(await api.Client.GetAsync("/auth/me"));
        api.UseIdentity(me.OrganizationId, me.UserId);
        return me;
    }
    public static async Task<RoleResponse> AddRoleAsync(this ApiTestHost api, IReadOnlyList<string> codes, bool active = true)
    {
        var role = await api.ReadAsync<RoleResponse>(await api.Client.PostAsJsonAsync("/admin/roles", new RoleRequest($"ROLE_{Guid.NewGuid():N}", active), ApiTestHost.Json), HttpStatusCode.Created);
        return await api.ReadAsync<RoleResponse>(await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(codes), ApiTestHost.Json));
    }
    public static async Task<UserResponse> AddUserAsync(this ApiTestHost api, IReadOnlyList<Guid>? roles = null, bool active = true, Guid? counterpartyId = null)
        => await api.ReadAsync<UserResponse>(await api.Client.PostAsJsonAsync("/admin/users", new CreateUserRequest($"user_{Guid.NewGuid():N}", Password,
            "Test Employee", counterpartyId, active, roles), ApiTestHost.Json), HttpStatusCode.Created);
    public static async Task<(UserResponse User, HttpClient Client, RoleResponse Role)> LimitedUserAsync(this ApiTestHost api, params string[] permissions)
    {
        var role = await api.AddRoleAsync(permissions);
        var user = await api.AddUserAsync([role.Id]);
        var client = api.NewClient();
        Assert.Equal(HttpStatusCode.NoContent, (await client.LoginAsync(user.Username)).StatusCode);
        return (user, client, role);
    }
    public static async Task SeedUserAsync(this ApiTestHost api, Guid org, string username, string password = Password)
    {
        await api.WithServicesAsync(async services =>
        {
            var manager = services.GetRequiredService<UserManager<TradeUser>>();
            IdentityAdministration.RequireSuccess(await manager.CreateAsync(new TradeUser { OrganizationId = org, UserName = username }, password));
        });
    }
    private sealed record CsrfResponse(string RequestToken, string HeaderName);
}
