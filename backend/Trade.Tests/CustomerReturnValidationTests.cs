using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Returns;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class CustomerReturnValidationTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_distinct_returns_cannot_exceed_GOODS_quantity_or_SERVICE_refund(bool service)
    {
        await using var api = await fixture.CreateApiAsync();
        SaleResponse sale;
        if (service) sale = await api.PostSaleAsync((await api.ServiceSaleForAsync(api.CounterpartyId, 100m)).Document.Id);
        else sale = (await api.SoldGoodsAsync()).Sale;
        var input = service ? sale.ReturnInput(0m, 60m) : sale.ReturnInput(3m);
        var a = await api.CreateReturnAsync(input);
        var b = await api.CreateReturnAsync(input);
        var responses = await Task.WhenAll(api.Client.PostAsync($"/customer-returns/{a.Document.Id}/post", null),
            api.Client.PostAsync($"/customer-returns/{b.Document.Id}/post", null));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        await ExpenseTestSupport.ErrorCodeAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict),
            service ? "RETURN_AMOUNT_EXCEEDED" : "RETURN_QUANTITY_EXCEEDED", HttpStatusCode.Conflict);
        var totals = Assert.Single(await api.ReturnableAsync(sale.Document.Id));
        Assert.Equal(service ? 40m : 10.8m, totals.RefundableAmount);
        Assert.Equal(1, await api.QueryAsync(db => db.SettlementMovements.CountAsync(x => x.DocumentId == a.Document.Id || x.DocumentId == b.Document.Id)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.00001")]
    [InlineData("10000000000000000")]
    public async Task Invalid_GOODS_quantity_is_rejected_on_create_and_update(string raw)
    {
        await using var api = await fixture.CreateApiAsync();
        var (_, sale) = await api.SoldGoodsAsync();
        var value = decimal.Parse(raw, CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/customer-returns", sale.ReturnInput(value), ApiTestHost.Json)).StatusCode);
        var result = await api.CreateReturnAsync(sale.ReturnInput());
        var edit = result.EditReturn();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/customer-returns/{result.Document.Id}",
            edit with { Lines = [edit.Lines[0] with { Quantity = value }] }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(result.Document, (await api.ReadAsync<CustomerReturnResponse>(await api.Client.GetAsync($"/customer-returns/{result.Document.Id}"))).Document);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.00001")]
    [InlineData("101")]
    public async Task Invalid_SERVICE_refund_is_rejected(string raw)
    {
        await using var api = await fixture.CreateApiAsync();
        var sale = await api.PostSaleAsync((await api.ServiceSaleForAsync(api.CounterpartyId, 100m)).Document.Id);
        var response = await api.Client.PostAsJsonAsync("/customer-returns", sale.ReturnInput(0m, decimal.Parse(raw, CultureInfo.InvariantCulture)), ApiTestHost.Json);
        Assert.Equal(raw == "101" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GOODS_rejects_manual_refund_and_duplicate_foreign_or_removed_source_lines()
    {
        await using var api = await fixture.CreateApiAsync();
        var (stock, sale) = await api.SoldGoodsAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/customer-returns", sale.ReturnInput(1m, 10.8m), ApiTestHost.Json)).StatusCode);
        var input = sale.ReturnInput();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/customer-returns", input with
            { Lines = [input.Lines[0], input.Lines[0]] }, ApiTestHost.Json)).StatusCode);
        var other = await api.CreateSaleAsync(stock, 1m);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/customer-returns", input with
            { Lines = [new(null, other.Lines.Single().Id, 1m)] }, ApiTestHost.Json)).StatusCode);
        var extra = await api.CreateProductAsync(ItemType.SERVICE);
        other = await api.UpdateSaleAsync(other.Document.Id, SalesTestSupport.Edit(other) with
            { Lines = [new(null, 1, extra.Id, null, 1m, 10m)] });
        other = await api.UpdateSaleAsync(other.Document.Id, SalesTestSupport.Edit(other) with
            { Lines = [new(null, 1, extra.Id, null, 1m, 20m)] });
        other = await api.PostSaleAsync(other.Document.Id);
        var removed = other.Lines.First(x => x.RemovedAt != null).Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/customer-returns", input with
            { SourceSaleInvoiceId = other.Document.Id, Lines = [new(null, removed, 0m, 1m)] }, ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Organization_isolation_rejects_foreign_sale_lines_storage_and_all_document_operations()
    {
        await using var api = await fixture.CreateApiAsync();
        var (_, localSale) = await api.SoldGoodsAsync();
        var local = await api.CreateReturnAsync(localSale.ReturnInput());
        api.SetOrganization(Guid.NewGuid());
        var (_, foreignSale) = await api.SoldGoodsAsync();
        var foreign = await api.CreateReturnAsync(foreignSale.ReturnInput());
        api.SetOrganization(api.OrganizationId);
        foreach (var bad in new[]
        {
            localSale.ReturnInput() with { SourceSaleInvoiceId = foreignSale.Document.Id },
            localSale.ReturnInput() with { StorageLocationId = foreignSale.StorageLocationId },
            localSale.ReturnInput() with { StorageLocationId = Guid.NewGuid() },
            localSale.ReturnInput() with { Lines = [new(null, foreignSale.Lines.Single().Id, 1m)] }
        })
        {
            var response = await api.Client.PostAsJsonAsync("/customer-returns", bad, ApiTestHost.Json);
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound });
            var edit = local.EditReturn() with { SourceSaleInvoiceId = bad.SourceSaleInvoiceId,
                StorageLocationId = bad.StorageLocationId, Lines = bad.Lines };
            response = await api.Client.PutAsJsonAsync($"/customer-returns/{local.Document.Id}", edit, ApiTestHost.Json);
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound });
        }
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/customer-returns/{foreign.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/sales/{foreignSale.Document.Id}/returnable-lines")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsJsonAsync($"/sales/{foreignSale.Document.Id}/customer-return",
            new CustomerReturnFromSaleRequest(new DateOnly(2026, 10, 20)), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/customer-returns/{foreign.Document.Id}", foreign.EditReturn(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/customer-returns/{foreign.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/customer-returns/{foreign.Document.Id}/unpost", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.DeleteAsync($"/customer-returns/{foreign.Document.Id}")).StatusCode);
        Assert.Equal(local.Document.Id, Assert.Single(await api.ReadAsync<CustomerReturnResponse[]>(await api.Client.GetAsync("/customer-returns"))).Document.Id);
    }

    [Fact]
    public async Task Concurrent_creation_and_source_sale_UNPOST_cannot_leave_an_invalid_dependency()
    {
        await using var api = await fixture.CreateApiAsync();
        var (_, sale) = await api.SoldGoodsAsync();
        var responses = await Task.WhenAll(api.Client.PostAsJsonAsync("/customer-returns", sale.ReturnInput(), ApiTestHost.Json),
            api.Client.PostAsync($"/sales/{sale.Document.Id}/unpost", null));
        if (responses[0].StatusCode == HttpStatusCode.Created)
            await ExpenseTestSupport.ErrorCodeAsync(responses[1], "DOCUMENT_HAS_DEPENDENCIES", HttpStatusCode.Conflict);
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, responses[0].StatusCode);
            Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
            Assert.Empty(await api.QueryAsync(db => db.CustomerReturns.ToListAsync()));
        }
    }

    [Theory]
    [InlineData("0.0001")]
    [InlineData("0")]
    public async Task Many_small_GOODS_returns_reconcile_rounding_and_allow_zero_value_goods(string price)
    {
        await using var api = await fixture.CreateApiAsync();
        var (_, sale) = await api.SoldGoodsAsync(quantity: 3m, discount: 50m, price: decimal.Parse(price, CultureInfo.InvariantCulture));
        for (var i = 0; i < 3; i++) await api.PostReturnAsync((await api.CreateReturnAsync(sale.ReturnInput())).Document.Id);
        var values = Assert.Single(await api.ReturnableAsync(sale.Document.Id));
        Assert.Equal(values.OriginalNetAmount, values.AlreadyRefundedAmount);
        Assert.Equal(0m, values.RefundableAmount);
        Assert.Equal(0m, values.ReturnableQuantity);
        Assert.True(await api.QueryAsync(db => db.CustomerReturnLines.AllAsync(x => x.RefundAmount >= 0)));
    }
}
