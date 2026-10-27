using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class PurchaseUiQueryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task List_filters_and_derived_totals_exclude_removed_deleted_and_opening_stock()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync(quantity: 2m);
        purchase = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}",
            ApiTestHost.Edit(purchase) with { DiscountPercent = 10m, ExternalNumber = "EXT-10%_" }, ApiTestHost.Json));
        var posted = await api.PostAsync(purchase.Document.Id);
        await api.CreatePurchaseAsync(type: DocumentType.OPENING_STOCK);
        var deleted = await api.CreatePurchaseAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/purchases/{deleted.Document.Id}")).StatusCode);
        var filter = $"from=2026-10-19&to=2026-10-19&counterpartyId={purchase.CounterpartyId}&storageLocationId={purchase.StorageLocationId}&currencyId={purchase.CurrencyId}&status=POSTED";
        var row = Assert.Single(await api.ReadAsync<PurchaseListResponse[]>(await api.Client.GetAsync("/purchases?" + filter)));
        Assert.Equal(posted.Document.Number, row.Number); Assert.Equal(18m, row.TotalCurrency);
        Assert.Equal(purchase.CounterpartyId, row.CounterpartyId); Assert.NotNull(row.CounterpartyName);
        Assert.Single(await api.ReadAsync<PurchaseListResponse[]>(await api.Client.GetAsync("/purchases?search=%25_")));
        Assert.Single(await api.ReadAsync<PurchaseListResponse[]>(await api.Client.GetAsync($"/purchases?search={row.Number}")));
        Assert.Single(await api.ReadAsync<PurchaseListResponse[]>(await api.Client.GetAsync("/purchases?search=test%20counterparty")));
        Assert.Empty(await api.ReadAsync<PurchaseListResponse[]>(await api.Client.GetAsync("/purchases?status=DRAFT")));
        Assert.Empty(await api.ReadAsync<PurchaseListResponse[]>(await api.Client.GetAsync("/purchases?from=2026-10-20")));
        Assert.Empty(await api.ReadAsync<PurchaseListResponse[]>(await api.Client.GetAsync($"/purchases?storageLocationId={Guid.NewGuid()}")));
        api.SetOrganization(Guid.NewGuid());
        Assert.Empty(await api.ReadAsync<PurchaseListResponse[]>(await api.Client.GetAsync("/purchases")));
    }

    [Fact]
    public async Task Batch_queries_show_retained_batches_prices_and_current_view_cost_without_changing_business_facts()
    {
        await using var api = await fixture.CreateApiAsync();
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail", 25m), ApiTestHost.Json), HttpStatusCode.Created);
        var purchase = await api.CreatePurchaseAsync(quantity: 2m);
        Assert.Empty(await api.ReadAsync<PurchaseBatchResponse[]>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}/batches")));
        await api.PostAsync(purchase.Document.Id);
        var batch = Assert.Single(await api.ReadAsync<PurchaseBatchResponse[]>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}/batches")));
        Assert.Equal(purchase.Lines[0].ProductId, batch.ProductId); Assert.NotEmpty(batch.ProductName); Assert.NotEmpty(batch.ProductCode);
        Assert.Equal(10m, Assert.Single(await api.ReadAsync<PurchaseBatchCostResponse[]>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}/batch-costs"))).UnitCostUah);
        var price = Assert.Single(await api.ReadAsync<PurchaseBatchPriceResponse[]>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}/batch-prices")));
        Assert.Equal(12.5m, price.UnitPrice); Assert.Equal(type.Name, price.PriceTypeName);
        purchase = await api.UnpostAsync(purchase.Document.Id);
        await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", ApiTestHost.Edit(purchase) with { ExchangeRate = 2m }, ApiTestHost.Json));
        Assert.Equal(batch.BatchId, Assert.Single(await api.ReadAsync<PurchaseBatchResponse[]>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}/batches"))).BatchId);
        Assert.Equal(20m, Assert.Single(await api.ReadAsync<PurchaseBatchCostResponse[]>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}/batch-costs"))).UnitCostUah);
        Assert.Equal(price, Assert.Single(await api.ReadAsync<PurchaseBatchPriceResponse[]>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}/batch-prices"))));
        var detail = await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}"));
        Assert.Equal(batch.ProductName, Assert.Single(detail.Products!).Name);
        api.SetOrganization(Guid.NewGuid());
        foreach (var suffix in new[] { "", "/batches", "/batch-costs", "/batch-prices" })
            Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/purchases/{purchase.Document.Id}{suffix}")).StatusCode);
    }

    [Fact]
    public async Task Draft_delete_is_logical_keeps_lines_and_retained_batches_and_posted_delete_is_rejected()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync(); await api.PostAsync(purchase.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.DeleteAsync($"/purchases/{purchase.Document.Id}")).StatusCode);
        await api.UnpostAsync(purchase.Document.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/purchases/{purchase.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/purchases/{purchase.Document.Id}")).StatusCode);
        Assert.NotNull(await api.QueryAsync(db => db.Documents.Where(x => x.Id == purchase.Document.Id).Select(x => x.DeletedAt).SingleAsync()));
        Assert.Single(await api.QueryAsync(db => db.PurchaseInvoiceLines.Where(x => x.PurchaseInvoiceId == purchase.Document.Id).ToArrayAsync()));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToArrayAsync()));
    }

    [Fact]
    public async Task Minimal_location_directory_preserves_external_ids_and_isolates_organizations()
    {
        await using var api = await fixture.CreateApiAsync();
        var location = await api.ReadAsync<StorageLocationResponse>(await api.Client.PostAsJsonAsync("/storage-locations", new StorageLocationRequest("  Основний  "), ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal("Основний", location.Name);
        var purchase = await api.CreatePurchaseAsync(storageLocationId: location.Id);
        var external = await api.CreatePurchaseAsync();
        var lookups = await api.ReadAsync<PurchaseLookupsResponse>(await api.Client.GetAsync("/purchases/lookups"));
        Assert.Contains(lookups.Locations, x => x == location);
        Assert.Contains(lookups.Locations, x => x.Id == external.StorageLocationId && x.Name == null);
        Assert.Contains(Guid.Parse("00000000-0000-0000-0000-000000000980"), lookups.CurrencyIds);
        Assert.Contains(purchase.CurrencyId, lookups.CurrencyIds);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/storage-locations", new StorageLocationRequest(" "), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PostAsJsonAsync("/storage-locations", new StorageLocationRequest("Основний"), ApiTestHost.Json)).StatusCode);
        var columns = await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'storage_locations'""").ToArrayAsync());
        Assert.Equal(new[] { "Id", "OrganizationId", "Name" }.Order(), columns.Order());
        api.SetOrganization(Guid.NewGuid());
        Assert.Empty((await api.ReadAsync<PurchaseLookupsResponse>(await api.Client.GetAsync("/purchases/lookups"))).Locations);
        var product = await api.CreateProductAsync(); var party = await api.CreateCounterpartyAsync();
        var body = new PurchaseRequest(DocumentType.PURCHASE_INVOICE, new(2026, 10, 20), null, null, party.Id, location.Id, purchase.CurrencyId, 1m, null, null, 0m,
            [new(null, 1, product.Id, 1m, 1m)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/purchases", body, ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Query_cost_location_and_delete_permissions_use_existing_permission_catalog()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var (_, client, _) = await api.LimitedUserAsync("purchase.view"); using var session = client;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/purchases")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/purchases/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/purchases/{Guid.NewGuid()}/batch-costs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/storage-locations", new StorageLocationRequest("Main"), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/purchases/{Guid.NewGuid()}")).StatusCode);
        var (_, creator, _) = await api.LimitedUserAsync("purchase.create"); using var creationSession = creator;
        Assert.Equal(HttpStatusCode.OK, (await creator.GetAsync("/purchases/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await creator.PostAsJsonAsync("/storage-locations", new StorageLocationRequest("Main"), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await creator.GetAsync("/purchases")).StatusCode);
    }
}
