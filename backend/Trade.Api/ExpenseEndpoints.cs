using Trade.Application;
using Trade.Domain.Documents;

namespace Trade.Api;

public static class ExpenseEndpoints
{
    public static void MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/expense-categories").WithTags("Expense categories");
        categories.MapPost("", async (ExpenseCategoryRequest request, RequestContext context, ExpenseService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            var category = await service.CreateCategoryAsync(context.OrganizationId, request, ct);
            return Results.Created($"/expense-categories/{category.Id}", category);
        }).Produces<ExpenseCategoryResponse>(201).ProducesProblem(400);
        categories.MapGet("", async (RequestContext context, ExpenseService service, CancellationToken ct) =>
            Results.Ok(await service.CategoriesAsync(context.OrganizationId, ct))).Produces<IReadOnlyList<ExpenseCategoryResponse>>();
        categories.MapGet("/{id:guid}", async (Guid id, RequestContext context, ExpenseService service, CancellationToken ct) =>
            Results.Ok(await service.CategoryAsync(context.OrganizationId, id, ct))).Produces<ExpenseCategoryResponse>().ProducesProblem(404);
        categories.MapPut("/{id:guid}", async (Guid id, ExpenseCategoryRequest request, RequestContext context, ExpenseService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateCategoryAsync(context.OrganizationId, id, request, ct));
        }).Produces<ExpenseCategoryResponse>().ProducesProblem(400).ProducesProblem(404);

        var expenses = app.MapGroup("/expenses").WithTags("Expenses");
        expenses.MapPost("", async (ExpenseRequest request, RequestContext context, ExpenseService service, CancellationToken ct) =>
        {
            var expense = await service.CreateAsync(context.OrganizationId, context.ActorId, request, ct);
            return Results.Created($"/expenses/{expense.Document.Id}", expense);
        }).Produces<ExpenseResponse>(201).ProducesProblem(400);
        expenses.MapGet("", async (DateOnly? from, DateOnly? to, Guid? counterpartyId, Guid? categoryId,
            Guid? purchaseInvoiceId, DocumentStatus? status, Guid? currencyId, RequestContext context, ExpenseService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(context.OrganizationId,
                new(from, to, counterpartyId, categoryId, purchaseInvoiceId, status, currencyId), ct)))
            .Produces<IReadOnlyList<ExpenseResponse>>().ProducesProblem(400);
        expenses.MapGet("/{id:guid}", async (Guid id, RequestContext context, ExpenseService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(context.OrganizationId, id, ct))).Produces<ExpenseResponse>().ProducesProblem(404);
        expenses.MapPut("/{id:guid}", async (Guid id, UpdateExpenseRequest request, RequestContext context, ExpenseService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAsync(context.OrganizationId, id, request, ct));
        }).Produces<ExpenseResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        expenses.MapPost("/{id:guid}/post", async (Guid id, RequestContext context, ExpenseService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(context.OrganizationId, id, context.ActorId, ct)))
            .Produces<ExpenseResponse>().ProducesProblem(400).ProducesProblem(404);
        expenses.MapPost("/{id:guid}/unpost", async (Guid id, RequestContext context, ExpenseService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UnpostAsync(context.OrganizationId, id, ct));
        }).Produces<ExpenseResponse>().ProducesProblem(404);
        expenses.MapDelete("/{id:guid}", async (Guid id, RequestContext context, ExpenseService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(context.OrganizationId, id, context.ActorId, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(404);
    }
}
