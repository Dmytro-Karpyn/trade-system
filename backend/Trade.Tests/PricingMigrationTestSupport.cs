using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Trade.Infrastructure.Persistence;
using Trade.Infrastructure.Persistence.Migrations;

namespace Trade.Tests;

internal static class PricingMigrationTestSupport
{
    // Round-trip the pricing migrations themselves without dropping later modules' populated journals.
    // Counterparty now references PriceType, so temporarily detach that FK while recreating the older table.
    public static async Task RoundTripAsync(TradeDbContext db, bool recreatePricingTables = false)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        // The current write-off report depends on v_batch_cost. Recreate only its view, preserving all facts.
        var report = new WriteOffsAndCurrentCost().UpOperations.OfType<SqlOperation>()
            .Single(x => x.Sql.StartsWith("CREATE VIEW v_write_off_cost", StringComparison.Ordinal));
        await db.Database.ExecuteSqlRawAsync("DROP VIEW v_write_off_cost;");
        var cost = new BatchCostAndInitialPrices();
        await ExecuteAsync(generator.Generate(cost.DownOperations, db.Model));
        if (recreatePricingTables)
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE counterparties DROP CONSTRAINT \"FK_counterparties_price_types_DefaultPriceTypeId\"");
            var pricing = new Pricing();
            await ExecuteAsync(generator.Generate(pricing.DownOperations, db.Model));
            await ExecuteAsync(generator.Generate(pricing.UpOperations, db.Model));
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE counterparties ADD CONSTRAINT "FK_counterparties_price_types_DefaultPriceTypeId"
                FOREIGN KEY ("DefaultPriceTypeId") REFERENCES price_types ("Id") ON DELETE RESTRICT
                """);
        }
        await ExecuteAsync(generator.Generate(cost.UpOperations, db.Model));
        // Restore the later direct-expense extension before recreating the dependent report.
        // Only its final CREATE VIEW statement is reapplied, never its already-existing triggers/tables.
        const string currentCostStart = "CREATE OR REPLACE VIEW v_batch_cost AS";
        var expenses = new ExpensesAndDirectPurchaseCost().UpOperations.OfType<SqlOperation>()
            .Single(x => x.Sql.Contains(currentCostStart, StringComparison.Ordinal));
        var currentCost = new SqlOperation { Sql = expenses.Sql[expenses.Sql.IndexOf(currentCostStart, StringComparison.Ordinal)..] };
        await ExecuteAsync(generator.Generate([currentCost], db.Model));
        await ExecuteAsync(generator.Generate([report], db.Model));
        await transaction.CommitAsync();

        async Task ExecuteAsync(IReadOnlyList<MigrationCommand> commands)
        {
            foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        }
    }
}
