using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ValidationAndIsolationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Product_CRUD_preserves_nullable_fields_and_JSONB_attributes()
    {
        await using var api = await fixture.CreateApiAsync();
        var attributes = JsonSerializer.Deserialize<JsonElement>("""{"color":"blue","size":42}""");
        var input = api.ProductInput() with { Attributes = attributes, Barcode = "0123456789", Description = "Details" };
        var p = await api.ReadAsync<ProductResponse>(await api.Client.PostAsJsonAsync("/products", input, ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal(api.OrganizationId, p.OrganizationId);
        Assert.Equal("blue", p.Attributes!.Value.GetProperty("color").GetString());
        var fetched = await api.ReadAsync<ProductResponse>(await api.Client.GetAsync($"/products/{p.Id}"));
        Assert.Equal(input.Barcode, fetched.Barcode);
        var list = await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products"));
        Assert.Equal(p.Id, Assert.Single(list).Id);
        var edited = await api.ReadAsync<ProductResponse>(await api.Client.PutAsJsonAsync($"/products/{p.Id}",
            input with { Name = "Updated", Active = false, Attributes = null, Barcode = null, Description = null }, ApiTestHost.Json));
        Assert.Equal("Updated", edited.Name);
        Assert.False(edited.Active);
        Assert.Null(edited.Attributes);
        Assert.Null(edited.Barcode);
        Assert.Null(edited.Description);
    }

    [Fact]
    public async Task SERVICE_cannot_be_created_or_edited_into_a_purchase_line()
    {
        await using var api = await fixture.CreateApiAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var input = PurchaseInput(api.CounterpartyId, service.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/purchases", input, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.Documents.ToListAsync()));
        var draft = await api.CreatePurchaseAsync();
        var update = ApiTestHost.Edit(draft) with { Lines = [new(draft.Lines[0].Id, 1, service.Id, 1m, 1m)] };
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/purchases/{draft.Document.Id}", update, ApiTestHost.Json)).StatusCode);
        Assert.Equal(draft.Lines[0].ProductId,
            (await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{draft.Document.Id}"))).Lines[0].ProductId);
    }

    [Fact]
    public async Task Referenced_GOODS_cannot_be_changed_to_SERVICE()
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await api.CreateProductAsync();
        await api.CreatePurchaseAsync(product.Id);
        var input = new ProductRequest(product.CategoryId, product.UnitId, ItemType.SERVICE, product.Code,
            product.Barcode, product.Name, product.Description, product.Attributes, product.Active);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/products/{product.Id}", input, ApiTestHost.Json)).StatusCode);
        Assert.Equal(ItemType.GOODS, (await api.ReadAsync<ProductResponse>(await api.Client.GetAsync($"/products/{product.Id}"))).ItemType);
    }

    [Fact]
    public async Task Counterparty_is_required_except_for_OPENING_STOCK()
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await api.CreateProductAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/purchases",
            PurchaseInput(api.CounterpartyId, product.Id) with { CounterpartyId = null }, ApiTestHost.Json)).StatusCode);
        var opening = await api.CreatePurchaseAsync(product.Id, type: DocumentType.OPENING_STOCK);
        Assert.Null(opening.CounterpartyId);
        Assert.StartsWith("OS-", (await api.PostAsync(opening.Document.Id)).Document.Number);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.00001")]
    [InlineData("10000000000000000")]
    public async Task Invalid_quantity_is_rejected_without_rounding_or_partial_writes(string value)
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await api.CreateProductAsync();
        var input = PurchaseInput(api.CounterpartyId, product.Id) with
            { Lines = [new(null, 1, product.Id, decimal.Parse(value, CultureInfo.InvariantCulture), 1m)] };
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/purchases", input, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.Documents.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
    }

    [Theory]
    [InlineData("price")]
    [InlineData("exchange")]
    [InlineData("discount")]
    [InlineData("duplicate-line-no")]
    public async Task Invalid_purchase_values_are_rejected_on_backend(string invalid)
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await api.CreateProductAsync();
        var input = PurchaseInput(api.CounterpartyId, product.Id);
        input = invalid switch
        {
            "price" => input with { Lines = [new(null, 1, product.Id, 1m, -1m)] },
            "exchange" => input with { ExchangeRate = 0 },
            "discount" => input with { DiscountPercent = 100.01m },
            _ => input with { Lines = [new(null, 1, product.Id, 1m, 1m), new(null, 1, product.Id, 2m, 1m)] }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/purchases", input, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.Documents.ToListAsync()));
    }

    [Fact]
    public async Task Empty_draft_can_be_saved_but_cannot_be_posted()
    {
        await using var api = await fixture.CreateApiAsync();
        var input = PurchaseInput(api.CounterpartyId, Guid.NewGuid()) with { Lines = [] };
        var draft = await api.ReadAsync<PurchaseResponse>(await api.Client.PostAsJsonAsync("/purchases", input, ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/purchases/{draft.Document.Id}/post", null)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Null((await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{draft.Document.Id}"))).Document.Number);
    }

    [Fact]
    public async Task Organization_context_is_required_and_tenants_cannot_read_or_mutate_each_others_data()
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await api.CreateProductAsync();
        var purchase = await api.CreatePurchaseAsync(product.Id);
        await api.PostAsync(purchase.Document.Id);
        api.Client.DefaultRequestHeaders.Remove("X-Test-Organization-Id");
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.GetAsync("/products")).StatusCode);
        api.Client.DefaultRequestHeaders.Add("X-Test-Organization-Id", Guid.NewGuid().ToString());
        Assert.Empty(await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products")));
        Assert.Empty(await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync("/stock/balances")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/products/{product.Id}", api.ProductInput(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/purchases/{purchase.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", ApiTestHost.Edit(purchase), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/purchases/{purchase.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/purchases/{purchase.Document.Id}/unpost", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/purchases", PurchaseInput(api.CounterpartyId, product.Id), ApiTestHost.Json)).StatusCode);
        Assert.Single(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
    }

    [Fact]
    public async Task Production_does_not_trust_development_identity_headers()
    {
        await using var api = await fixture.CreateApiAsync("Production");
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.GetAsync("/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.PostAsJsonAsync("/products", api.ProductInput(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync("/health")).StatusCode);
    }

    private static PurchaseRequest PurchaseInput(Guid counterpartyId, Guid productId) => new(DocumentType.PURCHASE_INVOICE,
        new DateOnly(2026, 10, 19), null, null, counterpartyId, Guid.NewGuid(), Guid.NewGuid(), 1m,
        null, null, 0m, [new(null, 1, productId, 1m, 1m)]);
}
