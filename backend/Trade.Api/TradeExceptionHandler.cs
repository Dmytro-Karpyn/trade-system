using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trade.Application;
using Trade.Domain.Common;
using Trade.Domain.Pricing;
using Trade.Domain.Documents;
using Trade.Domain.Expenses;
using Trade.Domain.Returns;

namespace Trade.Api;

public sealed class TradeExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is ReturnQuantityExceededException or ReturnAmountExceededException)
        {
            context.Response.StatusCode = 409;
            await Results.Problem(statusCode: 409, detail: "The return exceeds the remaining source quantity or refundable amount.",
                extensions: new Dictionary<string, object?> { ["code"] = exception.Message }).ExecuteAsync(context);
            return true;
        }
        if (exception is InvalidPurchaseExpenseAllocationException or DocumentHasDependenciesException)
        {
            var dependency = exception is DocumentHasDependenciesException;
            var statusCode = dependency ? 409 : 400;
            context.Response.StatusCode = statusCode;
            await Results.Problem(statusCode: statusCode,
                detail: dependency ? "The document has active dependent documents. Delete or detach their drafts first."
                    : "Direct expenses cannot be allocated to a zero-total purchase.",
                extensions: new Dictionary<string, object?> { ["code"] = exception.Message }).ExecuteAsync(context);
            return true;
        }
        if (exception is PriceBelowRecommendedException or PriceNotFoundException)
        {
            var denied = exception is PriceBelowRecommendedException;
            var statusCode = denied ? 403 : 400;
            context.Response.StatusCode = statusCode;
            await Results.Problem(statusCode: statusCode,
                detail: denied ? "Selling below the recommended unit price requires explicit permission."
                    : "A price is missing for the selected PriceType and Batch or SERVICE Product.",
                extensions: new Dictionary<string, object?> { ["code"] = exception.Message }).ExecuteAsync(context);
            return true;
        }
        if (exception is InsufficientStockException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await Results.Problem(statusCode: 409, title: "Insufficient stock",
                detail: "The operation would exceed available or physical stock.",
                extensions: new Dictionary<string, object?> { ["code"] = "INSUFFICIENT_STOCK" }).ExecuteAsync(context);
            return true;
        }
        var (status, detail) = exception switch
        {
            BadHttpRequestException e => (e.StatusCode, "The request body or parameters are invalid."),
            DomainException e => (400, e.Message),
            NotFoundException e => (404, e.Message),
            ConflictException e => (409, e.Message),
            UnauthorizedAccessException e => (401, e.Message),
            DbUpdateConcurrencyException => (409, "Concurrent modification. Reload the document and retry."),
            DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } =>
                (409, "A record with the same unique key already exists."),
            DbUpdateException { InnerException: PostgresException { SqlState: "23503" or "23514" or "22003" } } =>
                (400, "The operation violates a reference or business constraint."),
            PostgresException { SqlState: "23503" or "23514" or "22003" } =>
                (400, "The operation violates a reference or business constraint."),
            DbUpdateException { InnerException: PostgresException { SqlState: "40P01" or "40001" } } =>
                (409, "Concurrent transaction conflict. Retry the operation."),
            PostgresException { SqlState: "40P01" or "40001" } =>
                (409, "Concurrent transaction conflict. Retry the operation."),
            _ => (0, "")
        };
        if (status == 0) return false;
        context.Response.StatusCode = status;
        await Results.Problem(statusCode: status, detail: detail).ExecuteAsync(context);
        return true;
    }
}
