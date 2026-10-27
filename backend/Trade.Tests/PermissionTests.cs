using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Application.Authentication;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class PermissionTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Active_roles_union_permissions_and_inactive_roles_grant_nothing()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var view = await api.AddRoleAsync(["product.view"]);
        var create = await api.AddRoleAsync(["product.create"]);
        var inactive = await api.AddRoleAsync(["product.edit"], active: false);
        var user = await api.AddUserAsync([view.Id, create.Id, inactive.Id]);
        using var client = api.NewClient(); await client.LoginAsync(user.Username);
        var me = await api.ReadAsync<CurrentUserResponse>(await client.GetAsync("/auth/me"));
        Assert.Equal(new[] { "product.create", "product.view" }, me.Permissions.Order());
        Assert.Equal(new[] { create.Name, view.Name }.Order(), me.Roles.Order());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/products")).StatusCode);
        var product = await api.ReadAsync<ProductResponse>(await client.PostAsJsonAsync("/products", api.ProductInput(), ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/products/{product.Id}", api.ProductInput(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/stock/balances")).StatusCode);
    }

    [Fact]
    public async Task Permission_and_role_activation_changes_take_effect_on_existing_cookie()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var (_, client, role) = await api.LimitedUserAsync(); using var session = client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(["product.view"]), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}", new RoleRequest(role.Name, false), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/products")).StatusCode);
        var me = await api.ReadAsync<CurrentUserResponse>(await client.GetAsync("/auth/me")); Assert.Empty(me.Permissions); Assert.Empty(me.Roles);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}", new RoleRequest(role.Name, true), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest([]), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/products")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/products", "product.view", 200)]
    [InlineData("POST", "/products", "product.create", 400)]
    [InlineData("PUT", "/products/00000000-0000-0000-0000-000000000123", "product.edit", 400)]
    [InlineData("GET", "/purchases/00000000-0000-0000-0000-000000000123", "purchase.view", 404)]
    [InlineData("POST", "/purchases", "purchase.create", 400)]
    [InlineData("POST", "/purchases/00000000-0000-0000-0000-000000000123/post", "purchase.post", 404)]
    [InlineData("POST", "/purchases/00000000-0000-0000-0000-000000000123/unpost", "purchase.unpost", 404)]
    [InlineData("GET", "/sales/00000000-0000-0000-0000-000000000123", "sale.view", 404)]
    [InlineData("POST", "/sales", "sale.create", 400)]
    [InlineData("POST", "/sales/00000000-0000-0000-0000-000000000123/post", "sale.post", 404)]
    [InlineData("POST", "/sales/00000000-0000-0000-0000-000000000123/unpost", "sale.unpost", 404)]
    [InlineData("DELETE", "/sales/00000000-0000-0000-0000-000000000123", "sale.delete", 404)]
    [InlineData("GET", "/stock/balances", "stock.view", 200)]
    [InlineData("GET", "/stock-transfers", "stock.transfer", 200)]
    [InlineData("GET", "/write-offs", "stock.writeoff", 200)]
    [InlineData("GET", "/money/balances", "money.view", 200)]
    [InlineData("POST", "/money/receipts/00000000-0000-0000-0000-000000000123/post", "money.receipt", 404)]
    [InlineData("POST", "/money/payments/00000000-0000-0000-0000-000000000123/post", "money.payment", 404)]
    [InlineData("POST", "/money/transfers/00000000-0000-0000-0000-000000000123/post", "money.transfer", 404)]
    [InlineData("GET", "/expenses", "expense.view", 200)]
    [InlineData("POST", "/expenses", "expense.create", 400)]
    [InlineData("POST", "/expenses/00000000-0000-0000-0000-000000000123/post", "expense.post", 404)]
    [InlineData("POST", "/expenses/00000000-0000-0000-0000-000000000123/unpost", "expense.unpost", 404)]
    [InlineData("DELETE", "/expenses/00000000-0000-0000-0000-000000000123", "expense.delete", 404)]
    [InlineData("GET", "/settlements/balances", "settlement.view", 200)]
    [InlineData("GET", "/admin/users", "users.manage", 200)]
    [InlineData("GET", "/admin/roles", "roles.manage", 200)]
    [InlineData("GET", "/write-offs/costs", "cost.view", 200)]
    [InlineData("GET", "/supplier-returns", "purchase.view", 200)]
    [InlineData("GET", "/customer-returns", "sale.view", 200)]
    public async Task Endpoint_requires_its_permission_and_grants_access_with_that_permission(string method, string path, string permission, int grantedStatus)
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var (_, client, role) = await api.LimitedUserAsync(); using var session = client;
        using var denied = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await api.ReadAsync<RoleResponse>(await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest([permission]), ApiTestHost.Json));
        using var allowed = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal((HttpStatusCode)grantedStatus, allowed.StatusCode);
    }

    [Fact]
    public async Task Unknown_permission_is_rejected_and_role_names_are_unique_per_organization()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var role = await api.AddRoleAsync(["product.view"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(["invented.permission"]), ApiTestHost.Json)).StatusCode);
        var current = await api.ReadAsync<RoleResponse>(await api.Client.GetAsync($"/admin/roles/{role.Id}"));
        Assert.Equal(["product.view"], current.Permissions);
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PostAsJsonAsync("/admin/roles", new RoleRequest(role.Name.ToLowerInvariant()), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/admin/roles", new RoleRequest(" "), ApiTestHost.Json)).StatusCode);
    }
}
