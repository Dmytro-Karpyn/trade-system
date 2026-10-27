using System.Net;
using System.Net.Http.Json;
using Trade.Application;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class CounterpartyQueryTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("інструмент")]
    [InlineData("+380501234567")]
    [InlineData("sales@example.com")]
    [InlineData("1234567890")]
    [InlineData("edr-001")]
    public async Task Search_matches_name_phone_email_tax_and_registration_case_insensitively(string search)
    {
        await using var api = await fixture.CreateApiAsync();
        var party = await api.CreateCounterpartyAsync(new CounterpartyRequest("ІНСТРУМЕНТ", TaxId: "1234567890", RegistrationCode: "EDR-001", Phone: "+380501234567", Email: "Sales@Example.com"));
        var list = await api.ReadAsync<CounterpartyResponse[]>(await api.Client.GetAsync($"/counterparties?search={Uri.EscapeDataString(search)}"));
        Assert.Equal(party.Id, Assert.Single(list).Id);
    }

    [Fact]
    public async Task Search_is_literal_and_combines_with_roles_system_and_active_without_crossing_tenants()
    {
        await using var api = await fixture.CreateApiAsync();
        var party = await api.CreateCounterpartyAsync(new CounterpartyRequest("System 10%_Retail", IsCustomer: true, IsSupplier: true, IsSystem: true, Active: false));
        var list = await api.ReadAsync<CounterpartyResponse[]>(await api.Client.GetAsync("/counterparties?search=%25_&isCustomer=true&isSupplier=true&isSystem=true&active=false"));
        Assert.Equal(party.Id, Assert.Single(list).Id);
        Assert.Empty(await api.ReadAsync<CounterpartyResponse[]>(await api.Client.GetAsync("/counterparties?isSystem=true&active=true")));
        Assert.DoesNotContain(await api.ReadAsync<CounterpartyResponse[]>(await api.Client.GetAsync("/counterparties?isSystem=false")), x => x.Id == party.Id);
        api.SetOrganization(Guid.NewGuid());
        Assert.Empty(await api.ReadAsync<CounterpartyResponse[]>(await api.Client.GetAsync("/counterparties?search=Retail&isSystem=true")));
    }

    [Fact]
    public async Task Settlement_history_projects_the_actual_document_number_without_changing_the_balance()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync(quantity: 2m);
        var posted = await api.PostAsync(purchase.Document.Id);
        var history = await api.ReadAsync<CounterpartySettlementResponse>(await api.Client.GetAsync($"/settlements/counterparties/{purchase.CounterpartyId}"));
        var movement = Assert.Single(history.Movements);
        Assert.Equal(posted.Document.Number, movement.DocumentNumber);
        Assert.Equal(posted.Document.Id, movement.DocumentId);
        Assert.Equal(-20m, movement.Amount);
        Assert.Equal(-20m, movement.BalanceAfterMovement);
        Assert.Equal(-20m, Assert.Single(history.Balances).Balance);
    }
}
