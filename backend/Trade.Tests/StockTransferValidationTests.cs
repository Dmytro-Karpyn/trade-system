using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Trade.Application;
using Trade.Domain.Inventory;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class StockTransferValidationTests(DatabaseFixture fixture)
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
        var destination = await api.KnownLocationAsync();
        var value = decimal.Parse(raw, CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/stock-transfers", stock.TransferInput(destination, value), ApiTestHost.Json)).StatusCode);
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination));
        var edit = transfer.EditTransfer();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { Quantity = value }] }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(2m, (await api.LocationStockAsync(stock.BatchId, stock.LocationId)).Reserved);
    }

    [Fact]
    public async Task Required_locations_must_differ_and_batches_line_numbers_and_line_IDs_are_validated()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var other = await api.ReceiveAsync(locationId: stock.LocationId);
        var input = stock.TransferInput(await api.KnownLocationAsync());
        foreach (var bad in new[]
        {
            input with { FromLocationId = Guid.Empty }, input with { ToLocationId = Guid.Empty },
            input with { ToLocationId = stock.LocationId }, input with { FromLocationId = Guid.NewGuid() },
            input with { ToLocationId = Guid.NewGuid() }, input with { DocumentDate = default }, input with { Lines = null! },
            input with { Lines = [null!] }, input with { Lines = [input.Lines[0] with { BatchId = Guid.Empty }] },
            input with { Lines = [input.Lines[0] with { BatchId = Guid.NewGuid() }] },
            input with { Lines = [input.Lines[0] with { LineNo = 0 }] }, input with { Lines = [input.Lines[0] with { LineNo = -1 }] },
            input with { Lines = [input.Lines[0] with { Id = Guid.NewGuid() }] },
            input with { Lines = [input.Lines[0], input.Lines[0] with { LineNo = 2 }] },
            input with { Lines = [input.Lines[0], new StockTransferLineInput(null, 1, other.BatchId, 1m)] }
        }) Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/stock-transfers", bad, ApiTestHost.Json)).StatusCode);
        var transfer = await api.CreateTransferAsync(input);
        var edit = transfer.EditTransfer();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", edit with
            { ToLocationId = edit.FromLocationId }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", edit with
            { Lines = [edit.Lines[0], new(null, 2, stock.BatchId, 1m)] }, ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Organization_isolation_covers_both_locations_batch_and_all_document_operations()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination));
        api.SetOrganization(Guid.NewGuid());
        var foreign = await api.ReceiveAsync();
        var foreignDestination = await api.KnownLocationAsync();
        var foreignTransfer = await api.CreateTransferAsync(foreign.TransferInput(foreignDestination));
        api.SetOrganization(api.OrganizationId);
        var input = stock.TransferInput(destination);
        foreach (var bad in new[]
        {
            input with { FromLocationId = foreign.LocationId }, input with { ToLocationId = foreignDestination },
            input with { Lines = [new(null, 1, foreign.BatchId, 1m)] }
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/stock-transfers", bad, ApiTestHost.Json)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", transfer.EditTransfer() with
                { FromLocationId = bad.FromLocationId, ToLocationId = bad.ToLocationId, Lines = bad.Lines }, ApiTestHost.Json)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/stock-transfers/{foreignTransfer.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/stock-transfers/{foreignTransfer.Document.Id}", foreignTransfer.EditTransfer(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/stock-transfers/{foreignTransfer.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/stock-transfers/{foreignTransfer.Document.Id}/unpost", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.DeleteAsync($"/stock-transfers/{foreignTransfer.Document.Id}")).StatusCode);
        Assert.Equal(transfer.Document.Id, Assert.Single(await api.ReadAsync<StockTransferResponse[]>(await api.Client.GetAsync("/stock-transfers"))).Document.Id);
        Assert.Empty(await api.ReadAsync<StockTransferResponse[]>(await api.Client.GetAsync($"/stock-transfers?fromLocationId={foreign.LocationId}")));
    }
}
