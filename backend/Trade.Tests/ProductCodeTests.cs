using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ProductCodeTests(DatabaseFixture fixture)
{
    private const string DuplicateMessage = "Товар або послуга з таким кодом уже існує.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task Empty_code_generates_first_code_without_a_barcode(string? code)
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await CreateAsync(api, code);
        Assert.Equal("P000001", product.Code);
        Assert.Null(product.Barcode);
        Assert.Equal(product, await api.ReadAsync<ProductResponse>(await api.Client.GetAsync($"/products/{product.Id}")));
    }

    [Fact]
    public async Task Goods_and_services_use_one_organization_number_range()
    {
        await using var api = await fixture.CreateApiAsync();
        var goods = await CreateAsync(api, null);
        var service = await CreateAsync(api, "", ItemType.SERVICE);
        var next = await CreateAsync(api, " ");
        Assert.Equal(new[] { "P000001", "P000002", "P000003" }, new[] { goods.Code, service.Code, next.Code });
    }

    [Fact]
    public async Task Manual_code_is_trimmed_and_barcode_is_independent()
    {
        await using var api = await fixture.CreateApiAsync();
        var request = api.ProductInput() with { Code = "  TEST-001  ", Barcode = "4821234567890" };
        var product = await api.ReadAsync<ProductResponse>(await api.Client.PostAsJsonAsync("/products", request, ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal("TEST-001", product.Code);
        Assert.Equal(request.Barcode, product.Barcode);
        Assert.Equal("P000001", (await CreateAsync(api, null)).Code);
    }

    [Fact]
    public async Task Duplicate_trimmed_manual_code_is_rejected_for_goods_or_services()
    {
        await using var api = await fixture.CreateApiAsync();
        await CreateAsync(api, "TEST-001");
        var response = await api.Client.PostAsJsonAsync("/products", api.ProductInput(ItemType.SERVICE) with { Code = " TEST-001 " }, ApiTestHost.Json);
        await AssertDuplicateAsync(response);
        Assert.Equal(1, await api.QueryAsync(db => db.Products.CountAsync()));
    }

    [Fact]
    public async Task Different_organizations_can_reuse_generated_and_manual_codes()
    {
        await using var api = await fixture.CreateApiAsync();
        var first = await CreateAsync(api, null);
        await CreateAsync(api, "SHARED");
        api.SetOrganization(Guid.NewGuid());
        var second = await CreateAsync(api, null, ItemType.SERVICE);
        await CreateAsync(api, "SHARED");
        Assert.Equal("P000001", first.Code);
        Assert.Equal(first.Code, second.Code);
        Assert.NotEqual(first.OrganizationId, second.OrganizationId);
        Assert.Equal(2, (await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products"))).Length);
    }

    [Fact]
    public async Task Concurrent_creates_receive_distinct_consecutive_codes()
    {
        await using var api = await fixture.CreateApiAsync();
        var responses = await Task.WhenAll(
            api.Client.PostAsJsonAsync("/products", api.ProductInput() with { Code = "" }, ApiTestHost.Json),
            api.Client.PostAsJsonAsync("/products", api.ProductInput(ItemType.SERVICE) with { Code = null }, ApiTestHost.Json));
        var products = await Task.WhenAll(responses.Select(response => api.ReadAsync<ProductResponse>(response, HttpStatusCode.Created)));
        Assert.Equal(new[] { "P000001", "P000002" }, products.Select(x => x.Code).Order().ToArray());
        Assert.Equal(2, await api.QueryAsync(db => db.Products.CountAsync()));
    }

    [Fact]
    public async Task Manual_and_automatic_creates_share_the_same_concurrency_lock()
    {
        await using var api = await fixture.CreateApiAsync();
        var automaticTask = api.Client.PostAsJsonAsync("/products", api.ProductInput() with { Code = "" }, ApiTestHost.Json);
        var manualTask = api.Client.PostAsJsonAsync("/products", api.ProductInput() with { Code = "P000001" }, ApiTestHost.Json);
        var responses = await Task.WhenAll(automaticTask, manualTask);
        Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
        Assert.Contains(responses[1].StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        var products = await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products"));
        Assert.Equal(products.Length, products.Select(x => x.Code).Distinct().Count());
    }

    [Fact]
    public async Task Existing_numeric_and_inactive_codes_are_respected_without_renumbering()
    {
        await using var api = await fixture.CreateApiAsync();
        var first = await CreateAsync(api, "P000001");
        var old = await CreateAsync(api, "LEGACY");
        var inactive = await api.ReadAsync<ProductResponse>(await api.Client.PostAsJsonAsync("/products",
            api.ProductInput() with { Code = "P000015", Active = false }, ApiTestHost.Json), HttpStatusCode.Created);
        await CreateAsync(api, "P999999-custom");
        var org = api.OrganizationId;
        api.SetOrganization(Guid.NewGuid());
        await CreateAsync(api, "P999998");
        api.SetOrganization(org);
        Assert.Equal("P000016", (await CreateAsync(api, null)).Code);
        foreach (var existing in new[] { first, old, inactive })
            Assert.Equal(existing, await api.ReadAsync<ProductResponse>(await api.Client.GetAsync($"/products/{existing.Id}")));
    }

    [Fact]
    public async Task Manual_edit_checks_uniqueness_excluding_itself_and_advances_numeric_range()
    {
        await using var api = await fixture.CreateApiAsync();
        var first = await CreateAsync(api, "FIRST");
        var second = await CreateAsync(api, "SECOND");
        var unchanged = await api.ReadAsync<ProductResponse>(await api.Client.PutAsJsonAsync($"/products/{first.Id}", api.ProductInput() with { Code = " FIRST " }, ApiTestHost.Json));
        Assert.Equal(first.Code, unchanged.Code);
        await AssertDuplicateAsync(await api.Client.PutAsJsonAsync($"/products/{second.Id}", api.ProductInput() with { Code = "FIRST" }, ApiTestHost.Json));
        Assert.Equal("SECOND", (await api.ReadAsync<ProductResponse>(await api.Client.GetAsync($"/products/{second.Id}"))).Code);
        var edited = await api.ReadAsync<ProductResponse>(await api.Client.PutAsJsonAsync($"/products/{second.Id}", api.ProductInput() with { Code = " P000041 " }, ApiTestHost.Json));
        Assert.Equal("P000041", edited.Code);
        Assert.Equal("P000042", (await CreateAsync(api, null)).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Updating_an_existing_product_requires_a_code(string? code)
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await CreateAsync(api, "MANUAL");
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/products/{product.Id}", api.ProductInput() with { Code = code }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(product, await api.ReadAsync<ProductResponse>(await api.Client.GetAsync($"/products/{product.Id}")));
    }

    [Fact]
    public async Task Manual_code_length_is_bounded_and_generated_range_does_not_exceed_six_digits()
    {
        await using var api = await fixture.CreateApiAsync();
        Assert.Equal(Product.MaxCodeLength, (await CreateAsync(api, new string('X', Product.MaxCodeLength))).Code.Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/products", api.ProductInput() with { Code = new string('X', Product.MaxCodeLength + 1) }, ApiTestHost.Json)).StatusCode);
        await CreateAsync(api, "P999999");
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/products", api.ProductInput() with { Code = "" }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(2, await api.QueryAsync(db => db.Products.CountAsync()));
    }

    [Fact]
    public async Task Existing_database_index_is_unique_per_organization_and_model_needs_no_migration()
    {
        await using var api = await fixture.CreateApiAsync();
        var definition = await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value" FROM pg_indexes WHERE schemaname='public' AND indexname='IX_products_OrganizationId_Code'
            """).SingleAsync());
        Assert.Contains("CREATE UNIQUE INDEX", definition);
        Assert.Contains("(\"OrganizationId\", \"Code\")", definition);
        Assert.False(await api.QueryAsync(db => Task.FromResult(db.Database.HasPendingModelChanges())));
    }

    private static async Task<ProductResponse> CreateAsync(ApiTestHost api, string? code, ItemType type = ItemType.GOODS) =>
        await api.ReadAsync<ProductResponse>(await api.Client.PostAsJsonAsync("/products", api.ProductInput(type) with { Code = code }, ApiTestHost.Json), HttpStatusCode.Created);

    private static async Task AssertDuplicateAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PRODUCT_CODE_EXISTS", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(DuplicateMessage, body.RootElement.GetProperty("detail").GetString());
        Assert.Equal(DuplicateMessage, body.RootElement.GetProperty("errors").GetProperty("Code")[0].GetString());
    }
}
