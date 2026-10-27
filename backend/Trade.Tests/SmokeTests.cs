using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SmokeTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Migrated_API_exposes_health_and_OpenAPI_against_its_isolated_PostgreSQL_database()
    {
        await using var api = await fixture.CreateApiAsync();
        var health = await api.ReadAsync<HealthResponse>(await api.Client.GetAsync("/health"));
        Assert.Equal("Healthy", health.Status);
        using var openApi = JsonDocument.Parse(await api.Client.GetStringAsync("/openapi/v1.json"));
        var paths = openApi.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/health", out _));
        Assert.True(paths.TryGetProperty("/products", out _));
        Assert.True(paths.TryGetProperty("/purchases/{id}/post", out _));
        Assert.True(paths.TryGetProperty("/stock/balances", out _));
        Assert.True(paths.TryGetProperty("/sales", out _));
        Assert.True(paths.TryGetProperty("/sales/{id}/post", out _));
        Assert.True(paths.TryGetProperty("/stock/available-batches", out _));
        Assert.True(paths.TryGetProperty("/price-types", out _));
        Assert.True(paths.TryGetProperty("/counterparties", out _));
        Assert.True(paths.TryGetProperty("/settlements/balances", out _));
        Assert.True(paths.TryGetProperty("/settlements/counterparties/{id}", out _));
        Assert.True(paths.TryGetProperty("/money/accounts", out _));
        Assert.True(paths.TryGetProperty("/money/balances", out _));
        Assert.True(paths.TryGetProperty("/expense-categories", out _));
        Assert.True(paths.TryGetProperty("/expense-categories/{id}", out _));
        Assert.True(paths.TryGetProperty("/expenses", out _));
        Assert.True(paths.TryGetProperty("/expenses/{id}", out _));
        Assert.True(paths.TryGetProperty("/expenses/{id}/post", out _));
        Assert.True(paths.TryGetProperty("/expenses/{id}/unpost", out _));
        Assert.True(paths.TryGetProperty("/customer-returns", out _));
        Assert.True(paths.TryGetProperty("/customer-returns/{id}", out _));
        Assert.True(paths.TryGetProperty("/customer-returns/{id}/post", out _));
        Assert.True(paths.TryGetProperty("/customer-returns/{id}/unpost", out _));
        Assert.True(paths.TryGetProperty("/sales/{id}/customer-return", out _));
        Assert.True(paths.TryGetProperty("/sales/{id}/returnable-lines", out _));
        Assert.True(paths.TryGetProperty("/supplier-returns", out _));
        Assert.True(paths.TryGetProperty("/supplier-returns/{id}", out _));
        Assert.True(paths.TryGetProperty("/supplier-returns/{id}/post", out _));
        Assert.True(paths.TryGetProperty("/supplier-returns/{id}/unpost", out _));
        Assert.True(paths.TryGetProperty("/purchases/{id}/supplier-return", out _));
        Assert.True(paths.TryGetProperty("/purchases/{id}/returnable-lines", out _));
        Assert.True(paths.TryGetProperty("/stock-transfers", out _));
        Assert.True(paths.TryGetProperty("/stock-transfers/{id}", out _));
        Assert.True(paths.TryGetProperty("/stock-transfers/{id}/post", out _));
        Assert.True(paths.TryGetProperty("/stock-transfers/{id}/unpost", out _));
        Assert.True(paths.TryGetProperty("/write-off-reasons", out _));
        Assert.True(paths.TryGetProperty("/write-off-reasons/{id}", out _));
        Assert.True(paths.TryGetProperty("/write-offs", out _));
        Assert.True(paths.TryGetProperty("/write-offs/{id}", out _));
        Assert.True(paths.TryGetProperty("/write-offs/{id}/post", out _));
        Assert.True(paths.TryGetProperty("/write-offs/{id}/unpost", out _));
        foreach (var kind in new[] { "receipts", "payments", "transfers" })
        {
            Assert.True(paths.TryGetProperty($"/money/{kind}", out _));
            Assert.True(paths.TryGetProperty($"/money/{kind}/{{id}}/post", out _));
            Assert.True(paths.TryGetProperty($"/money/{kind}/{{id}}/unpost", out _));
        }
        Assert.True(paths.TryGetProperty("/batches/{batchId}/prices/{priceTypeId}", out _));
        Assert.True(paths.TryGetProperty("/products/{productId}/prices/{priceTypeId}", out _));
        var schemas = openApi.RootElement.GetProperty("components").GetProperty("schemas");
        var saleInput = schemas.GetProperty("SaleLineRequest").GetProperty("properties");
        Assert.False(saleInput.TryGetProperty("recommendedUnitPrice", out _));
        Assert.False(saleInput.TryGetProperty("canSellBelowRecommendedPrice", out _));
        Assert.Contains(paths.GetProperty("/sales/{id}").GetProperty("delete").GetProperty("parameters").EnumerateArray(),
            p => p.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        var productPostParameters = paths.GetProperty("/products").GetProperty("post").GetProperty("parameters");
        Assert.Contains(productPostParameters.EnumerateArray(), p => p.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        Assert.DoesNotContain(productPostParameters.EnumerateArray(), p => p.GetProperty("name").GetString() is "X-Actor-Id" or "X-Organization-Id");
        var name = await api.QueryAsync(db => db.Database.SqlQueryRaw<string>(
            """
            SELECT current_database() AS "Value"
            """).SingleAsync());
        Assert.Equal(api.DatabaseName, name);
        Assert.Empty(await api.QueryAsync(db => db.Database.GetPendingMigrationsAsync()));
    }
}
