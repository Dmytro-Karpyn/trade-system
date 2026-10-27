using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesAndReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.CreateTable(
                name: "sale_invoices",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_invoices", x => x.DocumentId);
                    table.CheckConstraint("ck_sale_discount", "\"DiscountPercent\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_sale_invoices_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_invoice_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    RecommendedUnitPrice = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_invoice_lines", x => x.Id);
                    table.CheckConstraint("ck_sale_line_no", "\"LineNo\" > 0");
                    table.CheckConstraint("ck_sale_line_prices", "\"UnitPrice\" >= 0 AND \"RecommendedUnitPrice\" >= 0");
                    table.CheckConstraint("ck_sale_line_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_sale_invoice_lines_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_invoice_lines_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_invoice_lines_sale_invoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "sale_invoices",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE')");

            migrationBuilder.CreateIndex(
                name: "IX_sale_invoice_lines_BatchId",
                table: "sale_invoice_lines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_invoice_lines_ProductId",
                table: "sale_invoice_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_invoice_lines_SaleInvoiceId_BatchId",
                table: "sale_invoice_lines",
                columns: new[] { "SaleInvoiceId", "BatchId" });

            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW v_stock_balance AS
                WITH physical AS (
                    SELECT "BatchId", "StorageLocationId", SUM("Quantity") AS quantity
                    FROM stock_movements GROUP BY "BatchId", "StorageLocationId"
                ), reserved AS (
                    SELECT l."BatchId", s."StorageLocationId", SUM(l."Quantity") AS quantity
                    FROM sale_invoice_lines l
                    JOIN sale_invoices s ON s."DocumentId" = l."SaleInvoiceId"
                    JOIN documents d ON d."Id" = s."DocumentId"
                    JOIN products p ON p."Id" = l."ProductId"
                    WHERE d."DeletedAt" IS NULL AND d."Status" = 'DRAFT' AND d."DocumentType" = 'SALE_INVOICE'
                        AND l."RemovedAt" IS NULL AND l."BatchId" IS NOT NULL AND p."ItemType" = 'GOODS'
                    GROUP BY l."BatchId", s."StorageLocationId"
                )
                SELECT COALESCE(p."BatchId", r."BatchId") AS "BatchId",
                    COALESCE(p."StorageLocationId", r."StorageLocationId") AS "StorageLocationId",
                    COALESCE(p.quantity, 0::numeric) AS "PhysicalQuantity",
                    COALESCE(r.quantity, 0::numeric) AS "ReservedQuantity",
                    COALESCE(p.quantity, 0::numeric) - COALESCE(r.quantity, 0::numeric) AS "AvailableQuantity"
                FROM physical p FULL OUTER JOIN reserved r
                    ON p."BatchId" = r."BatchId" AND p."StorageLocationId" = r."StorageLocationId";

                CREATE FUNCTION enforce_active_sale_batch_unique() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (
                        SELECT "BatchId" FROM sale_invoice_lines
                        WHERE "SaleInvoiceId" = NEW."SaleInvoiceId" AND "RemovedAt" IS NULL AND "BatchId" IS NOT NULL
                        GROUP BY "BatchId" HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'A Batch cannot occur twice among active sale lines'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_sale_active_batch_unique';
                    END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER active_sale_batch_unique
                AFTER INSERT OR UPDATE ON sale_invoice_lines
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION enforce_active_sale_batch_unique();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP VIEW v_stock_balance;
                CREATE VIEW v_stock_balance AS
                SELECT "BatchId", "StorageLocationId", SUM("Quantity") AS "PhysicalQuantity"
                FROM stock_movements GROUP BY "BatchId", "StorageLocationId";
                DROP TRIGGER active_sale_batch_unique ON sale_invoice_lines;
                DROP FUNCTION enforce_active_sale_batch_unique();
                """);

            migrationBuilder.DropTable(
                name: "sale_invoice_lines");

            migrationBuilder.DropTable(
                name: "sale_invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK')");
        }
    }
}
