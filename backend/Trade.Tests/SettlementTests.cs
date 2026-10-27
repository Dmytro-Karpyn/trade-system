using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;
using Trade.Domain.Purchasing;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SettlementTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Purchase_POST_records_one_negative_discounted_foreign_currency_settlement_for_active_lines()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, Guid.NewGuid(), discount: 10m, rate: 40.5m);
        var edit = ApiTestHost.Edit(purchase);
        purchase = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit with
        {
            Lines = [edit.Lines[0], new PurchaseLineDetails(null, 2, edit.Lines[0].ProductId, 3m, 10m),
                new PurchaseLineDetails(null, 3, edit.Lines[0].ProductId, 2m, 20m)]
        }, ApiTestHost.Json));
        purchase = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", ApiTestHost.Edit(purchase) with
            { Lines = ApiTestHost.Edit(purchase).Lines.Where(x => x.LineNo != 1).ToArray() }, ApiTestHost.Json));
        Assert.Empty(await api.SettlementsAsync(purchase.Document.Id));
        await api.PostAsync(purchase.Document.Id);
        var movement = Assert.Single(await api.SettlementsAsync(purchase.Document.Id));
        Assert.Equal(api.CounterpartyId, movement.CounterpartyId);
        Assert.Equal(purchase.CurrencyId, movement.CurrencyId);
        Assert.Equal((-63m, -2551.5m), (movement.Amount, movement.BaseAmountUah));
        var balance = Assert.Single(await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync($"/settlements/balances?counterpartyId={api.CounterpartyId}")));
        Assert.Equal(-63m, balance.Balance);
    }

    [Fact]
    public async Task Purchase_POST_is_concurrently_idempotent_and_UNPOST_removes_only_its_settlement()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, Guid.NewGuid(), quantity: 3m, price: 20m);
        var posted = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostAsync(purchase.Document.Id)));
        Assert.All(posted, x => Assert.Equal(posted[0].Document, x.Document));
        var original = Assert.Single(await api.SettlementsAsync(purchase.Document.Id));
        Assert.Equal(-60m, original.Amount);
        Assert.Equal(original.Id, Assert.Single(await api.SettlementsAsync(purchase.Document.Id)).Id);
        purchase = await api.UnpostAsync(purchase.Document.Id);
        Assert.Empty(await api.SettlementsAsync(purchase.Document.Id));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        var edit = ApiTestHost.Edit(purchase);
        await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { UnitPriceCurrency = 30m }] }, ApiTestHost.Json));
        var reposted = await api.PostAsync(purchase.Document.Id);
        Assert.Equal(posted[0].Document.Number, reposted.Document.Number);
        Assert.Equal(-90m, Assert.Single(await api.SettlementsAsync(purchase.Document.Id)).Amount);
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OPENING_STOCK_never_records_settlement_even_with_a_counterparty(bool hasCounterparty)
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync(type: DocumentType.OPENING_STOCK);
        if (hasCounterparty)
            purchase = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}",
                ApiTestHost.Edit(purchase) with { CounterpartyId = api.CounterpartyId }, ApiTestHost.Json));
        await api.PostAsync(purchase.Document.Id);
        Assert.Empty(await api.SettlementsAsync(purchase.Document.Id));
        Assert.Single(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        await api.UnpostAsync(purchase.Document.Id);
        Assert.Empty(await api.SettlementsAsync(purchase.Document.Id));
    }

    [Fact]
    public async Task Mixed_GOODS_and_SERVICE_sale_records_one_positive_UAH_total_with_header_discount_and_no_removed_lines()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var sale = await api.CreateSaleAsync(api.NewInput(stock.LocationId,
            [new(null, 1, stock.ProductId, stock.BatchId, 2m, 12.5m), new(null, 2, service.Id, null, 3m, 20m),
                new(null, 3, service.Id, null, 1m, 999m)]) with { DiscountPercent = 15m });
        var edit = SalesTestSupport.Edit(sale);
        sale = await api.UpdateSaleAsync(sale.Document.Id, edit with { Lines = edit.Lines.Where(x => x.LineNo != 3).ToArray() });
        Assert.Empty(await api.SettlementsAsync(sale.Document.Id));
        await api.PostSaleAsync(sale.Document.Id);
        var movement = Assert.Single(await api.SettlementsAsync(sale.Document.Id));
        Assert.Equal((SettlementCurrencies.Uah, 72.25m, 72.25m), (movement.CurrencyId, movement.Amount, movement.BaseAmountUah));
        Assert.Equal(api.CounterpartyId, movement.CounterpartyId);
        Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
    }

    [Fact]
    public async Task Sale_POST_is_concurrently_idempotent_and_UNPOST_releases_settlement_while_restoring_reserves()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        var posted = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostSaleAsync(sale.Document.Id)));
        Assert.All(posted, x => Assert.Equal(posted[0].Document, x.Document));
        var movement = Assert.Single(await api.SettlementsAsync(sale.Document.Id));
        Assert.Equal(48m, movement.Amount);
        sale = await api.UnpostSaleAsync(sale.Document.Id);
        Assert.Empty(await api.SettlementsAsync(sale.Document.Id));
        var balance = await api.BalanceAsync(stock);
        Assert.Equal((10m, 4m, 6m), (balance.PhysicalQuantity, balance.ReservedQuantity, balance.AvailableQuantity));
        var edit = SalesTestSupport.Edit(sale);
        await api.UpdateSaleAsync(sale.Document.Id, edit with { Lines = [edit.Lines[0] with { UnitPrice = 15m }] });
        var reposted = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(posted[0].Document.Number, reposted.Document.Number);
        Assert.Equal(60m, Assert.Single(await api.SettlementsAsync(sale.Document.Id)).Amount);
    }

    [Fact]
    public async Task Balance_view_aggregates_multiple_documents_and_changes_after_UNPOST()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, SettlementCurrencies.Uah, quantity: 8m, price: 10m);
        await api.PostAsync(purchase.Document.Id);
        var a = await api.ServiceSaleForAsync(api.CounterpartyId, 100m);
        var b = await api.ServiceSaleForAsync(api.CounterpartyId, 50m);
        await api.PostSaleAsync(a.Document.Id);
        await api.PostSaleAsync(b.Document.Id);
        var balances = await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync($"/settlements/balances?counterpartyId={api.CounterpartyId}"));
        Assert.Equal(new SettlementBalanceResponse(api.CounterpartyId, SettlementCurrencies.Uah, 70m), Assert.Single(balances));
        Assert.Equal(70m, await api.QueryAsync(db => db.Database.SqlQuery<decimal>($"""
            SELECT "Balance" AS "Value" FROM v_counterparty_balance WHERE "CounterpartyId"={api.CounterpartyId} AND "CurrencyId"={SettlementCurrencies.Uah}
            """).SingleAsync()));
        await api.UnpostSaleAsync(b.Document.Id);
        Assert.Equal(20m, Assert.Single(await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync($"/settlements/balances?counterpartyId={api.CounterpartyId}"))).Balance);
    }

    [Fact]
    public async Task History_orders_backdated_documents_chronologically_and_computes_running_balances_separately_per_currency()
    {
        await using var api = await fixture.CreateApiAsync();
        var currency = Guid.NewGuid();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, SettlementCurrencies.Uah, price: 50m, date: new(2026, 10, 18));
        var foreign = await api.PurchaseForAsync(api.CounterpartyId, currency, price: 7m, date: new(2026, 10, 19));
        var backdated = await api.ServiceSaleForAsync(api.CounterpartyId, 30m, date: new(2026, 10, 17));
        var last = await api.ServiceSaleForAsync(api.CounterpartyId, 10m, date: new(2026, 10, 20));
        await api.PostSaleAsync(last.Document.Id);
        await api.PostAsync(foreign.Document.Id);
        await api.PostAsync(purchase.Document.Id);
        await api.PostSaleAsync(backdated.Document.Id);
        var history = await api.ReadAsync<CounterpartySettlementResponse>(await api.Client.GetAsync($"/settlements/counterparties/{api.CounterpartyId}"));
        Assert.Equal(new[] { backdated.Document.Id, purchase.Document.Id, foreign.Document.Id, last.Document.Id }, history.Movements.Select(x => x.DocumentId));
        Assert.Equal(new[] { 30m, -50m, -7m, 10m }, history.Movements.Select(x => x.Amount));
        Assert.Equal(new[] { 30m, -20m, -7m, -10m }, history.Movements.Select(x => x.BalanceAfterMovement));
        Assert.Equal(DocumentType.SALE_INVOICE, history.Movements[0].DocumentType);
        Assert.Equal(DocumentType.PURCHASE_INVOICE, history.Movements[1].DocumentType);
        Assert.Equal(new DateOnly(2026, 10, 17), history.Movements[0].DocumentDate);
        Assert.Equal(-10m, history.Balances.Single(x => x.CurrencyId == SettlementCurrencies.Uah).Balance);
        Assert.Equal(-7m, history.Balances.Single(x => x.CurrencyId == currency).Balance);
        var repeat = await api.ReadAsync<CounterpartySettlementResponse>(await api.Client.GetAsync($"/settlements/counterparties/{api.CounterpartyId}"));
        Assert.Equal(history.Movements, repeat.Movements);
    }

    [Fact]
    public async Task Balance_filters_handle_counterparty_currency_and_empty_results()
    {
        await using var api = await fixture.CreateApiAsync();
        var other = await api.CreateCounterpartyAsync();
        var currency = Guid.NewGuid();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, currency, price: 20m);
        await api.PostAsync(purchase.Document.Id);
        var own = await api.ServiceSaleForAsync(api.CounterpartyId, 30m);
        var theirs = await api.ServiceSaleForAsync(other.Id, 100m);
        await api.PostSaleAsync(own.Document.Id);
        await api.PostSaleAsync(theirs.Document.Id);
        Assert.Equal(3, (await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync("/settlements/balances"))).Length);
        Assert.Equal(2, (await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync($"/settlements/balances?counterpartyId={api.CounterpartyId}"))).Length);
        Assert.Equal(2, (await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync($"/settlements/balances?currencyId={SettlementCurrencies.Uah}"))).Length);
        Assert.Equal(-20m, Assert.Single(await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync($"/settlements/balances?counterpartyId={api.CounterpartyId}&currencyId={currency}"))).Balance);
        Assert.Empty(await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync($"/settlements/balances?counterpartyId={Guid.NewGuid()}")));
        var empty = await api.CreateCounterpartyAsync();
        var history = await api.ReadAsync<CounterpartySettlementResponse>(await api.Client.GetAsync($"/settlements/counterparties/{empty.Id}"));
        Assert.Empty(history.Balances);
        Assert.Empty(history.Movements);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/settlements/counterparties/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Counterparties_from_another_organization_are_rejected_by_purchase_and_sale_create_and_update()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        api.SetOrganization(Guid.NewGuid());
        var other = await api.CreateCounterpartyAsync();
        api.SetOrganization(api.OrganizationId);
        var purchaseInput = new PurchaseRequest(DocumentType.PURCHASE_INVOICE, purchase.Document.DocumentDate, null, null,
            other.Id, purchase.StorageLocationId, purchase.CurrencyId, 1m, null, null, 0m,
            [new(null, 1, purchase.Lines[0].ProductId, 1m, 1m)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/purchases", purchaseInput, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", ApiTestHost.Edit(purchase) with { CounterpartyId = other.Id }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/sales", stock.SaleInput() with { CounterpartyId = other.Id }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/sales/{sale.Document.Id}", SalesTestSupport.Edit(sale) with { CounterpartyId = other.Id }, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.SettlementsAsync(purchase.Document.Id));
        Assert.Equal(purchase.CounterpartyId, (await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}"))).CounterpartyId);
    }

    [Fact]
    public async Task Balances_and_history_never_expose_other_organizations()
    {
        await using var api = await fixture.CreateApiAsync();
        var own = await api.ServiceSaleForAsync(api.CounterpartyId, 50m);
        await api.PostSaleAsync(own.Document.Id);
        api.SetOrganization(Guid.NewGuid());
        var other = await api.CreateCounterpartyAsync();
        var otherSale = await api.ServiceSaleForAsync(other.Id, 20m);
        await api.PostSaleAsync(otherSale.Document.Id);
        var balances = await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync("/settlements/balances"));
        Assert.Equal((other.Id, 20m), (Assert.Single(balances).CounterpartyId, balances[0].Balance));
        Assert.Empty(await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync($"/settlements/balances?counterpartyId={api.CounterpartyId}")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/settlements/counterparties/{api.CounterpartyId}")).StatusCode);
        api.SetOrganization(api.OrganizationId);
        Assert.Equal(50m, Assert.Single(await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync("/settlements/balances"))).Balance);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/settlements/counterparties/{other.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_settlement_insert_rolls_back_POST_stock_prices_and_document(bool isSale)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = isSale ? await api.ReceiveAsync() : null;
        var sale = stock is null ? null : await api.CreateSaleAsync(stock);
        var purchase = isSale ? null : await api.CreatePurchaseAsync();
        var document = sale?.Document ?? purchase!.Document;
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_settlement_insert() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected settlement failure' USING ERRCODE='23514'; END; $$;
            CREATE TRIGGER fail_settlement_insert BEFORE INSERT ON settlement_movements
            FOR EACH ROW EXECUTE FUNCTION fail_settlement_insert();
            """));
        var prefix = isSale ? "sales" : "purchases";
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/{prefix}/{document.Id}/post", null)).StatusCode);
        var current = isSale ? (await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{document.Id}"))).Document
            : (await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{document.Id}"))).Document;
        Assert.Equal(document, current);
        Assert.Empty(await api.SettlementsAsync(document.Id));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == document.Id).ToListAsync()));
        if (stock is not null) Assert.Equal((10m, 4m), ((await api.BalanceAsync(stock)).PhysicalQuantity, (await api.BalanceAsync(stock)).ReservedQuantity));
        else Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_settlement_delete_rolls_back_UNPOST_including_already_deleted_stock_movements(bool isSale)
    {
        await using var api = await fixture.CreateApiAsync();
        DocumentResponse document;
        if (isSale)
        {
            var stock = await api.ReceiveAsync();
            document = (await api.PostSaleAsync((await api.CreateSaleAsync(stock)).Document.Id)).Document;
        }
        else document = (await api.PostAsync((await api.CreatePurchaseAsync()).Document.Id)).Document;
        var movements = await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == document.Id).Select(x => x.Id).ToArrayAsync());
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_settlement_delete() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected settlement delete failure' USING ERRCODE='23514'; END; $$;
            CREATE TRIGGER fail_settlement_delete BEFORE DELETE ON settlement_movements
            FOR EACH ROW EXECUTE FUNCTION fail_settlement_delete();
            """));
        var prefix = isSale ? "sales" : "purchases";
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/{prefix}/{document.Id}/unpost", null)).StatusCode);
        var current = isSale ? (await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{document.Id}"))).Document
            : (await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{document.Id}"))).Document;
        Assert.Equal(document, current);
        Assert.Single(await api.SettlementsAsync(document.Id));
        Assert.Equal(movements, await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == document.Id).Select(x => x.Id).ToArrayAsync()));
    }

    [Fact]
    public async Task Totals_do_not_round_lines_and_base_amount_uses_final_amount_times_exchange_rate()
    {
        await using var api = await fixture.CreateApiAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var sale = await api.CreateSaleAsync(api.NewInput(Guid.NewGuid(),
            [new(null, 1, service.Id, null, 0.0001m, 0.5m), new(null, 2, service.Id, null, 0.0001m, 0.5m)]));
        await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(0.0001m, Assert.Single(await api.SettlementsAsync(sale.Document.Id)).Amount);
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, Guid.NewGuid(), price: 0.0001m, discount: 50m, rate: 1.5m);
        await api.PostAsync(purchase.Document.Id);
        var movement = Assert.Single(await api.SettlementsAsync(purchase.Document.Id));
        Assert.Equal((-0.0001m, -0.0002m), (movement.Amount, movement.BaseAmountUah));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Large_intermediate_totals_are_exact_and_only_unrepresentable_final_settlements_are_rejected(bool fullDiscount)
    {
        await using var api = await fixture.CreateApiAsync();
        var sale = await api.ServiceSaleForAsync(api.CounterpartyId, 9_999_999_999_999_999.9999m,
            quantity: 9_999_999_999_999_999.9999m, discount: fullDiscount ? 100m : 0m);
        var result = await api.Client.PostAsync($"/sales/{sale.Document.Id}/post", null);
        if (fullDiscount)
        {
            await api.ReadAsync<SaleResponse>(result);
            Assert.Equal(0m, Assert.Single(await api.SettlementsAsync(sale.Document.Id)).Amount);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            Assert.Empty(await api.SettlementsAsync(sale.Document.Id));
            Assert.Equal(sale.Document, (await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"))).Document);
        }
    }
}
