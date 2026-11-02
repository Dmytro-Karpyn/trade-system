using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trade.Application;
using Trade.Application.Authentication;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class AdministrationAuditTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Users_roles_lookups_and_metadata_support_multiple_roles_and_no_physical_delete()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var a = await api.AddRoleAsync(["product.view"]); var b = await api.AddRoleAsync(["stock.view"]);
        var employee = await api.CreateCounterpartyAsync(new CounterpartyRequest("Employee", IsEmployee: true));
        var user = await api.AddUserAsync([a.Id, b.Id], counterpartyId: employee.Id);
        Assert.Equal(new[] { a.Name, b.Name }.Order(), user.RoleNames!.Order()); Assert.Equal("Employee", user.CounterpartyName);
        var roles = await api.ReadAsync<RoleResponse[]>(await api.Client.GetAsync("/admin/roles"));
        Assert.Equal(1, roles.Single(x => x.Id == a.Id).UserCount);
        var lookups = await api.ReadAsync<UserLookups>(await api.Client.GetAsync("/admin/users/lookups"));
        Assert.Contains(lookups.Counterparties, x => x.Id == employee.Id && x.IsEmployee);
        Assert.Contains(lookups.Roles, x => x.Id == b.Id);
        Assert.Equal(PermissionCatalog.All.Count, (await api.ReadAsync<PermissionDefinition[]>(await api.Client.GetAsync("/admin/permissions"))).Length);
        var inactive = await api.ReadAsync<UserResponse>(await api.Client.PostAsync($"/admin/users/{user.UserId}/deactivate", null)); Assert.False(inactive.Active);
        Assert.True((await api.ReadAsync<UserResponse>(await api.Client.PostAsync($"/admin/users/{user.UserId}/activate", null))).Active);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await api.Client.DeleteAsync($"/admin/users/{user.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await api.Client.DeleteAsync($"/admin/roles/{a.Id}")).StatusCode);
    }

    [Fact]
    public async Task Inactive_role_cannot_receive_permission_changes_until_reactivated()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var role = await api.AddRoleAsync(["product.view"], active: false);
        var rejected = await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(["sale.view"]), ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Assert.Contains("ROLE_INACTIVE", await rejected.Content.ReadAsStringAsync());
        Assert.Equal(["product.view"], (await api.ReadAsync<RoleResponse>(await api.Client.GetAsync($"/admin/roles/{role.Id}"))).Permissions);
        await api.ReadAsync<RoleResponse>(await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}", new RoleRequest(role.Name, true), ApiTestHost.Json));
        Assert.Equal(["sale.view"], (await api.ReadAsync<RoleResponse>(await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(["sale.view"]), ApiTestHost.Json))).Permissions);
    }

    [Fact]
    public async Task Last_admin_cannot_lose_access_by_user_role_or_permission_changes_and_rejections_are_atomic()
    {
        await using var api = await fixture.IdentityApiAsync(); var me = await api.LoginAdminAsync();
        var admin = await api.ReadAsync<UserResponse>(await api.Client.GetAsync($"/admin/users/{me.UserId}"));
        var role = await api.ReadAsync<RoleResponse>(await api.Client.GetAsync($"/admin/roles/{admin.RoleIds.Single()}"));
        var count = await api.QueryAsync(db => db.AuditEntries.CountAsync());
        var responses = new[]
        {
            await api.Client.PostAsync($"/admin/users/{me.UserId}/deactivate", null),
            await api.Client.PutAsJsonAsync($"/admin/users/{me.UserId}", new UpdateUserRequest(admin.Username, "Should rollback", RoleIds: []), ApiTestHost.Json),
            await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}", new RoleRequest(role.Name, false), ApiTestHost.Json),
            await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(["product.view"]), ApiTestHost.Json)
        };
        foreach (var response in responses) { Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("LAST_ADMIN_REQUIRED", await response.Content.ReadAsStringAsync()); }
        var after = await api.ReadAsync<UserResponse>(await api.Client.GetAsync($"/admin/users/{me.UserId}"));
        Assert.True(after.Active); Assert.Equal(admin.FullName, after.FullName); Assert.Equal(admin.RoleIds, after.RoleIds);
        Assert.Equal(role.Permissions, (await api.ReadAsync<RoleResponse>(await api.Client.GetAsync($"/admin/roles/{role.Id}"))).Permissions);
        Assert.Equal(count, await api.QueryAsync(db => db.AuditEntries.CountAsync()));
    }

    [Fact]
    public async Task Concurrent_deactivation_of_two_administrators_preserves_one_active_administrator()
    {
        await using var api = await fixture.IdentityApiAsync(); var me = await api.LoginAdminAsync();
        var role = await api.AddRoleAsync(["users.manage"]); var second = await api.AddUserAsync([role.Id]);
        var results = await Task.WhenAll(api.Client.PostAsync($"/admin/users/{me.UserId}/deactivate", null), api.Client.PostAsync($"/admin/users/{second.UserId}/deactivate", null));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, await api.QueryAsync(db => db.Users.CountAsync(x => x.Active && (x.Id == me.UserId || x.Id == second.UserId))));
    }

    [Fact]
    public async Task Audit_captures_business_old_new_actor_document_and_filters_without_secrets()
    {
        await using var api = await fixture.IdentityApiAsync(); var me = await api.LoginAdminAsync();
        var product = await api.CreateProductAsync();
        await api.ReadAsync<ProductResponse>(await api.Client.PutAsJsonAsync($"/products/{product.Id}", api.ProductInput() with { Name = "Змінена назва", Code = product.Code, UnitId = product.UnitId }, ApiTestHost.Json));
        var page = await api.ReadAsync<AuditPage>(await api.Client.GetAsync($"/admin/audit?entityType=Product&eventType=UPDATE&userId={me.UserId}&search={product.Id}"));
        var row = Assert.Single(page.Items); Assert.Equal(me.UserId, row.ActorId); Assert.NotNull(row.Username); Assert.Equal("Змінена назва", row.Summary);
        var detail = await api.ReadAsync<AuditDetail>(await api.Client.GetAsync($"/admin/audit/{row.Id}"));
        Assert.Equal(product.Name, ((JsonElement)detail.OldValues!).GetProperty("Name").GetString());
        Assert.Equal("Змінена назва", ((JsonElement)detail.NewValues!).GetProperty("Name").GetString());
        var purchase = await api.CreatePurchaseAsync(product.Id); await api.PostAsync(purchase.Document.Id);
        var posts = await api.ReadAsync<AuditPage>(await api.Client.GetAsync($"/admin/audit?documentId={purchase.Document.Id}&eventType=POST"));
        Assert.Equal(purchase.Document.Id, Assert.Single(posts.Items).DocumentId); Assert.NotNull(posts.Items[0].DocumentNumber);
        var user = await api.AddUserAsync();
        await api.ReadAsync<UserResponse>(await api.Client.PutAsJsonAsync($"/admin/users/{user.UserId}", new UpdateUserRequest(user.Username, "Edited", Password: "Another-test.Password2026!"), ApiTestHost.Json));
        var auditRows = await api.QueryAsync(db => db.AuditEntries.Select(x => new { x.OldValues, x.NewValues }).ToArrayAsync());
        var auditText = string.Join("\n", auditRows.Select(x => (x.OldValues ?? "") + (x.NewValues ?? "")));
        foreach (var secret in new[] { "Password", "SecurityStamp", "ConcurrencyStamp", AuthenticationTestSupport.Password, "Another-test.Password2026!" })
            Assert.DoesNotContain(secret, auditText, StringComparison.OrdinalIgnoreCase);
        var lookups = await api.ReadAsync<AuditLookups>(await api.Client.GetAsync("/admin/audit/lookups"));
        Assert.Contains("Product", lookups.EntityTypes); Assert.Contains(lookups.Documents, x => x.Id == purchase.Document.Id);
        Assert.Empty((await api.ReadAsync<AuditPage>(await api.Client.GetAsync("/admin/audit?from=2099-01-01T00:00:00Z"))).Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/admin/audit?from=2099-01-01T00:00:00Z&to=2000-01-01T00:00:00Z")).StatusCode);
    }

    [Fact]
    public async Task Audit_is_immutable_and_tenant_scoped_and_permission_enforced()
    {
        await using var api = await fixture.IdentityApiAsync(); var me = await api.LoginAdminAsync();
        await api.CreateProductAsync(); var row = Assert.Single((await api.ReadAsync<AuditPage>(await api.Client.GetAsync("/admin/audit?entityType=Product"))).Items);
        var (_, limited, _) = await api.LimitedUserAsync("product.view"); using var client = limited;
        foreach (var path in new[] { "/admin/audit", "/admin/audit/lookups", $"/admin/audit/{row.Id}", "/admin/users", "/admin/roles", "/admin/permissions" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/categories", new CategoryRequest("Denied"), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/categories")).StatusCode);
        var org = Guid.NewGuid(); Guid foreign = Guid.Empty;
        await api.WithDbAsync(async db => { var entry = Trade.Infrastructure.Persistence.AuditEntry.Create(org, null, "CREATE", "Product", Guid.NewGuid().ToString(), null, "Foreign", null, new { Name = "Secret foreign" }); foreign = entry.Id; db.AuditEntries.Add(entry); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/admin/audit/{foreign}")).StatusCode);
        Assert.DoesNotContain((await api.ReadAsync<AuditPage>(await api.Client.GetAsync("/admin/audit"))).Items, x => x.Id == foreign);
        await Assert.ThrowsAsync<PostgresException>(() => api.WithDbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_logs WHERE \"Id\"={row.Id}")));
        await Assert.ThrowsAsync<PostgresException>(() => api.WithDbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit_logs SET \"Summary\"='tampered' WHERE \"Id\"={row.Id}")));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await api.Client.DeleteAsync($"/admin/audit/{row.Id}")).StatusCode);
        Assert.Equal(me.OrganizationId, await api.QueryAsync(db => db.AuditEntries.Where(x => x.Id == row.Id).Select(x => x.OrganizationId).SingleAsync()));
    }
}
