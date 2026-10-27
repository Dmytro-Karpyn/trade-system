using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Trade.Application;
using Trade.Domain.Inventory;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class WriteOffValidationTests(DatabaseFixture fixture)
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
        var reason = await api.CreateReasonAsync();
        var value = decimal.Parse(raw, CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/write-offs", stock.WriteOffInput(reason.Id, value), ApiTestHost.Json)).StatusCode);
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id));
        var edit = writeOff.EditWriteOff();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { Quantity = value }] }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(2m, (await api.LocationStockAsync(stock.BatchId, stock.LocationId)).Reserved);
    }

    [Fact]
    public async Task Reason_requires_name_and_category_and_writeoff_rejects_invalid_refs_duplicate_batches_and_line_numbers()
    {
        await using var api = await fixture.CreateApiAsync();
        var reason = await api.CreateReasonAsync();
        foreach (var name in new[] { "", "  ", null! })
        {
            var bad = new WriteOffReasonRequest(name, reason.ExpenseCategoryId);
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/write-off-reasons", bad, ApiTestHost.Json)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/write-off-reasons/{reason.Id}", bad, ApiTestHost.Json)).StatusCode);
        }
        foreach (var id in new[] { Guid.Empty, Guid.NewGuid() })
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/write-off-reasons", new WriteOffReasonRequest("Invalid", id), ApiTestHost.Json)).StatusCode);
        var stock = await api.ReceiveAsync();
        var other = await api.ReceiveAsync(locationId: stock.LocationId);
        var input = stock.WriteOffInput(reason.Id);
        foreach (var bad in new[]
        {
            input with { StorageLocationId = Guid.Empty }, input with { StorageLocationId = Guid.NewGuid() },
            input with { WriteOffReasonId = Guid.Empty }, input with { WriteOffReasonId = Guid.NewGuid() },
            input with { DocumentDate = default }, input with { Lines = null! }, input with { Lines = [null!] },
            input with { Lines = [input.Lines[0] with { BatchId = Guid.Empty }] }, input with { Lines = [input.Lines[0] with { BatchId = Guid.NewGuid() }] },
            input with { Lines = [input.Lines[0] with { LineNo = 0 }] }, input with { Lines = [input.Lines[0] with { Id = Guid.NewGuid() }] },
            input with { Lines = [input.Lines[0], input.Lines[0] with { LineNo = 2 }] },
            input with { Lines = [input.Lines[0], new WriteOffLineInput(null, 1, other.BatchId, 1m)] }
        }) Assert.Contains((await api.Client.PostAsJsonAsync("/write-offs", bad, ApiTestHost.Json)).StatusCode,
            new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound });
        var writeOff = await api.CreateWriteOffAsync(input);
        var edit = writeOff.EditWriteOff();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", edit with
            { Lines = [edit.Lines[0], new(null, 2, stock.BatchId, 1m)] }, ApiTestHost.Json)).StatusCode);
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/write-offs", stock.WriteOffInput(reason.Id, 11m), ApiTestHost.Json));
    }

    [Fact]
    public async Task Organization_isolation_covers_category_reason_storage_batch_filters_and_all_document_operations()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var reason = await api.CreateReasonAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id));
        api.SetOrganization(Guid.NewGuid());
        var foreign = await api.ReceiveAsync();
        var foreignReason = await api.CreateReasonAsync();
        var foreignWriteOff = await api.CreateWriteOffAsync(foreign.WriteOffInput(foreignReason.Id));
        api.SetOrganization(api.OrganizationId);
        var badReason = new WriteOffReasonRequest("Foreign category", foreignReason.ExpenseCategoryId);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/write-off-reasons", badReason, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/write-off-reasons/{reason.Id}", badReason, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/write-off-reasons/{foreignReason.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/write-off-reasons/{foreignReason.Id}", badReason, ApiTestHost.Json)).StatusCode);
        Assert.Equal(reason.Id, Assert.Single(await api.ReadAsync<WriteOffReasonResponse[]>(await api.Client.GetAsync("/write-off-reasons"))).Id);
        var input = stock.WriteOffInput(reason.Id);
        foreach (var bad in new[] { input with { StorageLocationId = foreign.LocationId }, input with { WriteOffReasonId = foreignReason.Id },
            input with { Lines = [new(null, 1, foreign.BatchId, 1m)] } })
        {
            Assert.Contains((await api.Client.PostAsJsonAsync("/write-offs", bad, ApiTestHost.Json)).StatusCode,
                new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound });
            Assert.Contains((await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", writeOff.EditWriteOff() with
                { StorageLocationId = bad.StorageLocationId, WriteOffReasonId = bad.WriteOffReasonId, Lines = bad.Lines }, ApiTestHost.Json)).StatusCode,
                new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound });
        }
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/write-offs/{foreignWriteOff.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/write-offs/{foreignWriteOff.Document.Id}", foreignWriteOff.EditWriteOff(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/write-offs/{foreignWriteOff.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/write-offs/{foreignWriteOff.Document.Id}/unpost", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.DeleteAsync($"/write-offs/{foreignWriteOff.Document.Id}")).StatusCode);
        Assert.Equal(writeOff.Document.Id, Assert.Single(await api.ReadAsync<WriteOffResponse[]>(await api.Client.GetAsync("/write-offs"))).Document.Id);
        foreach (var filter in new[] { $"storageLocationId={foreign.LocationId}", $"writeOffReasonId={foreignReason.Id}", $"expenseCategoryId={foreignReason.ExpenseCategoryId}" })
            Assert.Empty(await api.ReadAsync<WriteOffResponse[]>(await api.Client.GetAsync($"/write-offs?{filter}")));
        Assert.Equal(writeOff.Document.Id, Assert.Single(await api.WriteOffCostsAsync()).WriteOffId);
    }
}
