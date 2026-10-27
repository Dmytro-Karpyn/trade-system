using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trade.Application;
using Trade.Application.Authentication;
using Trade.Infrastructure.Authentication;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class AuthenticationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Valid_login_uses_Identity_hash_HttpOnly_cookie_and_updates_LastLoginAt()
    {
        await using var api = await fixture.IdentityApiAsync();
        var response = await api.Client.LoginAsync("ADMIN");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), x => x.StartsWith("trade.auth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
        var user = await api.QueryAsync(db => db.Users.SingleAsync());
        Assert.NotNull(user.LastLoginAt); Assert.NotEqual(AuthenticationTestSupport.Password, user.PasswordHash);
        await api.WithServicesAsync(async services => Assert.True(await services.GetRequiredService<UserManager<TradeUser>>().CheckPasswordAsync(user, AuthenticationTestSupport.Password)));
    }

    [Theory]
    [InlineData("admin", "wrong-password")]
    [InlineData("missing", "Test-only.Password2026!")]
    [InlineData("", "")]
    public async Task Invalid_credentials_return_401_without_a_session(string name, string password)
    {
        await using var api = await fixture.IdentityApiAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.LoginAsync(name, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.GetAsync("/auth/me")).StatusCode);
        Assert.Null(await api.QueryAsync(db => db.Users.Select(x => x.LastLoginAt).SingleAsync()));
    }

    [Fact]
    public async Task Repeated_invalid_passwords_lock_the_account_using_Identity()
    {
        await using var api = await fixture.IdentityApiAsync();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.LoginAsync(password: "wrong-password")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.LoginAsync()).StatusCode);
        Assert.True(await api.QueryAsync(db => db.Users.AnyAsync(x => x.LockoutEnd > DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task Inactive_user_cannot_login_and_deactivation_revokes_existing_session()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var inactive = await api.AddUserAsync(active: false);
        using var client = api.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(inactive.Username)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.PostAsync($"/admin/users/{inactive.UserId}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.LoginAsync(inactive.Username)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.PostAsync($"/admin/users/{inactive.UserId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_invalidates_browser_session_and_previously_captured_cookie()
    {
        await using var api = await fixture.IdentityApiAsync();
        var login = await api.Client.LoginAsync();
        var captured = Assert.Single(login.Headers.GetValues("Set-Cookie"), x => x.StartsWith("trade.auth=")).Split(';')[0];
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.GetAsync("/auth/me")).StatusCode);
        using var replay = api.NewClient(); replay.DefaultRequestHeaders.Add("Cookie", captured);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Me_returns_active_roles_union_permissions_without_storing_effective_permissions()
    {
        await using var api = await fixture.IdentityApiAsync(); var me = await api.LoginAdminAsync();
        Assert.Equal("admin", me.Username); Assert.Equal("Demo Administrator", me.FullName);
        Assert.Equal(DevelopmentIdentitySeed.DemoOrganizationId, me.OrganizationId);
        Assert.Equal(["OWNER"], me.Roles); Assert.Equal(PermissionCatalog.Codes.Order(), me.Permissions.Order());
        var columns = await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'users'
            """).ToArrayAsync());
        Assert.DoesNotContain(columns, x => x.Contains("Permission", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, x => x is "Email" or "TwoFactorEnabled" or "Balance" or "Debt");
    }

    [Theory]
    [InlineData("/products")]
    [InlineData("/stock/balances")]
    [InlineData("/settlements/balances")]
    [InlineData("/admin/users")]
    [InlineData("/admin/roles")]
    [InlineData("/write-offs/costs")]
    public async Task Unauthenticated_protected_API_returns_401_even_with_forged_context_headers(string path)
    {
        await using var api = await fixture.IdentityApiAsync();
        api.Client.DefaultRequestHeaders.Add("X-Actor-Id", Guid.NewGuid().ToString());
        api.Client.DefaultRequestHeaders.Add("X-Organization-Id", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Login_and_authenticated_mutations_require_a_valid_CSRF_token()
    {
        await using var api = await fixture.IdentityApiAsync();
        var body = new LoginRequest("admin", AuthenticationTestSupport.Password);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/auth/login", body)).StatusCode);
        await api.Client.CsrfAsync(); api.Client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); api.Client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "forged");
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/auth/login", body)).StatusCode);
        await api.LoginAdminAsync(); api.Client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/products", api.ProductInput(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.Products.ToArrayAsync()));
        await api.Client.CsrfAsync(); Assert.Equal(HttpStatusCode.Created, (await api.Client.PostAsJsonAsync("/products", api.ProductInput(), ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Identity_password_policy_rejects_weak_passwords_without_partial_user_or_role_links()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var role = await api.AddRoleAsync(["product.view"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/admin/users", new CreateUserRequest("weak", "short", RoleIds: [role.Id]), ApiTestHost.Json)).StatusCode);
        Assert.Equal(1, await api.QueryAsync(db => db.Users.CountAsync()));
        Assert.Equal(1, await api.QueryAsync(db => db.UserRoles.CountAsync()));
    }

    [Fact]
    public async Task Password_change_revokes_sessions_and_user_never_exposes_password_hash()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var user = await api.AddUserAsync(); using var client = api.NewClient(); await client.LoginAsync(user.Username);
        var update = new UpdateUserRequest(user.Username, "Updated employee", Password: "New-test.Password2026!");
        var response = await api.Client.PutAsJsonAsync($"/admin/users/{user.UserId}", update, ApiTestHost.Json);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(user.Username)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.LoginAsync(user.Username, "New-test.Password2026!")).StatusCode);
    }

    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Development", false, false)]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    public async Task Seed_is_opt_in_Development_only_and_admin_is_usable(string environment, bool configured, bool expected)
    {
        await using var api = await fixture.IdentityApiAsync(environment, configured);
        Assert.Equal(expected ? 1 : 0, await api.QueryAsync(db => db.Users.CountAsync()));
        Assert.Equal(expected ? 7 : 0, await api.QueryAsync(db => db.Roles.CountAsync()));
        Assert.Equal(34, await api.QueryAsync(db => db.Permissions.CountAsync()));
        Assert.Equal(expected ? HttpStatusCode.NoContent : HttpStatusCode.Unauthorized, (await api.Client.LoginAsync()).StatusCode);
    }

    [Fact]
    public async Task Production_auth_and_CSRF_cookies_are_secure_HttpOnly_and_SameSite_Lax()
    {
        await using var api = await fixture.IdentityApiAsync("Production");
        await api.SeedUserAsync(Guid.NewGuid(), "production-test-user");
        var csrf = await api.Client.GetAsync("/auth/csrf");
        var csrfCookie = Assert.Single(csrf.Headers.GetValues("Set-Cookie"), x => x.StartsWith("trade.csrf="));
        Assert.Contains("secure", csrfCookie); Assert.Contains("httponly", csrfCookie); Assert.Contains("samesite=lax", csrfCookie);
        var login = await api.Client.LoginAsync("production-test-user"); Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var auth = Assert.Single(login.Headers.GetValues("Set-Cookie"), x => x.StartsWith("trade.auth="));
        Assert.Contains("secure", auth); Assert.Contains("httponly", auth); Assert.Contains("samesite=lax", auth);
    }
}
