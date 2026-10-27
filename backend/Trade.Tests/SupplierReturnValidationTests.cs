using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Returns;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SupplierReturnValidationTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.00001")]
    [InlineData("10000000000000000")]
    public async Task Quantity_must_be_positive_numeric_20_4_on_create_and_update(string raw)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var value = decimal.Parse(raw, CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(value), ApiTestHost.Json)).StatusCode);
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput());
        var edit = result.EditSupplierReturn();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { Quantity = value }] }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(2m, (await api.BalanceAsync(stock)).ReservedQuantity);
    }

    [Fact]
    public async Task Duplicate_wrong_purchase_removed_unknown_and_foreign_line_IDs_are_rejected()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var other = await api.ReceiveAsync();
        var input = stock.SupplierInput();
        foreach (var lines in new SupplierReturnLineInput[][]
        {
            [input.Lines[0], input.Lines[0]], [new(null, other.Purchase.Lines.Single().Id, 1m)],
            [new(null, Guid.Empty, 1m)], [new(null, Guid.NewGuid(), 1m)],
            [input.Lines[0] with { Id = Guid.NewGuid() }]
        }) Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/supplier-returns", input with { Lines = lines }, ApiTestHost.Json)).StatusCode);
        var draft = await api.CreatePurchaseAsync();
        var edit = ApiTestHost.Edit(draft);
        draft = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{draft.Document.Id}", edit with
            { Lines = [new(null, 2, edit.Lines[0].ProductId, 10m, 10m)] }, ApiTestHost.Json));
        await api.PostAsync(draft.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/supplier-returns", input with
        {
            SourcePurchaseInvoiceId = draft.Document.Id, StorageLocationId = draft.StorageLocationId,
            Lines = [new(null, draft.Lines.Single(x => x.RemovedAt != null).Id, 1m)]
        }, ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Organization_isolation_covers_source_storage_lines_lists_and_all_document_operations()
    {
        await using var api = await fixture.CreateApiAsync();
        var local = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(local.SupplierInput());
        api.SetOrganization(Guid.NewGuid());
        var foreign = await api.ReceiveAsync();
        var foreignReturn = await api.CreateSupplierReturnAsync(foreign.SupplierInput());
        api.SetOrganization(api.OrganizationId);
        foreach (var bad in new[]
        {
            local.SupplierInput() with { SourcePurchaseInvoiceId = foreign.Purchase.Document.Id },
            local.SupplierInput() with { StorageLocationId = foreign.LocationId },
            local.SupplierInput() with { StorageLocationId = Guid.NewGuid() },
            local.SupplierInput() with { StorageLocationId = Guid.Empty },
            local.SupplierInput() with { SourcePurchaseInvoiceId = Guid.Empty },
            local.SupplierInput() with { Lines = foreign.SupplierInput().Lines }
        })
        {
            var response = await api.Client.PostAsJsonAsync("/supplier-returns", bad, ApiTestHost.Json);
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound });
            response = await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}", result.EditSupplierReturn() with
            {
                SourcePurchaseInvoiceId = bad.SourcePurchaseInvoiceId, StorageLocationId = bad.StorageLocationId, Lines = bad.Lines
            }, ApiTestHost.Json);
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound });
        }
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/supplier-returns/{foreignReturn.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/supplier-returns/{foreignReturn.Document.Id}", foreignReturn.EditSupplierReturn(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/supplier-returns/{foreignReturn.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/supplier-returns/{foreignReturn.Document.Id}/unpost", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.DeleteAsync($"/supplier-returns/{foreignReturn.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/purchases/{foreign.Purchase.Document.Id}/returnable-lines")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync($"/purchases/{local.Purchase.Document.Id}/returnable-lines?storageLocationId={foreign.LocationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsJsonAsync($"/purchases/{foreign.Purchase.Document.Id}/supplier-return",
            new SupplierReturnFromPurchaseRequest(new DateOnly(2026, 10, 20)), ApiTestHost.Json)).StatusCode);
        Assert.Equal(result.Document.Id, Assert.Single(await api.ReadAsync<SupplierReturnResponse[]>(await api.Client.GetAsync("/supplier-returns"))).Document.Id);
    }

    [Fact]
    public async Task Concurrent_creation_and_purchase_UNPOST_cannot_leave_an_invalid_dependency()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var responses = await Task.WhenAll(api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(), ApiTestHost.Json),
            api.Client.PostAsync($"/purchases/{stock.Purchase.Document.Id}/unpost", null));
        if (responses[0].StatusCode == HttpStatusCode.Created)
            await ExpenseTestSupport.ErrorCodeAsync(responses[1], "DOCUMENT_HAS_DEPENDENCIES", HttpStatusCode.Conflict);
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, responses[0].StatusCode);
            Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
            Assert.Empty(await api.QueryAsync(db => db.SupplierReturns.ToListAsync()));
        }
    }

    [Fact]
    public async Task SERVICE_corruption_in_a_source_purchase_cannot_be_used_as_a_supplier_return()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE products SET "ItemType" = 'SERVICE' WHERE "Id" = {stock.ProductId}
            """));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(), ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.SupplierReturnLines.ToListAsync()));
    }
}
