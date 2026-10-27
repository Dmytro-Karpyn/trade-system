using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trade.Application;
using Trade.Application.Authentication;
using Trade.Domain.Catalog;
using Trade.Infrastructure.Authentication;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class IdentityIsolationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Organization_and_actor_come_only_from_cookie_despite_forged_headers_and_body_fields()
    {
        await using var api = await fixture.IdentityApiAsync(); var me = await api.LoginAdminAsync();
        api.Client.DefaultRequestHeaders.Add("X-Organization-Id", Guid.NewGuid().ToString());
        api.Client.DefaultRequestHeaders.Add("X-Actor-Id", Guid.NewGuid().ToString());
        var product = await api.CreateProductAsync(); Assert.Equal(me.OrganizationId, product.OrganizationId);
        await api.InitializeCounterpartyAsync();
        var stock = await api.ReceiveAsync(); Assert.Equal(me.UserId, stock.Purchase.Document.CreatedBy);
        var posted = await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{stock.Purchase.Document.Id}"));
        Assert.Equal(me.UserId, posted.Document.PostedBy);
        var sale = await api.CreateSaleAsync(stock);
        Assert.Equal(me.UserId, sale.Document.CreatedBy);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/sales/{sale.Document.Id}")).StatusCode);
        Assert.Equal(me.UserId, await api.QueryAsync(db => db.Documents.Where(x => x.Id == sale.Document.Id).Select(x => x.DeletedBy).SingleAsync()));
        Assert.Equal(me.OrganizationId, (await api.ReadAsync<CurrentUserResponse>(await api.Client.GetAsync("/auth/me"))).OrganizationId);
        var forged = new { Username = "bodyorg", Password = AuthenticationTestSupport.Password, OrganizationId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var user = await api.ReadAsync<UserResponse>(await api.Client.PostAsJsonAsync("/admin/users", forged), HttpStatusCode.Created);
        Assert.Equal(me.OrganizationId, user.OrganizationId);
    }

    [Fact]
    public async Task Foreign_organization_users_roles_and_business_data_are_inaccessible()
    {
        await using var api = await fixture.IdentityApiAsync(); var me = await api.LoginAdminAsync();
        var foreignOrg = Guid.NewGuid(); await api.SeedUserAsync(foreignOrg, "foreign-user");
        Guid foreignRole = Guid.Empty; Guid foreignProduct = Guid.Empty; Guid foreignCounterparty = Guid.Empty;
        await api.WithDbAsync(async db =>
        {
            var role = new TradeRole { OrganizationId = foreignOrg, Name = "OWNER", NormalizedName = "OWNER" }; foreignRole = role.Id; db.Roles.Add(role);
            var product = Trade.Domain.Catalog.Product.Create(foreignOrg, new(null, Guid.NewGuid(), ItemType.GOODS, "FOREIGN", null, "Foreign product", null, null, true)); foreignProduct = product.Id; db.Products.Add(product);
            var party = Trade.Domain.Counterparties.Counterparty.Create(foreignOrg, new("Foreign", null, null, null, null, null, null, null, false, false, false, false, true, null)); foreignCounterparty = party.Id; db.Counterparties.Add(party);
            await db.SaveChangesAsync();
        });
        var foreignUser = await api.QueryAsync(db => db.Users.Where(x => x.OrganizationId == foreignOrg).Select(x => x.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/products/{foreignProduct}")).StatusCode);
        Assert.Empty(await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/admin/users/{foreignUser}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/admin/users/{foreignUser}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/admin/roles/{foreignRole}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/admin/roles/{foreignRole}/permissions", new RolePermissionsRequest(["product.view"]), ApiTestHost.Json)).StatusCode);
        Assert.All(await api.ReadAsync<UserResponse[]>(await api.Client.GetAsync("/admin/users")), x => Assert.Equal(me.OrganizationId, x.OrganizationId));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/admin/users", new CreateUserRequest("badrole", AuthenticationTestSupport.Password, RoleIds: [foreignRole]), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/admin/users", new CreateUserRequest("badparty", AuthenticationTestSupport.Password, CounterpartyId: foreignCounterparty), ApiTestHost.Json)).StatusCode);
        var own = await api.AddUserAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/admin/users/{own.UserId}", new UpdateUserRequest(own.Username, RoleIds: [foreignRole]), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/admin/users/{own.UserId}", new UpdateUserRequest(own.Username, CounterpartyId: foreignCounterparty), ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task User_can_link_own_counterparty_and_update_username_and_roles()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var party = await api.CreateCounterpartyAsync(); var role = await api.AddRoleAsync(["product.view"]);
        var user = await api.AddUserAsync([role.Id], counterpartyId: party.Id);
        var update = new UpdateUserRequest("updated_login", "Updated Name", party.Id, RoleIds: [role.Id]);
        var saved = await api.ReadAsync<UserResponse>(await api.Client.PutAsJsonAsync($"/admin/users/{user.UserId}", update, ApiTestHost.Json));
        Assert.Equal(("updated_login", "Updated Name", party.Id), (saved.Username, saved.FullName, saved.CounterpartyId));
        Assert.Equal([role.Id], saved.RoleIds);
        using var client = api.NewClient(); Assert.Equal(HttpStatusCode.NoContent, (await client.LoginAsync(saved.Username)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/products")).StatusCode);
    }

    [Fact]
    public async Task Database_forbids_physical_user_deletion_cross_organization_roles_and_organization_changes()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var user = await api.AddUserAsync(); var foreign = new TradeRole { OrganizationId = Guid.NewGuid(), Name = "FOREIGN", NormalizedName = "FOREIGN" };
        await api.WithDbAsync(async db => { db.Roles.Add(foreign); await db.SaveChangesAsync(); });
        foreach (var sql in new[]
        {
            $"DELETE FROM users WHERE \"Id\" = '{user.UserId}'",
            $"UPDATE users SET \"OrganizationId\" = '{Guid.NewGuid()}' WHERE \"Id\" = '{user.UserId}'",
            $"INSERT INTO user_roles (\"UserId\", \"RoleId\") VALUES ('{user.UserId}', '{foreign.Id}')"
        })
            await api.WithDbAsync(async db => Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await api.Client.DeleteAsync($"/admin/users/{user.UserId}")).StatusCode);
        Assert.Equal(2, await api.QueryAsync(db => db.Users.CountAsync()));
    }
}
