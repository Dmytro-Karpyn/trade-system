using Trade.Application;
using Trade.Domain.Documents;

namespace Trade.Api;

public static class MoneyEndpoints
{
    public static void MapMoneyEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/money/accounts").WithTags("Money");
        accounts.MapPost("", async (MoneyAccountRequest request, RequestContext context, MoneyService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            var account = await service.CreateAccountAsync(context.OrganizationId, request, ct);
            return Results.Created($"/money/accounts/{account.Id}", account);
        }).Produces<MoneyAccountResponse>(201);
        accounts.MapGet("", async (RequestContext context, MoneyService service, CancellationToken ct) =>
            Results.Ok(await service.AccountsAsync(context.OrganizationId, ct))).Produces<IReadOnlyList<MoneyAccountResponse>>();
        accounts.MapGet("/{id:guid}", async (Guid id, RequestContext context, MoneyService service, CancellationToken ct) =>
            Results.Ok(await service.AccountAsync(context.OrganizationId, id, ct))).Produces<MoneyAccountResponse>();
        accounts.MapPut("/{id:guid}", async (Guid id, MoneyAccountRequest request, RequestContext context, MoneyService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAccountAsync(context.OrganizationId, id, request, ct));
        }).Produces<MoneyAccountResponse>();

        MapDocuments(app, "receipts", DocumentType.MONEY_RECEIPT);
        MapDocuments(app, "payments", DocumentType.MONEY_PAYMENT);
        MapDocuments(app, "transfers", DocumentType.MONEY_TRANSFER);
        app.MapGet("/money/balances", async (RequestContext context, MoneyService service, CancellationToken ct) =>
            Results.Ok(await service.BalancesAsync(context.OrganizationId, ct))).WithTags("Money").Produces<IReadOnlyList<MoneyBalanceResponse>>();
        app.MapGet("/money/lookups", async (RequestContext context, IFinanceQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.LookupsAsync(context.OrganizationId, ct))).WithTags("Money").Produces<MoneyLookups>();
        app.MapGet("/money/account-overview", async (RequestContext context, IFinanceQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.AccountsAsync(context.OrganizationId, ct))).WithTags("Money").Produces<IReadOnlyList<MoneyAccountOverview>>();
    }

    private static void MapDocuments(IEndpointRouteBuilder app, string path, DocumentType type)
    {
        var group = app.MapGroup($"/money/{path}").WithTags("Money");
        group.MapGet("", async (DateOnly? from, DateOnly? to, Guid? accountId, Guid? counterpartyId, Trade.Domain.Money.MoneyPurpose? purpose,
            DocumentStatus? status, RequestContext context, IFinanceQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.MoneyListAsync(context.OrganizationId, type, new(from, to, accountId, counterpartyId, purpose, status), ct)))
            .Produces<IReadOnlyList<MoneyListRow>>();
        group.MapPost("", async (MoneyDocumentRequest request, RequestContext context, MoneyService service, CancellationToken ct) =>
        {
            var document = await service.CreateAsync(context.OrganizationId, context.ActorId, type, request, ct);
            return Results.Created($"/money/{path}/{document.Document.Id}", document);
        }).Produces<MoneyDocumentResponse>(201);
        group.MapGet("/{id:guid}", async (Guid id, RequestContext context, MoneyService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(context.OrganizationId, id, type, ct))).Produces<MoneyDocumentResponse>();
        group.MapPut("/{id:guid}", async (Guid id, UpdateMoneyDocumentRequest request, RequestContext context, MoneyService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAsync(context.OrganizationId, id, type, request, ct));
        }).Produces<MoneyDocumentResponse>().ProducesProblem(409);
        group.MapPost("/{id:guid}/post", async (Guid id, RequestContext context, MoneyService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(context.OrganizationId, context.ActorId, id, type, ct))).Produces<MoneyDocumentResponse>();
        group.MapPost("/{id:guid}/unpost", async (Guid id, RequestContext context, MoneyService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UnpostAsync(context.OrganizationId, id, type, ct));
        }).Produces<MoneyDocumentResponse>();
        group.MapDelete("/{id:guid}", async (Guid id, RequestContext context, MoneyService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(context.OrganizationId, context.ActorId, id, type, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(404);
    }
}
