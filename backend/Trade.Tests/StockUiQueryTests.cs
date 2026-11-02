using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class StockUiQueryTests(DatabaseFixture fixture)
{
    private static async Task<StockRow[]> Rows(ApiTestHost api, string query = "") =>
        await api.ReadAsync<StockRow[]>(await api.Client.GetAsync("/stock/overview" + query));

    [Fact]
    public async Task Category_filters_use_only_existing_goods_references_without_a_new_category_directory_or_invented_names()
    {
        await using var api = await fixture.CreateApiAsync(); var category = Guid.NewGuid();
        var input = api.ProductInput() with { CategoryId = category };
        var product = await api.ReadAsync<ProductResponse>(await api.Client.PostAsJsonAsync("/products", input, ApiTestHost.Json), HttpStatusCode.Created);
        await api.ReceiveAsync(productId: product.Id);
        var lookups = await api.ReadAsync<StockLookups>(await api.Client.GetAsync("/stock/lookups"));
        var option = Assert.Single(lookups.Categories); Assert.Equal(category, option.Id); Assert.Null(option.Name);
        Assert.Equal(category, Assert.Single(await Rows(api, $"?categoryId={category}")).CategoryId);
        Assert.Empty(await Rows(api, $"?categoryId={Guid.NewGuid()}"));
    }

    [Fact]
    public async Task Overview_groups_only_goods_by_product_location_or_batch_with_derived_ranges_and_filters()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync(10m, date: new(2026, 10, 1));
        var newer = await api.ReceiveAsync(5m, stock.ProductId, stock.LocationId, new(2026, 10, 10));
        await api.CreateProductAsync(ItemType.SERVICE);
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail"), ApiTestHost.Json), HttpStatusCode.Created);
        await api.Client.PutAsJsonAsync($"/batches/{stock.BatchId}/prices/{type.Id}", new PriceRequest(20m), ApiTestHost.Json);
        await api.Client.PutAsJsonAsync($"/batches/{newer.BatchId}/prices/{type.Id}", new PriceRequest(30m), ApiTestHost.Json);
        var row = Assert.Single(await Rows(api, $"?retailPriceTypeId={type.Id}"));
        Assert.Null(row.BatchId); Assert.Equal(15m, row.PhysicalQuantity); Assert.Equal(0m, row.ReservedQuantity);
        Assert.Equal(15m, row.AvailableQuantity); Assert.Equal(new(20m, 30m), row.RetailPrice); Assert.NotNull(row.CurrentUnitCost);
        var batches = await Rows(api, $"?byBatch=true&productId={stock.ProductId}&withStock=true");
        Assert.Equal(new[] { stock.BatchId, newer.BatchId }, batches.Select(x => x.BatchId!.Value));
        Assert.Empty(await Rows(api, "?withReservations=true"));
        Assert.Empty(await Rows(api, "?search=does-not-exist"));
        Assert.Single(await Rows(api, $"?search={Uri.EscapeDataString(row.Code)}&storageLocationId={stock.LocationId}"));
    }

    [Fact]
    public async Task Reservations_list_all_four_draft_sources_and_match_the_view_without_service_or_deleted_documents()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync(20m); var destination = await api.KnownLocationAsync();
        var sale = await api.CreateSaleAsync(stock, 2m);
        var supplierReturn = await api.CreateSupplierReturnAsync(stock.SupplierInput(2m));
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination, 2m));
        var reason = await api.CreateReasonAsync(); var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id, 2m));
        var reservations = await api.ReadAsync<StockReservationEntry[]>(await api.Client.GetAsync($"/stock/reservations?batchId={stock.BatchId}&storageLocationId={stock.LocationId}"));
        Assert.Equal(4, reservations.Length); Assert.Equal(8m, reservations.Sum(x => x.Quantity));
        Assert.Equal(new[] { "SALE_INVOICE", "STOCK_TRANSFER", "SUPPLIER_RETURN", "WRITE_OFF" }, reservations.Select(x => x.DocumentType).Order().ToArray());
        Assert.All(reservations, x => Assert.Null(x.DocumentNumber));
        Assert.Equal(8m, Assert.Single(await Rows(api, $"?productId={stock.ProductId}")).ReservedQuantity);
        await api.Client.DeleteAsync($"/stock-transfers/{transfer.Document.Id}");
        await api.PostWriteOffAsync(writeOff.Document.Id);
        reservations = await api.ReadAsync<StockReservationEntry[]>(await api.Client.GetAsync($"/stock/reservations?productId={stock.ProductId}"));
        Assert.Equal(2, reservations.Length); Assert.Equal(4m, reservations.Sum(x => x.Quantity));
        Assert.DoesNotContain(reservations, x => x.DocumentId == transfer.Document.Id || x.DocumentId == writeOff.Document.Id);
        Assert.Contains(reservations, x => x.DocumentId == sale.Document.Id); Assert.Contains(reservations, x => x.DocumentId == supplierReturn.Document.Id);
    }

    [Fact]
    public async Task Movement_history_has_document_dates_and_running_physical_balances_partitioned_by_location()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync(10m, date: new(2026, 10, 1)); var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination, 3m)); await api.PostTransferAsync(transfer.Document.Id);
        var sale = await api.CreateSaleAsync(stock, 2m); await api.PostSaleAsync(sale.Document.Id);
        var rows = await api.ReadAsync<StockMovementEntry[]>(await api.Client.GetAsync($"/stock/movements?productId={stock.ProductId}"));
        Assert.Equal(new[] { 10m, 7m, 5m }, rows.Where(x => x.StorageLocationId == stock.LocationId).Select(x => x.PhysicalBalanceAfter));
        Assert.Equal(3m, Assert.Single(rows, x => x.StorageLocationId == destination).PhysicalBalanceAfter);
        Assert.All(rows, row => Assert.NotNull(row.DocumentNumber));
        var source = Assert.Single(rows, x => x.DocumentId == stock.Purchase.Document.Id); Assert.Equal(new DateOnly(2026, 10, 1), source.DocumentDate);
        Assert.Equal(3, (await api.ReadAsync<StockMovementEntry[]>(await api.Client.GetAsync($"/stock/movements?batchId={stock.BatchId}&storageLocationId={stock.LocationId}"))).Length);
    }

    [Fact]
    public async Task Product_and_batch_details_expose_origin_prices_and_current_view_cost_without_batch_active_or_manual_creation()
    {
        await using var api = await fixture.CreateApiAsync(); var stock = await api.ReceiveAsync();
        var product = await api.ReadAsync<StockProductDetail>(await api.Client.GetAsync($"/stock/products/{stock.ProductId}"));
        Assert.Equal(stock.ProductId, product.Product.Id); Assert.Single(product.Balances); Assert.Single(product.Batches);
        var batch = await api.ReadAsync<StockBatchDetail>(await api.Client.GetAsync($"/stock/batches/{stock.BatchId}"));
        Assert.Equal(stock.Purchase.Document.Id, batch.PurchaseDocumentId); Assert.NotNull(batch.PurchaseNumber); Assert.NotNull(batch.Supplier);
        Assert.Equal(stock.Purchase.Lines.Single().UnitPriceCurrency, batch.Valuation!.UnitPriceCurrency);
        Assert.Equal(stock.Purchase.Lines.Single().UnitPriceCurrency * stock.Purchase.ExchangeRate * (1m - stock.Purchase.DiscountPercent / 100m), batch.Valuation.UnitCostUah);
        var json = await (await api.Client.GetAsync($"/stock/batches/{stock.BatchId}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"batchActive\"", json);
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/stock/products/{service.Id}")).StatusCode);
    }

    [Fact]
    public async Task Operation_candidates_exclude_only_the_owned_draft_at_its_saved_source_and_preserve_global_balances()
    {
        await using var api = await fixture.CreateApiAsync(); var stock = await api.ReceiveAsync(10m); var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination, 10m));
        Assert.Empty(await api.ReadAsync<StockCandidate[]>(await api.Client.GetAsync($"/stock/operation-batches?storageLocationId={stock.LocationId}")));
        var candidate = Assert.Single(await api.ReadAsync<StockCandidate[]>(await api.Client.GetAsync($"/stock/operation-batches?storageLocationId={stock.LocationId}&excludeDocumentId={transfer.Document.Id}")));
        Assert.Equal(10m, candidate.OwnReservedQuantity); Assert.Equal(10m, candidate.AvailableForDocument); Assert.Equal(0m, candidate.Stock.AvailableQuantity); Assert.Equal(10m, candidate.Stock.ReservedQuantity);
        Assert.DoesNotContain(await api.ReadAsync<StockCandidate[]>(await api.Client.GetAsync($"/stock/operation-batches?storageLocationId={destination}&excludeDocumentId={transfer.Document.Id}")), x => x.Stock.BatchId == stock.BatchId);
        Assert.Single(await api.ReadAsync<StockRow[]>(await api.Client.GetAsync($"/stock-transfers/{transfer.Document.Id}/stock-lines")));
        await api.PostTransferAsync(transfer.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync($"/stock/operation-batches?storageLocationId={stock.LocationId}&excludeDocumentId={transfer.Document.Id}")).StatusCode);
    }

    [Fact]
    public async Task Writeoff_cost_is_read_from_current_view_and_own_reserve_is_available_again_for_editing()
    {
        await using var api = await fixture.CreateApiAsync(); var stock = await api.ReceiveAsync(10m); var reason = await api.CreateReasonAsync();
        var doc = await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id, 3m));
        var candidate = Assert.Single(await api.ReadAsync<StockCandidate[]>(await api.Client.GetAsync($"/stock/operation-batches?storageLocationId={stock.LocationId}&excludeDocumentId={doc.Document.Id}")));
        Assert.Equal(10m, candidate.AvailableForDocument); Assert.Equal(7m, candidate.Stock.AvailableQuantity);
        var cost = await api.ReadAsync<StockWriteOffCost>(await api.Client.GetAsync($"/write-offs/{doc.Document.Id}/cost"));
        Assert.Equal(30m, cost.TotalCostUah);
        Assert.Single(await api.ReadAsync<StockRow[]>(await api.Client.GetAsync($"/write-offs/{doc.Document.Id}/stock-lines")));
    }

    [Fact]
    public async Task All_stock_reads_and_document_exclusion_are_organization_scoped()
    {
        await using var api = await fixture.CreateApiAsync(); var stock = await api.ReceiveAsync(); var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination)); var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Foreign type"), ApiTestHost.Json), HttpStatusCode.Created);
        api.SetOrganization(Guid.NewGuid()); Assert.Empty(await Rows(api));
        foreach (var path in new[] { $"/stock/products/{stock.ProductId}", $"/stock/batches/{stock.BatchId}", $"/stock/movements?batchId={stock.BatchId}", $"/stock/reservations?productId={stock.ProductId}", $"/stock-transfers/{transfer.Document.Id}/stock-lines", $"/stock/operation-batches?storageLocationId={stock.LocationId}&excludeDocumentId={transfer.Document.Id}", $"/stock/overview?retailPriceTypeId={type.Id}" })
            Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync(path)).StatusCode);
        Assert.Empty(await api.ReadAsync<StockReservationEntry[]>(await api.Client.GetAsync("/stock/reservations")));
        Assert.Empty(await api.ReadAsync<StockMovementEntry[]>(await api.Client.GetAsync("/stock/movements")));
    }

    [Fact]
    public async Task Identity_stock_viewer_never_receives_cost_and_operation_permissions_do_not_grant_balances()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync(); var stock = await api.ReceiveAsync();
        var (_, viewer, role) = await api.LimitedUserAsync("stock.view"); using var session = viewer;
        var rows = await api.ReadAsync<StockRow[]>(await viewer.GetAsync("/stock/overview")); Assert.All(rows, x => Assert.Null(x.CurrentUnitCost));
        var batch = await api.ReadAsync<StockBatchDetail>(await viewer.GetAsync($"/stock/batches/{stock.BatchId}")); Assert.Null(batch.Valuation);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/write-offs/{Guid.NewGuid()}/cost")).StatusCode);
        await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new Trade.Application.Authentication.RolePermissionsRequest(["stock.view", "cost.view"]), ApiTestHost.Json);
        Assert.All(await api.ReadAsync<StockRow[]>(await viewer.GetAsync("/stock/overview")), x => Assert.NotNull(x.CurrentUnitCost));
        await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new Trade.Application.Authentication.RolePermissionsRequest(["stock.transfer"]), ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/stock/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/stock/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/stock/operation-batches?storageLocationId={stock.LocationId}")).StatusCode);
    }
}
