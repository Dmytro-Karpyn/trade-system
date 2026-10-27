using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BatchCostAndInitialPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "price_types",
                type: "boolean",
                nullable: false,
                // Existing price types were usable before Active was introduced.
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultMarkupPercent",
                table: "price_types",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "price_types",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_price_type_markup",
                table: "price_types",
                sql: "\"DefaultMarkupPercent\" >= 0");

            migrationBuilder.Sql("""
                CREATE VIEW v_batch_cost AS
                SELECT batch."Id" AS "BatchId", line."Id" AS "PurchaseInvoiceLineId", line."ProductId",
                    trim_scale(line."UnitPriceCurrency" * (1 - purchase."DiscountPercent" / 100::numeric)
                        * purchase."ExchangeRate") AS "UnitCostUah"
                FROM batches batch
                JOIN purchase_invoice_lines line ON line."Id" = batch."PurchaseInvoiceLineId"
                JOIN purchase_invoices purchase ON purchase."DocumentId" = line."PurchaseInvoiceId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW v_batch_cost;");
            migrationBuilder.DropCheckConstraint(
                name: "ck_price_type_markup",
                table: "price_types");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "price_types");

            migrationBuilder.DropColumn(
                name: "DefaultMarkupPercent",
                table: "price_types");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "price_types");
        }
    }
}
