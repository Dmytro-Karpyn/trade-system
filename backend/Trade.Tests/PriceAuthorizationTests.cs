using System.Net;
using System.Net.Http.Json;
using Trade.Application;
using Trade.Application.Authentication;
using Trade.Domain.Catalog;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class PriceAuthorizationTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Below_recommended_price_requires_sale_change_price_for_create_edit_and_POST(bool service)
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync(); await api.InitializeCounterpartyAsync();
        var stock = await api.ReceiveAsync();
        var productId = service ? (await api.CreateProductAsync(ItemType.SERVICE)).Id : stock.ProductId;
        Guid? batchId = service ? null : stock.BatchId;
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail"), ApiTestHost.Json), HttpStatusCode.Created);
        var pricePath = service ? $"/products/{productId}/prices/{type.Id}" : $"/batches/{batchId}/prices/{type.Id}";
        Assert.Equal(HttpStatusCode.OK, (await api.Client.PutAsJsonAsync(pricePath, new PriceRequest(10m), ApiTestHost.Json)).StatusCode);
        var (_, client, role) = await api.LimitedUserAsync("sale.create", "sale.post", "sale.view"); using var session = client;
        var input = api.NewInput(stock.LocationId, [new(null, 1, productId, batchId, 1m, 9m)]) with { PriceTypeId = type.Id };
        var denied = await client.PostAsJsonAsync("/sales", input, ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("PRICE_BELOW_RECOMMENDED", await denied.Content.ReadAsStringAsync());
        var equal = await api.ReadAsync<SaleResponse>(await client.PostAsJsonAsync("/sales", input with { Lines = [input.Lines[0] with { UnitPrice = 10m }] }, ApiTestHost.Json), HttpStatusCode.Created);
        var edit = SalesTestSupport.Edit(equal) with { Lines = [SalesTestSupport.Edit(equal).Lines[0] with { UnitPrice = 11m }] };
        var above = await api.ReadAsync<SaleResponse>(await client.PutAsJsonAsync($"/sales/{equal.Document.Id}", edit, ApiTestHost.Json));
        Assert.Equal(11m, above.Lines[0].UnitPrice);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/sales/{equal.Document.Id}", SalesTestSupport.Edit(above) with { Lines = [SalesTestSupport.Edit(above).Lines[0] with { UnitPrice = 9m }] }, ApiTestHost.Json)).StatusCode);
        await api.ReadAsync<RoleResponse>(await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(["sale.create", "sale.post", "sale.view", "sale.change_price"]), ApiTestHost.Json));
        var discounted = await api.ReadAsync<SaleResponse>(await client.PostAsJsonAsync("/sales", input, ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal((10m, 9m), (discounted.Lines[0].RecommendedUnitPrice, discounted.Lines[0].UnitPrice));
        await api.ReadAsync<RoleResponse>(await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(["sale.create", "sale.post", "sale.view"]), ApiTestHost.Json));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/sales/{discounted.Document.Id}/post", null)).StatusCode);
        await api.ReadAsync<RoleResponse>(await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(["sale.create", "sale.post", "sale.view", "sale.change_price"]), ApiTestHost.Json));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/sales/{discounted.Document.Id}/post", null)).StatusCode);
    }

    [Fact]
    public async Task Cost_view_is_computed_and_tenant_scoped_and_requires_explicit_cost_permission()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync(); await api.InitializeCounterpartyAsync();
        var stock = await api.ReceiveAsync(); var reason = await api.CreateReasonAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id)); await api.PostWriteOffAsync(writeOff.Document.Id);
        var (_, client, role) = await api.LimitedUserAsync("stock.writeoff", "reports.view"); using var session = client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/write-offs/costs")).StatusCode);
        await api.ReadAsync<RoleResponse>(await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new RolePermissionsRequest(["cost.view"]), ApiTestHost.Json));
        var rows = await api.ReadAsync<WriteOffCostData[]>(await client.GetAsync("/write-offs/costs"));
        var cost = Assert.Single(rows); Assert.Equal(writeOff.Document.Id, cost.DocumentId); Assert.Equal(20m, cost.TotalCostUah);
        client.DefaultRequestHeaders.Add("X-Organization-Id", Guid.NewGuid().ToString());
        Assert.Single(await api.ReadAsync<WriteOffCostData[]>(await client.GetAsync("/write-offs/costs")));
    }
}
