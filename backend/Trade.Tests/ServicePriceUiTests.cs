using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Application.Authentication;
using Trade.Domain.Catalog;
using Trade.Domain.Pricing;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ServicePriceUiTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task List_and_upsert_service_prices_preserve_unique_keys_and_do_not_invent_prices_for_new_types()
    {
        await using var api = await fixture.CreateApiAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail", 25m), ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Empty(await api.ReadAsync<ProductPriceResponse[]>(await api.Client.GetAsync($"/products/{service.Id}/prices")));
        var path = $"/products/{service.Id}/prices/{type.Id}";
        await api.ReadAsync<ProductPriceResponse>(await api.Client.PutAsJsonAsync(path, new PriceRequest(300m), ApiTestHost.Json));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => await api.ReadAsync<ProductPriceResponse>(await api.Client.PutAsJsonAsync(path, new PriceRequest(350m), ApiTestHost.Json))));
        Assert.Equal(350m, Assert.Single(await api.ReadAsync<ProductPriceResponse[]>(await api.Client.GetAsync($"/products/{service.Id}/prices"))).UnitPrice);
        Assert.Equal(1, await api.QueryAsync(db => db.ProductPrices.CountAsync()));
        await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("New type", 50m), ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Single(await api.ReadAsync<ProductPriceResponse[]>(await api.Client.GetAsync($"/products/{service.Id}/prices")));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync(path, new PriceRequest(-1m), ApiTestHost.Json)).StatusCode);
        Assert.Equal(0m, (await api.ReadAsync<ProductPriceResponse>(await api.Client.PutAsJsonAsync(path, new PriceRequest(0m), ApiTestHost.Json))).UnitPrice);
        Assert.Empty(await api.QueryAsync(db => db.BatchPrices.ToListAsync())); Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
        var goods = await api.CreateProductAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync($"/products/{goods.Id}/prices")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/products/{goods.Id}/prices/{type.Id}", new PriceRequest(100m), ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Pricing_permissions_and_organization_isolation_apply_to_bulk_read_and_upsert()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail"), ApiTestHost.Json), HttpStatusCode.Created);
        var (_, limited, role) = await api.LimitedUserAsync("product.view", "product.edit"); using var client = limited;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/products/{service.Id}/prices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/products/{service.Id}/prices/{type.Id}", new PriceRequest(10m), ApiTestHost.Json)).StatusCode);
        await api.ReadAsync<RoleResponse>(await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(["settings.manage"]), ApiTestHost.Json));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/products/{service.Id}/prices")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/products/{service.Id}/prices/{type.Id}", new PriceRequest(10m), ApiTestHost.Json)).StatusCode);
        var foreignOrg = Guid.NewGuid(); Guid foreignProduct = Guid.Empty, foreignType = Guid.Empty;
        await api.WithDbAsync(async db =>
        {
            var p = Product.Create(foreignOrg, new(null, Guid.NewGuid(), ItemType.SERVICE, "FOREIGN", null, "Foreign", null, null, true));
            var t = PriceType.Create(foreignOrg, "Foreign"); foreignProduct = p.Id; foreignType = t.Id; db.Products.Add(p); db.PriceTypes.Add(t); db.ProductPrices.Add(ProductPrice.Create(p, t, 99m)); await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/products/{foreignProduct}/prices")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/products/{foreignProduct}/prices/{type.Id}", new PriceRequest(1m), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/products/{service.Id}/prices/{foreignType}", new PriceRequest(1m), ApiTestHost.Json)).StatusCode);
        Assert.Single(await api.ReadAsync<ProductPriceResponse[]>(await api.Client.GetAsync($"/products/{service.Id}/prices")));
    }

    [Fact]
    public async Task ProductPrice_audit_identifies_user_product_type_timestamp_and_old_new_price()
    {
        await using var api = await fixture.IdentityApiAsync(); var me = await api.LoginAdminAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail"), ApiTestHost.Json), HttpStatusCode.Created);
        var before = DateTimeOffset.UtcNow;
        foreach (var amount in new[] { 300m, 350m }) await api.ReadAsync<ProductPriceResponse>(await api.Client.PutAsJsonAsync($"/products/{service.Id}/prices/{type.Id}", new PriceRequest(amount), ApiTestHost.Json));
        var page = await api.ReadAsync<AuditPage>(await api.Client.GetAsync("/admin/audit?entityType=ProductPrice&eventType=UPDATE")); var row = Assert.Single(page.Items);
        Assert.Equal(me.UserId, row.ActorId); Assert.Contains(service.Id.ToString(), row.EntityId); Assert.Contains(type.Id.ToString(), row.EntityId); Assert.InRange(row.OccurredAt, before, DateTimeOffset.UtcNow);
        var detail = await api.ReadAsync<AuditDetail>(await api.Client.GetAsync($"/admin/audit/{row.Id}"));
        Assert.Equal(300m, ((JsonElement)detail.OldValues!).GetProperty("UnitPrice").GetDecimal()); Assert.Equal(350m, ((JsonElement)detail.NewValues!).GetProperty("UnitPrice").GetDecimal());
    }
}
