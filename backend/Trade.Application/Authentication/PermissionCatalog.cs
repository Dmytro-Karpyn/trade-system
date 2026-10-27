namespace Trade.Application.Authentication;

public sealed record PermissionDefinition(Guid Id, string Code, string Description);

public static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionDefinition> All = new (string Code, string Description)[]
    {
        ("product.view", "View products and selling prices"), ("product.create", "Create products"), ("product.edit", "Edit products"),
        ("purchase.view", "View purchases and supplier returns"), ("purchase.create", "Create/edit purchases and supplier returns"),
        ("purchase.post", "Post purchases and supplier returns"), ("purchase.unpost", "Unpost purchases and supplier returns"), ("purchase.delete", "Delete draft purchases/returns"),
        ("sale.view", "View sales and customer returns"), ("sale.create", "Create/edit sales and customer returns"),
        ("sale.post", "Post sales and customer returns"), ("sale.unpost", "Unpost sales and customer returns"), ("sale.delete", "Delete draft sales/returns"),
        ("sale.change_price", "Sell below the recommended price"),
        ("stock.view", "View stock and available batches"), ("stock.transfer", "Manage stock transfers"), ("stock.writeoff", "Manage write-offs and reasons"), ("cost.view", "View calculated costs"),
        ("money.view", "View money accounts/documents/balances"), ("money.receipt", "Manage money receipts"), ("money.payment", "Manage money payments"), ("money.transfer", "Manage money transfers"),
        ("expense.view", "View expenses/categories"), ("expense.create", "Create/edit expenses/categories"), ("expense.post", "Post expenses"), ("expense.unpost", "Unpost expenses"), ("expense.delete", "Delete draft expenses"),
        ("settlement.view", "View counterparties and settlements"), ("reports.view", "View reports"), ("profit.view", "View profit"),
        ("users.manage", "Manage users"), ("roles.manage", "Manage roles and their permissions"), ("settings.manage", "Manage counterparties and pricing settings"), ("audit.view", "View audit data")
    }.Select((p, i) => new PermissionDefinition(Guid.Parse($"10000000-0000-0000-0000-{i + 1:D12}"), p.Code, p.Description)).ToArray();

    public static readonly IReadOnlySet<string> Codes = All.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
    public static readonly IReadOnlyList<string> InitialRoles = ["OWNER", "SALES_MANAGER", "PURCHASE_MANAGER", "WAREHOUSE", "FINANCE", "ADMIN", "CASHIER"];
}

// A request-scoped result, never a field/cache persisted on a User or in the cookie.
public interface ICurrentPermissions
{
    IReadOnlySet<string> Codes { get; }
    IReadOnlyList<string> Roles { get; }
    Task LoadAsync(System.Security.Claims.ClaimsPrincipal principal, CancellationToken ct);
    bool Has(string code) => Codes.Contains(code);
}
