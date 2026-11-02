using Microsoft.AspNetCore.Mvc;
using Trade.Application;
using Trade.Application.Authentication;

namespace Trade.Api;

public static class ReportEndpoints
{
    private static ReportAccess Access(ICurrentPermissions p) => new(p.Has("reports.view"),p.Has("profit.view"),p.Has("cost.view"),p.Has("money.view"),p.Has("settlement.view"),
        p.Has("reports.view") || p.Has("sale.view"),p.Has("reports.view") || p.Has("purchase.view"),p.Has("reports.view") || p.Has("expense.view"),p.Has("reports.view") || p.Has("stock.view"),
        p.Has("reports.view") || p.Has("stock.transfer"),p.Has("reports.view") || p.Has("stock.writeoff"));
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard/summary",async ([AsParameters] ReportFilter filter,RequestContext context,ICurrentPermissions permissions,IReportQueries queries,CancellationToken ct) =>
            Results.Ok(await queries.DashboardAsync(context.OrganizationId,filter,Access(permissions),ct))).WithTags("Dashboard").Produces<DashboardSummary>();
        app.MapGet("/reports/lookups",async (RequestContext context,ICurrentPermissions permissions,IReportQueries queries,CancellationToken ct) =>
            Results.Ok(await queries.LookupsAsync(context.OrganizationId,Access(permissions),ct))).WithTags("Reports").Produces<ReportLookups>();
        foreach (var kind in new[] { "sales","profitability","stock","purchases","money","expenses","settlements" })
        {
            var reportKind = kind;
            app.MapGet($"/reports/{reportKind}",async ([AsParameters] ReportFilter filter,RequestContext context,ICurrentPermissions permissions,IReportQueries queries,CancellationToken ct) =>
                Results.Ok(await queries.ReportAsync(context.OrganizationId,reportKind,filter,Access(permissions),ct))).WithTags("Reports").Produces<ReportTable>();
        }
    }
}
