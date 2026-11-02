using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Catalog;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ReturnUiQueryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Lookups_include_only_posted_sources_own_references_and_used_locations_without_opening_stock()
    {
        await using var api = await fixture.CreateApiAsync();
        var (stock, sale) = await api.SoldGoodsAsync(); var draft = await api.CreateSaleAsync(stock);
        var opening = await api.CreatePurchaseAsync(type: DocumentType.OPENING_STOCK); await api.PostAsync(opening.Document.Id);
        var unused = await api.ReadAsync<StorageLocationResponse>(await api.Client.PostAsJsonAsync("/storage-locations", new StorageLocationRequest("Unused"), ApiTestHost.Json), HttpStatusCode.Created);
        var customer = await api.ReadAsync<ReturnLookups>(await api.Client.GetAsync("/customer-returns/lookups"));
        Assert.Equal(sale.Document.Id, Assert.Single(customer.Sources).Id); Assert.DoesNotContain(customer.Sources, x => x.Id == draft.Document.Id);
        Assert.Contains(customer.Batches, x => x.BatchId == stock.BatchId); Assert.Contains(customer.Products, x => x.ProductId == stock.ProductId);
        Assert.DoesNotContain(customer.Locations, x => x.Id == unused.Id);
        var supplier = await api.ReadAsync<ReturnLookups>(await api.Client.GetAsync("/supplier-returns/lookups"));
        Assert.Equal(stock.Purchase.Document.Id, Assert.Single(supplier.Sources).Id);
        api.SetOrganization(Guid.NewGuid());
        foreach (var root in new[] { "customer-returns", "supplier-returns" })
        {
            var foreign = await api.ReadAsync<ReturnLookups>(await api.Client.GetAsync($"/{root}/lookups"));
            Assert.Empty(foreign.Sources); Assert.Empty(foreign.Locations); Assert.Empty(foreign.Products); Assert.Empty(foreign.Batches);
        }
    }

    [Fact]
    public async Task Customer_preview_uses_domain_refund_rounding_and_does_not_write_or_reserve_anything()
    {
        await using var api = await fixture.CreateApiAsync(); var (stock, sale) = await api.SoldGoodsAsync();
        var before = await api.BalanceAsync(stock); var count = await api.QueryAsync(db => db.Documents.CountAsync());
        var input = sale.ReturnInput(2m);
        var preview = await api.ReadAsync<ReturnPreview>(await api.Client.PostAsJsonAsync("/customer-returns/preview", input, ApiTestHost.Json));
        Assert.Equal(21.6m, preview.TotalRefund); Assert.Equal(21.6m, Assert.Single(preview.Lines).RefundAmount);
        Assert.Equal(count, await api.QueryAsync(db => db.Documents.CountAsync())); Assert.Equal(before, await api.BalanceAsync(stock));
        Assert.Empty(await api.QueryAsync(db => db.CustomerReturns.ToListAsync()));
        var result = await api.CreateReturnAsync(input);
        var saved = await api.ReadAsync<ReturnPreview>(await api.Client.GetAsync($"/customer-returns/{result.Document.Id}/refund"));
        Assert.Equal(preview.TotalRefund, saved.TotalRefund); await api.PostReturnAsync(result.Document.Id);
        Assert.Equal(-preview.TotalRefund, Assert.Single(await api.SettlementsAsync(result.Document.Id)).Amount);
    }

    [Fact]
    public async Task Mixed_preview_supports_service_partial_refund_and_enforces_remaining_financial_limit_without_writes()
    {
        await using var api = await fixture.CreateApiAsync(); var stock = await api.ReceiveAsync(); var service = await api.CreateProductAsync(ItemType.SERVICE);
        var sale = await api.CreateSaleAsync(stock.SaleInput() with { DiscountPercent = 10m,
            Lines = [new(null, 1, stock.ProductId, stock.BatchId, 4m, 12m), new(null, 2, service.Id, null, 1m, 100m)] });
        sale = await api.PostSaleAsync(sale.Document.Id);
        var input = new CustomerReturnRequest(new DateOnly(2026, 10, 20), sale.Document.Id, sale.StorageLocationId,
            [new(null, sale.Lines.Single(x => x.LineNo == 1).Id, 2m), new(null, sale.Lines.Single(x => x.LineNo == 2).Id, 1m, 30m)]);
        var preview = await api.ReadAsync<ReturnPreview>(await api.Client.PostAsJsonAsync("/customer-returns/preview", input, ApiTestHost.Json));
        Assert.Equal(51.6m, preview.TotalRefund); Assert.Equal(2, preview.Lines.Count);
        Assert.Empty(await api.QueryAsync(db => db.CustomerReturns.ToListAsync()));
        var result = await api.CreateReturnAsync(input); await api.PostReturnAsync(result.Document.Id);
        await ExpenseTestSupport.ErrorCodeAsync(await api.Client.PostAsJsonAsync("/customer-returns/preview", input with {
            Lines = [input.Lines[1] with { RefundAmount = 61m }] }, ApiTestHost.Json), "RETURN_AMOUNT_EXCEEDED", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Supplier_preview_and_saved_refund_exclude_direct_expenses_and_match_posted_settlement()
    {
        await using var api = await fixture.CreateApiAsync(); var stock = await api.ReceiveAsync();
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(stock.Purchase.Document.Id, 100m)); await api.PostExpenseAsync(expense.Document.Id);
        var before = await api.BalanceAsync(stock); var count = await api.QueryAsync(db => db.Documents.CountAsync());
        var preview = await api.ReadAsync<ReturnPreview>(await api.Client.PostAsJsonAsync("/supplier-returns/preview", stock.SupplierInput(2m), ApiTestHost.Json));
        Assert.Equal(20m, preview.TotalRefund); Assert.Equal(20m, Assert.Single(preview.Lines).RefundAmount);
        Assert.Equal(count, await api.QueryAsync(db => db.Documents.CountAsync())); Assert.Equal(before, await api.BalanceAsync(stock));
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput(2m));
        var saved = await api.ReadAsync<ReturnPreview>(await api.Client.GetAsync($"/supplier-returns/{result.Document.Id}/refund"));
        Assert.Equal(preview.TotalRefund, saved.TotalRefund); await api.PostSupplierReturnAsync(result.Document.Id);
        Assert.Equal(preview.TotalRefund, Assert.Single(await api.SettlementsAsync(result.Document.Id)).Amount);
    }

    [Fact]
    public async Task Supplier_edit_query_excludes_only_own_draft_reserve_at_its_saved_location_without_changing_public_balances()
    {
        await using var api = await fixture.CreateApiAsync(); var stock = await api.ReceiveAsync();
        var own = await api.CreateSupplierReturnAsync(stock.SupplierInput(8m));
        async Task<SupplierReturnableLineResponse> Read(Guid? location = null) => Assert.Single(await api.ReadAsync<SupplierReturnableLineResponse[]>(
            await api.Client.GetAsync($"/purchases/{stock.Purchase.Document.Id}/returnable-lines?excludeSupplierReturnId={own.Document.Id}&storageLocationId={location ?? stock.LocationId}")));
        var row = await Read(); Assert.Equal((10m, 8m, 2m, 10m, 10m), (row.PhysicalQuantity, row.ReservedQuantity, row.AvailableQuantity, row.AvailableForDocument, row.MaxReturnableQuantity));
        var otherLocation = (await api.CreatePurchaseAsync()).StorageLocationId; row = await Read(otherLocation); Assert.Equal(0m, row.AvailableForDocument);
        var otherSource = await api.ReceiveAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync($"/purchases/{otherSource.Purchase.Document.Id}/returnable-lines?excludeSupplierReturnId={own.Document.Id}")).StatusCode);
        await api.PostSupplierReturnAsync(own.Document.Id); Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync($"/purchases/{stock.Purchase.Document.Id}/returnable-lines?excludeSupplierReturnId={own.Document.Id}")).StatusCode);
        api.SetOrganization(Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/purchases/{stock.Purchase.Document.Id}/returnable-lines?excludeSupplierReturnId={own.Document.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overview_derived_total_and_filters_use_source_party_and_currency_and_ignore_logically_deleted(bool supplier)
    {
        await using var api = await fixture.CreateApiAsync(); var (stock, sale) = await api.SoldGoodsAsync();
        var root = supplier ? "supplier-returns" : "customer-returns"; var sourceId = supplier ? stock.Purchase.Document.Id : sale.Document.Id;
        var id = supplier ? (await api.CreateSupplierReturnAsync(stock.SupplierInput())).Document.Id : (await api.CreateReturnAsync(sale.ReturnInput(2m))).Document.Id;
        var key = supplier ? "sourcePurchaseInvoiceId" : "sourceSaleInvoiceId";
        var row = Assert.Single(await api.ReadAsync<ReturnListItem[]>(await api.Client.GetAsync($"/{root}/overview?from=2026-10-20&to=2026-10-20&counterpartyId={supplier switch { true => stock.Purchase.CounterpartyId, false => sale.CounterpartyId }}&status=DRAFT&{key}={sourceId}")));
        Assert.Equal(id, row.Id); Assert.NotEmpty(row.CounterpartyName); Assert.NotNull(row.SourceNumber); Assert.Equal(supplier ? 20m : 21.6m, row.TotalRefund);
        Assert.Empty(await api.ReadAsync<ReturnListItem[]>(await api.Client.GetAsync($"/{root}/overview?status=POSTED")));
        api.SetOrganization(Guid.NewGuid()); Assert.Empty(await api.ReadAsync<ReturnListItem[]>(await api.Client.GetAsync($"/{root}/overview")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/{root}/{id}/refund")).StatusCode);
        api.SetOrganization(stock.Purchase.Document.OrganizationId);
        await api.Client.DeleteAsync($"/{root}/{id}"); Assert.Empty(await api.ReadAsync<ReturnListItem[]>(await api.Client.GetAsync($"/{root}/overview")));
    }

    [Fact]
    public async Task Return_read_and_preview_permissions_use_existing_sale_purchase_permissions_and_no_schema_changes()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var (_, viewer, role) = await api.LimitedUserAsync("sale.create"); using var session = viewer;
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/customer-returns/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/customer-returns/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/supplier-returns/lookups")).StatusCode);
        await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new Trade.Application.Authentication.RolePermissionsRequest(["sale.view"]), ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/customer-returns/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/customer-returns/preview", new { }, ApiTestHost.Json)).StatusCode);
        await api.WithDbAsync(db => { Assert.False(db.Database.HasPendingModelChanges()); return Task.CompletedTask; });
    }
}
