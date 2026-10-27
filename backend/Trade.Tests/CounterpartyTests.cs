using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trade.Application;
using Trade.Domain.Common;
using Trade.Domain.Counterparties;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class CounterpartyTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(null)]
    [InlineData(LegalType.PERSON)]
    [InlineData(LegalType.ENTREPRENEUR)]
    [InlineData(LegalType.COMPANY)]
    public async Task Create_read_and_update_round_trip_all_counterparty_fields(LegalType? legalType)
    {
        await using var api = await fixture.CreateApiAsync();
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail"), ApiTestHost.Json), HttpStatusCode.Created);
        var input = new CounterpartyRequest("  Partner  ", legalType, "tax-id", "registration", "+380123", "test@example.com",
            "Kyiv", type.Id, true, true, true, false, true, "Note");
        var party = await api.CreateCounterpartyAsync(input);
        Assert.NotEqual(Guid.Empty, party.Id);
        Assert.Equal(api.OrganizationId, party.OrganizationId);
        Assert.Equal("Partner", party.Name);
        Assert.Equal(legalType, party.LegalType);
        Assert.Equal((input.TaxId, input.RegistrationCode, input.Phone, input.Email, input.Address, type.Id),
            (party.TaxId, party.RegistrationCode, party.Phone, party.Email, party.Address, party.DefaultPriceTypeId));
        Assert.Equal((true, true, true, false, true, "Note"),
            (party.IsCustomer, party.IsSupplier, party.IsEmployee, party.IsSystem, party.Active, party.Note));
        Assert.Equal(party, await api.ReadAsync<CounterpartyResponse>(await api.Client.GetAsync($"/counterparties/{party.Id}")));
        var updated = await api.ReadAsync<CounterpartyResponse>(await api.Client.PutAsJsonAsync($"/counterparties/{party.Id}",
            new CounterpartyRequest("Renamed", Active: false), ApiTestHost.Json));
        Assert.Equal(party.Id, updated.Id);
        Assert.Equal(("Renamed", false, false, false, false, false),
            (updated.Name, updated.IsCustomer, updated.IsSupplier, updated.IsEmployee, updated.IsSystem, updated.Active));
        Assert.Null(updated.LegalType);
        Assert.Null(updated.DefaultPriceTypeId);
        Assert.Null(updated.Note);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_name_is_rejected_without_changing_existing_counterparty(string name)
    {
        await using var api = await fixture.CreateApiAsync();
        var party = await api.CreateCounterpartyAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/counterparties", new CounterpartyRequest(name), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/counterparties/{party.Id}", new CounterpartyRequest(name), ApiTestHost.Json)).StatusCode);
        Assert.Equal(party, await api.ReadAsync<CounterpartyResponse>(await api.Client.GetAsync($"/counterparties/{party.Id}")));
    }

    [Fact]
    public async Task Filters_support_true_false_and_intersections_without_hiding_inactive_parties_by_default()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.CreateCounterpartyAsync(new("Customer", IsCustomer: true));
        var b = await api.CreateCounterpartyAsync(new("Employee supplier", IsSupplier: true, IsEmployee: true, Active: false));
        var c = await api.CreateCounterpartyAsync(new("All", IsCustomer: true, IsSupplier: true, IsEmployee: true));
        foreach (var (query, expected) in new[]
        {
            ("isCustomer=false", new[] { b.Id }), ("isSupplier=false", new[] { a.Id }),
            ("isEmployee=true", new[] { b.Id, c.Id }), ("active=false", new[] { b.Id }),
            ("isCustomer=true&isEmployee=true&active=true", new[] { c.Id }),
            ("isCustomer=true&isSupplier=true&isEmployee=false&active=true", new[] { api.CounterpartyId })
        })
            Assert.Equal(expected.Order(), (await api.ReadAsync<CounterpartyResponse[]>(await api.Client.GetAsync($"/counterparties?{query}"))).Select(x => x.Id).Order());
        Assert.Equal(4, (await api.ReadAsync<CounterpartyResponse[]>(await api.Client.GetAsync("/counterparties"))).Length);
    }

    [Fact]
    public async Task Default_price_type_must_exist_in_the_same_organization_on_create_and_update()
    {
        await using var api = await fixture.CreateApiAsync();
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Own"), ApiTestHost.Json), HttpStatusCode.Created);
        var party = await api.CreateCounterpartyAsync(new("Own", DefaultPriceTypeId: type.Id));
        api.SetOrganization(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsJsonAsync("/counterparties", new CounterpartyRequest("Foreign price", DefaultPriceTypeId: type.Id), ApiTestHost.Json)).StatusCode);
        var other = await api.CreateCounterpartyAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/counterparties/{other.Id}", new CounterpartyRequest("Foreign price", DefaultPriceTypeId: type.Id), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/counterparties", new CounterpartyRequest("Empty price ID", DefaultPriceTypeId: Guid.Empty), ApiTestHost.Json)).StatusCode);
        Assert.Null((await api.ReadAsync<CounterpartyResponse>(await api.Client.GetAsync($"/counterparties/{other.Id}"))).DefaultPriceTypeId);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/counterparties/{party.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/counterparties/{party.Id}", new CounterpartyRequest("Not ours"), ApiTestHost.Json)).StatusCode);
        Assert.Equal(other.Id, Assert.Single(await api.ReadAsync<CounterpartyResponse[]>(await api.Client.GetAsync("/counterparties"))).Id);
    }

    [Fact]
    public async Task System_parties_are_not_seeded_and_cannot_be_deleted_through_EF_or_direct_SQL()
    {
        await using var api = await fixture.CreateApiAsync();
        Assert.False(await api.QueryAsync(db => db.Counterparties.AnyAsync(x => x.IsSystem || x.Name == "Роздрібний покупець")));
        var party = await api.CreateCounterpartyAsync(new("System test", IsSystem: true));
        await api.WithDbAsync(async db =>
        {
            db.Counterparties.Remove(await db.Counterparties.SingleAsync(x => x.Id == party.Id));
            await Assert.ThrowsAsync<DomainException>(() => db.SaveChangesAsync());
        });
        await api.WithDbAsync(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM counterparties WHERE \"Id\" = {party.Id}"));
            Assert.Equal("23514", error.SqlState);
        });
        Assert.Equal(party, await api.ReadAsync<CounterpartyResponse>(await api.Client.GetAsync($"/counterparties/{party.Id}")));
    }

    [Fact]
    public async Task Database_constraints_reject_foreign_organization_references_and_duplicate_document_settlements()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync();
        await api.PostAsync(purchase.Document.Id);
        var own = await api.CreateCounterpartyAsync();
        api.SetOrganization(Guid.NewGuid());
        var other = await api.CreateCounterpartyAsync();
        var foreignType = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Other"), ApiTestHost.Json), HttpStatusCode.Created);
        api.SetOrganization(api.OrganizationId);
        foreach (var sql in new[]
        {
            $"UPDATE purchase_invoices SET \"CounterpartyId\" = '{other.Id}' WHERE \"DocumentId\" = '{purchase.Document.Id}'",
            $"UPDATE settlement_movements SET \"CounterpartyId\" = '{other.Id}' WHERE \"DocumentId\" = '{purchase.Document.Id}'",
            $"UPDATE counterparties SET \"DefaultPriceTypeId\" = '{foreignType.Id}' WHERE \"Id\" = '{own.Id}'"
        })
            await api.WithDbAsync(async db => Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState));
        await api.WithDbAsync(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO settlement_movements ("Id", "DocumentId", "CounterpartyId", "CurrencyId", "Amount", "BaseAmountUah")
                SELECT {Guid.NewGuid()}, "DocumentId", "CounterpartyId", "CurrencyId", "Amount", "BaseAmountUah"
                FROM settlement_movements WHERE "DocumentId" = {purchase.Document.Id}
                """));
            Assert.Equal("23505", error.SqlState);
        });
    }

    [Fact]
    public async Task Schema_has_only_requested_fields_and_ordinary_balance_view_without_cached_balances_or_totals()
    {
        await using var api = await fixture.CreateApiAsync();
        foreach (var (table, expected) in new[]
        {
            ("counterparties", new[] { "Id", "OrganizationId", "Name", "LegalType", "TaxId", "RegistrationCode", "Phone", "Email", "Address", "DefaultPriceTypeId", "IsCustomer", "IsSupplier", "IsEmployee", "IsSystem", "Active", "Note" }),
            ("settlement_movements", new[] { "Id", "DocumentId", "CounterpartyId", "CurrencyId", "Amount", "BaseAmountUah" }),
            ("v_counterparty_balance", new[] { "CounterpartyId", "CurrencyId", "Balance" })
        })
        {
            Assert.Equal(expected, await api.QueryAsync(db => db.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = {table} ORDER BY ordinal_position
                """).ToArrayAsync()));
        }
        Assert.Equal(2, await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns WHERE table_schema='public'
              AND table_name='settlement_movements' AND column_name IN ('Amount','BaseAmountUah') AND numeric_precision=20 AND numeric_scale=4
            """).SingleAsync()));
        Assert.Equal("v", await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("SELECT relkind::text AS \"Value\" FROM pg_class WHERE oid='v_counterparty_balance'::regclass").SingleAsync()));
        Assert.Equal(0, await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns c JOIN information_schema.tables t
              ON t.table_name=c.table_name AND t.table_schema=c.table_schema
            WHERE c.table_schema='public' AND t.table_type='BASE TABLE'
              AND lower(replace(c.column_name,'_','')) IN ('balance','debt','advance','paidamount','paymentstatus','invoicestatus','linetotal','documenttotal','total','balanceaftermovement')
            """).SingleAsync()));
    }
}
