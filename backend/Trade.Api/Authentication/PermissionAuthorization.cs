using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Trade.Application;
using Trade.Application.Authentication;
using Trade.Domain.Pricing;

namespace Trade.Api.Authentication;

public sealed record PermissionRequirement(IReadOnlyList<string> Codes) : IAuthorizationRequirement;

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "permission:";
    public override Task<AuthorizationPolicy?> GetPolicyAsync(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal)) return base.GetPolicyAsync(name);
        var codes = name[Prefix.Length..].Split('|');
        if (codes.Length == 0 || codes.Any(c => !PermissionCatalog.Codes.Contains(c))) throw new InvalidOperationException("Unknown permission policy.");
        return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(codes)).Build());
    }
}

public sealed class PermissionHandler(ICurrentPermissions permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && requirement.Codes.Any(permissions.Has)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class SalePricePermissionProvider(ICurrentPermissions permissions) : ISalePricePermissionProvider
{
    public SalePricePermissions GetPermissions() => permissions.Has("sale.change_price") ? new(true) : SalePricePermissions.None;
}

public static class BusinessAuthorization
{
    // Central mapping for existing slices. Unknown routes fail closed instead of silently shipping without a permission.
    public static void Apply(EndpointBuilder builder)
    {
        if (builder is not RouteEndpointBuilder route) throw new InvalidOperationException("Expected a route endpoint.");
        var path = route.RoutePattern.RawText?.Trim('/') ?? "";
        var method = builder.Metadata.OfType<HttpMethodMetadata>().Last().HttpMethods.Single();
        builder.Metadata.Add(new AuthorizeAttribute(PermissionPolicyProvider.Prefix + Resolve(path, method)));
    }

    public static string Resolve(string path, string method)
    {
        var read = method == "GET";
        var root = path.Split('/')[0];
        if (path == "write-offs/costs") return "cost.view";
        if (root == "purchases" && path.EndsWith("/batch-costs", StringComparison.Ordinal)) return "cost.view";
        if (path == "purchases/lookups") return "purchase.view|purchase.create";
        if (root == "storage-locations") return "purchase.create|settings.manage";
        if (root is "price-types" or "batches" || root == "products" && path.Contains("/prices/"))
            return read ? "product.view|sale.view|purchase.view" : "settings.manage";
        if (root == "units") return read ? "product.view|product.create|product.edit|settings.manage" : "product.create|product.edit|settings.manage";
        if (root == "products") return read ? "product.view" : method == "POST" ? "product.create" : "product.edit";
        if (root is "purchases" or "supplier-returns") return DocumentPermission("purchase", path, method);
        if (root is "sales" or "customer-returns") return DocumentPermission("sale", path, method);
        if (root == "stock") return "stock.view";
        if (root == "stock-transfers") return "stock.transfer";
        if (root is "write-offs" or "write-off-reasons") return "stock.writeoff";
        if (root == "money")
        {
            if (read) return "money.view";
            return path.Split('/')[1] switch
            {
                "accounts" => "settings.manage", "receipts" => "money.receipt", "payments" => "money.payment", "transfers" => "money.transfer",
                _ => throw new InvalidOperationException($"No money permission mapping: {path}")
            };
        }
        if (root == "expense-categories") return read ? "expense.view" : "expense.create";
        if (root == "expenses") return DocumentPermission("expense", path, method);
        if (root == "settlements") return "settlement.view";
        if (root == "counterparties") return read ? "settlement.view|sale.view|purchase.view" : "settings.manage";
        throw new InvalidOperationException($"No permission mapping for {method} /{path}");
    }
    private static string DocumentPermission(string slice, string path, string method) => slice + "." + (method == "GET" ? "view"
        : method == "DELETE" ? "delete" : path.EndsWith("/unpost", StringComparison.Ordinal) ? "unpost"
        : path.EndsWith("/post", StringComparison.Ordinal) ? "post" : "create");
}
