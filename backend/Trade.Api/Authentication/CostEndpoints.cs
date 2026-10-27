using Microsoft.EntityFrameworkCore;
using Trade.Infrastructure.Persistence;

namespace Trade.Api.Authentication;

public static class CostEndpoints
{
    public static void MapCostEndpoints(this IEndpointRouteBuilder app)
    {
        // The existing computed view is queried with the same tenant boundary as document APIs.
        app.MapGet("/write-offs/costs", async (RequestContext context, TradeDbContext db, CancellationToken ct) =>
            await db.Database.SqlQuery<WriteOffCostResponse>($"""
                SELECT cost.* FROM v_write_off_cost cost
                JOIN documents document ON document."Id" = cost."DocumentId"
                WHERE document."OrganizationId" = {context.OrganizationId} AND document."Status" = 'POSTED'
                ORDER BY cost."DocumentDate", cost."DocumentId"
                """).ToArrayAsync(ct)).WithTags("Write-offs");
    }
}

public sealed record WriteOffCostResponse(Guid WriteOffId, Guid DocumentId, DateOnly DocumentDate,
    Guid StorageLocationId, Guid WriteOffReasonId, Guid ExpenseCategoryId, decimal TotalCostUah);
