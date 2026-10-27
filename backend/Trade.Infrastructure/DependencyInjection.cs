using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trade.Application;
using Trade.Infrastructure.Health;
using Trade.Infrastructure.Persistence;

namespace Trade.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TradeDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("TradeDatabase")
                ?? throw new InvalidOperationException("ConnectionStrings:TradeDatabase is required.");
            options.UseNpgsql(connectionString);
        });

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgresql");
        services.AddScoped<ITradeStore, EfTradeStore>();
        services.AddScoped<ProductService>();
        services.AddScoped<UnitService>();
        services.AddScoped<PurchaseService>();
        services.AddScoped<PurchaseQueryService>();
        services.AddScoped<SaleService>();
        services.AddScoped<PricingService>();
        services.AddScoped<CounterpartyService>();
        services.AddScoped<SettlementService>();
        services.AddScoped<MoneyService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<CustomerReturnService>();
        services.AddScoped<SupplierReturnService>();
        services.AddScoped<StockTransferService>();
        services.AddScoped<WriteOffService>();
        return services;
    }
}
