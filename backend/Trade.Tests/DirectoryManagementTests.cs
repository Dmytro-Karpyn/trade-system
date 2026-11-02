using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class DirectoryManagementTests(DatabaseFixture fixture)
{
    private static async Task<CategoryResponse> Category(ApiTestHost api, string name, Guid? parent = null) =>
        await api.ReadAsync<CategoryResponse>(await api.Client.PostAsJsonAsync("/categories", new CategoryRequest(name, parent), ApiTestHost.Json), HttpStatusCode.Created);

    [Fact]
    public async Task Category_tree_supports_nested_edit_and_rejects_cycles_and_duplicate_siblings()
    {
        await using var api = await fixture.CreateApiAsync();
        var root = await Category(api, "Посуд"); var child = await Category(api, "Чашки", root.Id);
        var leaf = await Category(api, "Керамічні", child.Id);
        var updated = await api.ReadAsync<CategoryResponse>(await api.Client.PutAsJsonAsync($"/categories/{child.Id}", new CategoryRequest("Горнятка", root.Id, "Опис", 7, false), ApiTestHost.Json));
        Assert.Equal(("Горнятка", root.Id, "Опис", 7, false), (updated.Name, updated.ParentId, updated.Description, updated.SortOrder, updated.Active));
        foreach (var parent in new[] { root.Id, child.Id, leaf.Id })
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/categories/{root.Id}", new CategoryRequest(root.Name, parent), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PostAsJsonAsync("/categories", new CategoryRequest(root.Name), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PostAsJsonAsync("/categories", new CategoryRequest(updated.Name, root.Id), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/categories", new CategoryRequest(" "), ApiTestHost.Json)).StatusCode);
        var rows = await api.ReadAsync<CategoryResponse[]>(await api.Client.GetAsync("/categories"));
        Assert.Equal(3, rows.Length); Assert.Equal(child.Id, rows.Single(x => x.Id == leaf.Id).ParentId);
    }

    [Fact]
    public async Task Concurrent_category_reparenting_cannot_create_a_cycle()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await Category(api, "A"); var b = await Category(api, "B");
        var responses = await Task.WhenAll(api.Client.PutAsJsonAsync($"/categories/{a.Id}", new CategoryRequest("A", b.Id), ApiTestHost.Json),
            api.Client.PutAsJsonAsync($"/categories/{b.Id}", new CategoryRequest("B", a.Id), ApiTestHost.Json));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Category_names_flow_to_product_stock_and_filters_without_a_depth_limit()
    {
        await using var api = await fixture.CreateApiAsync();
        var parent = await Category(api, "Посуд"); var path = parent.Name;
        for (var i = 0; i < 35; i++) { parent = await Category(api, $"Рівень {i}", parent.Id); path += " / " + parent.Name; }
        var product = await api.ReadAsync<ProductResponse>(await api.Client.PostAsJsonAsync("/products", api.ProductInput() with { CategoryId = parent.Id }, ApiTestHost.Json), HttpStatusCode.Created);
        var purchase = await api.CreatePurchaseAsync(product.Id); await api.PostAsync(purchase.Document.Id);
        var stock = await api.ReadAsync<StockProductDetail>(await api.Client.GetAsync($"/stock/products/{product.Id}"));
        Assert.Equal(path, stock.Product.Category);
        var lookups = await api.ReadAsync<StockLookups>(await api.Client.GetAsync("/stock/lookups"));
        Assert.Equal(path, lookups.Categories.Single(x => x.Id == parent.Id).Name);
        Assert.Equal(path, Assert.Single(await api.ReadAsync<StockRow[]>(await api.Client.GetAsync($"/stock/overview?categoryId={parent.Id}"))).Category);
    }

    [Fact]
    public async Task Directory_links_are_organization_scoped_and_legacy_product_category_ids_remain_compatible()
    {
        await using var api = await fixture.CreateApiAsync();
        var category = await Category(api, "Own");
        var unit = await api.ReadAsync<UnitResponse>(await api.Client.PostAsJsonAsync("/units", new UnitRequest("шт"), ApiTestHost.Json), HttpStatusCode.Created);
        var location = await api.ReadAsync<StorageLocationResponse>(await api.Client.PostAsJsonAsync("/storage-locations", new StorageLocationRequest("Основний"), ApiTestHost.Json), HttpStatusCode.Created);
        api.SetOrganization(Guid.NewGuid());
        Assert.Empty(await api.ReadAsync<CategoryResponse[]>(await api.Client.GetAsync("/categories")));
        Assert.Empty(await api.ReadAsync<StorageLocationResponse[]>(await api.Client.GetAsync("/storage-locations")));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/categories", new CategoryRequest("Child", category.Id), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/categories/{category.Id}", new CategoryRequest("Foreign"), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/units/{unit.Id}", new DirectoryNameRequest("Foreign"), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/storage-locations/{location.Id}", new DirectoryNameRequest("Foreign"), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/products", api.ProductInput() with { CategoryId = category.Id }, ApiTestHost.Json)).StatusCode);
        var product = await api.CreateProductAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/products/{product.Id}", api.ProductInput() with { CategoryId = category.Id }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await api.Client.PostAsJsonAsync("/products", api.ProductInput() with { CategoryId = Guid.NewGuid() }, ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Unit_and_location_can_be_created_inactive_edited_and_reactivated_without_deleting()
    {
        await using var api = await fixture.CreateApiAsync();
        var unit = await api.ReadAsync<UnitResponse>(await api.Client.PostAsJsonAsync("/units", new UnitRequest("шт", false), ApiTestHost.Json), HttpStatusCode.Created);
        var location = await api.ReadAsync<StorageLocationResponse>(await api.Client.PostAsJsonAsync("/storage-locations", new StorageLocationRequest("Склад", false), ApiTestHost.Json), HttpStatusCode.Created);
        Assert.False(unit.Active); Assert.False(location.Active);
        Assert.False(Assert.Single(await api.ReadAsync<UnitResponse[]>(await api.Client.GetAsync("/units"))).Active);
        Assert.False(Assert.Single(await api.ReadAsync<StorageLocationResponse[]>(await api.Client.GetAsync("/storage-locations"))).Active);
        Assert.True((await api.ReadAsync<UnitResponse>(await api.Client.PutAsJsonAsync($"/units/{unit.Id}", new DirectoryNameRequest("штука"), ApiTestHost.Json))).Active);
        Assert.True((await api.ReadAsync<StorageLocationResponse>(await api.Client.PutAsJsonAsync($"/storage-locations/{location.Id}", new DirectoryNameRequest("Основний"), ApiTestHost.Json))).Active);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await api.Client.DeleteAsync($"/units/{unit.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await api.Client.DeleteAsync($"/storage-locations/{location.Id}")).StatusCode);
    }

    [Fact]
    public async Task Price_defaults_are_explicit_idempotent_and_never_reprice_existing_batches()
    {
        await using var api = await fixture.CreateApiAsync();
        Assert.Empty(await api.ReadAsync<PriceTypeResponse[]>(await api.Client.GetAsync("/price-types")));
        var defaults = await api.ReadAsync<PriceTypeResponse[]>(await api.Client.PostAsync("/price-types/defaults", null));
        Assert.Equal(new[] { "Роздрібна", "Гуртова" }, defaults.Select(x => x.Name));
        var purchase = await api.CreatePurchaseAsync(); await api.PostAsync(purchase.Document.Id);
        var prices = await api.QueryAsync(db => db.BatchPrices.AsNoTracking().OrderBy(x => x.PriceTypeId).Select(x => x.UnitPrice).ToArrayAsync());
        await api.ReadAsync<PriceTypeResponse>(await api.Client.PutAsJsonAsync($"/price-types/{defaults[0].Id}", new PriceTypeRequest(defaults[0].Name, 50m, false, 7), ApiTestHost.Json));
        var again = await api.ReadAsync<PriceTypeResponse[]>(await api.Client.PostAsync("/price-types/defaults", null));
        Assert.Equal(2, again.Length); Assert.False(again.Single(x => x.Id == defaults[0].Id).Active);
        Assert.Equal(50m, again.Single(x => x.Id == defaults[0].Id).DefaultMarkupPercent);
        Assert.Equal(prices, await api.QueryAsync(db => db.BatchPrices.AsNoTracking().OrderBy(x => x.PriceTypeId).Select(x => x.UnitPrice).ToArrayAsync()));
    }

    [Fact]
    public async Task Writeoff_reason_keeps_expense_category_and_currencies_are_read_only_known_references()
    {
        await using var api = await fixture.CreateApiAsync();
        var category = await api.CreateCategoryAsync(); var reason = await api.CreateReasonAsync(category.Id);
        Assert.Equal(category.Id, reason.ExpenseCategoryId);
        var currencies = await api.ReadAsync<CurrencyReference[]>(await api.Client.GetAsync("/currencies"));
        Assert.Equal("UAH", Assert.Single(currencies, x => x.IsBase).Code);
        Assert.Equal(SettlementCurrencies.Uah, Assert.Single(currencies, x => x.IsBase).Id);
        var purchase = await api.CreatePurchaseAsync();
        currencies = await api.ReadAsync<CurrencyReference[]>(await api.Client.GetAsync("/currencies"));
        var external = Assert.Single(currencies, x => x.Id == purchase.CurrencyId);
        Assert.Null(external.Code); Assert.Null(external.Name); Assert.Null(external.Active);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await api.Client.PostAsJsonAsync("/currencies", new { code = "USD" })).StatusCode);
    }
}
