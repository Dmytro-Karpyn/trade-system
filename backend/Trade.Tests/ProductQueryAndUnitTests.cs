using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ProductQueryAndUnitTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("drill")]
    [InlineData("дриль")]
    [InlineData("tool-001")]
    [InlineData("48200001")]
    public async Task Search_finds_name_code_and_barcode_case_insensitively(string search)
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await api.ReadAsync<ProductResponse>(await api.Client.PostAsJsonAsync("/products", api.ProductInput() with
            { Name = "Electric DRILL / Дриль", Code = "TOOL-001", Barcode = "48200001" }, ApiTestHost.Json), HttpStatusCode.Created);
        await api.CreateProductAsync();
        var results = await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync($"/products?search={search}"));
        Assert.Equal(product.Id, Assert.Single(results).Id);
    }

    [Fact]
    public async Task Search_treats_percent_and_underscore_literally_and_blank_search_preserves_existing_list()
    {
        await using var api = await fixture.CreateApiAsync();
        var literal = await api.ReadAsync<ProductResponse>(await api.Client.PostAsJsonAsync("/products", api.ProductInput() with
            { Name = "Discount 10%_only" }, ApiTestHost.Json), HttpStatusCode.Created);
        await api.CreateProductAsync();
        var found = await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products?search=%25_"));
        Assert.Equal(literal.Id, Assert.Single(found).Id);
        Assert.Equal(2, (await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products?search=%20%20"))).Length);
    }

    [Fact]
    public async Task ItemType_and_active_filters_combine_and_no_filter_includes_inactive_products()
    {
        await using var api = await fixture.CreateApiAsync();
        await api.CreateProductAsync();
        var inactiveService = await api.ReadAsync<ProductResponse>(await api.Client.PostAsJsonAsync("/products", api.ProductInput(ItemType.SERVICE) with { Active = false }, ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal(2, (await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products"))).Length);
        Assert.Equal(inactiveService.Id, Assert.Single(await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products?itemType=SERVICE&active=false"))).Id);
        Assert.Empty(await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products?itemType=GOODS&active=false")));
        Assert.Equal(ItemType.GOODS, Assert.Single(await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products?active=true"))).ItemType);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/products?itemType=UNKNOWN")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/products?itemType=99")).StatusCode);
    }

    [Fact]
    public async Task Filtered_product_search_is_organization_scoped()
    {
        await using var api = await fixture.CreateApiAsync();
        await api.CreateProductAsync();
        api.SetOrganization(Guid.NewGuid());
        Assert.Empty(await api.ReadAsync<ProductResponse[]>(await api.Client.GetAsync("/products?active=true")));
    }

    [Fact]
    public async Task Unit_lookup_starts_empty_and_inline_creation_persists_only_the_agreed_fields()
    {
        await using var api = await fixture.CreateApiAsync();
        Assert.Empty(await api.ReadAsync<UnitResponse[]>(await api.Client.GetAsync("/units")));
        var unit = await api.ReadAsync<UnitResponse>(await api.Client.PostAsJsonAsync("/units", new UnitRequest("  упаковка  "), ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal(api.OrganizationId, unit.OrganizationId); Assert.Equal("упаковка", unit.Name);
        Assert.Equal(unit, Assert.Single(await api.ReadAsync<UnitResponse[]>(await api.Client.GetAsync("/units"))));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/units", new UnitRequest(" "), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PostAsJsonAsync("/units", new UnitRequest(unit.Name), ApiTestHost.Json)).StatusCode);
        var columns = await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'units'
            """).ToArrayAsync());
        Assert.Equal(new[] { "Id", "OrganizationId", "Name" }.Order(), columns.Order());
        var product = await api.ReadAsync<ProductResponse>(await api.Client.PostAsJsonAsync("/products", api.ProductInput() with { UnitId = unit.Id }, ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal(unit.Id, product.UnitId);
        api.SetOrganization(Guid.NewGuid());
        Assert.Empty(await api.ReadAsync<UnitResponse[]>(await api.Client.GetAsync("/units")));
        Assert.Equal(HttpStatusCode.Created, (await api.Client.PostAsJsonAsync("/units", new UnitRequest(unit.Name), ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Unit_lookup_and_creation_require_permissions_from_the_authenticated_user()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var (_, client, role) = await api.LimitedUserAsync("product.view"); using var session = client;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/units")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/units", new UnitRequest("година"), ApiTestHost.Json)).StatusCode);
        await api.ReadAsync<Trade.Application.Authentication.RoleResponse>(await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions",
            new Trade.Application.Authentication.RolePermissionsRequest(["product.create"]), ApiTestHost.Json));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/units", new UnitRequest("година"), ApiTestHost.Json)).StatusCode);
    }
    [Fact]
    public async Task Known_directory_units_cannot_be_used_by_products_of_another_organization()
    {
        await using var api = await fixture.CreateApiAsync();
        var foreign = await api.ReadAsync<UnitResponse>(await api.Client.PostAsJsonAsync("/units", new UnitRequest("box"), ApiTestHost.Json), HttpStatusCode.Created);
        api.SetOrganization(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/products", api.ProductInput() with { UnitId = foreign.Id }, ApiTestHost.Json)).StatusCode);
        var product = await api.CreateProductAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/products/{product.Id}", api.ProductInput() with { UnitId = foreign.Id }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(product.UnitId, (await api.ReadAsync<ProductResponse>(await api.Client.GetAsync($"/products/{product.Id}"))).UnitId);
    }

}
