using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockTransfersAndReservations : Migration
    {
        private const string PreviousStockBalanceSql = """
            CREATE OR REPLACE VIEW v_stock_balance AS
            WITH physical AS (
                SELECT "BatchId", "StorageLocationId", SUM("Quantity") AS quantity
                FROM stock_movements GROUP BY "BatchId", "StorageLocationId"
            ), reserve_lines AS (
                SELECT line."BatchId", sale."StorageLocationId", line."Quantity"
                FROM sale_invoice_lines line
                JOIN sale_invoices sale ON sale."DocumentId" = line."SaleInvoiceId"
                JOIN documents document ON document."Id" = sale."DocumentId"
                JOIN products product ON product."Id" = line."ProductId"
                WHERE document."DeletedAt" IS NULL AND document."Status" = 'DRAFT' AND document."DocumentType" = 'SALE_INVOICE'
                    AND line."RemovedAt" IS NULL AND line."BatchId" IS NOT NULL AND product."ItemType" = 'GOODS'
                UNION ALL
                SELECT batch."Id" AS "BatchId", header."StorageLocationId", line."Quantity"
                FROM supplier_return_lines line
                JOIN supplier_returns header ON header."DocumentId" = line."SupplierReturnId"
                JOIN documents document ON document."Id" = header."DocumentId"
                JOIN purchase_invoice_lines source ON source."Id" = line."SourcePurchaseLineId"
                JOIN products product ON product."Id" = source."ProductId"
                JOIN batches batch ON batch."PurchaseInvoiceLineId" = source."Id"
                WHERE document."DeletedAt" IS NULL AND document."Status" = 'DRAFT' AND document."DocumentType" = 'SUPPLIER_RETURN'
                    AND source."RemovedAt" IS NULL AND product."ItemType" = 'GOODS'
            ), reserved AS (
                SELECT "BatchId", "StorageLocationId", SUM("Quantity") AS quantity
                FROM reserve_lines GROUP BY "BatchId", "StorageLocationId"
            )
            SELECT COALESCE(p."BatchId", r."BatchId") AS "BatchId",
                COALESCE(p."StorageLocationId", r."StorageLocationId") AS "StorageLocationId",
                COALESCE(p.quantity, 0::numeric) AS "PhysicalQuantity",
                COALESCE(r.quantity, 0::numeric) AS "ReservedQuantity",
                COALESCE(p.quantity, 0::numeric) - COALESCE(r.quantity, 0::numeric) AS "AvailableQuantity"
            FROM physical p FULL OUTER JOIN reserved r
                ON p."BatchId" = r."BatchId" AND p."StorageLocationId" = r."StorageLocationId";
            """;

        private const string StockBalanceSql = """
            CREATE OR REPLACE VIEW v_stock_balance AS
            WITH physical AS (
                SELECT "BatchId", "StorageLocationId", SUM("Quantity") AS quantity
                FROM stock_movements GROUP BY "BatchId", "StorageLocationId"
            ), reserve_lines AS (
                SELECT line."BatchId", sale."StorageLocationId", line."Quantity"
                FROM sale_invoice_lines line
                JOIN sale_invoices sale ON sale."DocumentId" = line."SaleInvoiceId"
                JOIN documents document ON document."Id" = sale."DocumentId"
                JOIN products product ON product."Id" = line."ProductId"
                WHERE document."DeletedAt" IS NULL AND document."Status" = 'DRAFT' AND document."DocumentType" = 'SALE_INVOICE'
                    AND line."RemovedAt" IS NULL AND line."BatchId" IS NOT NULL AND product."ItemType" = 'GOODS'
                UNION ALL
                SELECT batch."Id" AS "BatchId", header."StorageLocationId", line."Quantity"
                FROM supplier_return_lines line
                JOIN supplier_returns header ON header."DocumentId" = line."SupplierReturnId"
                JOIN documents document ON document."Id" = header."DocumentId"
                JOIN purchase_invoice_lines source ON source."Id" = line."SourcePurchaseLineId"
                JOIN products product ON product."Id" = source."ProductId"
                JOIN batches batch ON batch."PurchaseInvoiceLineId" = source."Id"
                WHERE document."DeletedAt" IS NULL AND document."Status" = 'DRAFT' AND document."DocumentType" = 'SUPPLIER_RETURN'
                    AND source."RemovedAt" IS NULL AND product."ItemType" = 'GOODS'
                UNION ALL
                SELECT line."BatchId", transfer."FromLocationId" AS "StorageLocationId", line."Quantity"
                FROM stock_transfer_lines line
                JOIN stock_transfers transfer ON transfer."DocumentId" = line."StockTransferId"
                JOIN documents document ON document."Id" = transfer."DocumentId"
                WHERE document."DeletedAt" IS NULL AND document."Status" = 'DRAFT' AND document."DocumentType" = 'STOCK_TRANSFER'
            ), reserved AS (
                SELECT "BatchId", "StorageLocationId", SUM("Quantity") AS quantity
                FROM reserve_lines GROUP BY "BatchId", "StorageLocationId"
            )
            SELECT COALESCE(p."BatchId", r."BatchId") AS "BatchId",
                COALESCE(p."StorageLocationId", r."StorageLocationId") AS "StorageLocationId",
                COALESCE(p.quantity, 0::numeric) AS "PhysicalQuantity",
                COALESCE(r.quantity, 0::numeric) AS "ReservedQuantity",
                COALESCE(p.quantity, 0::numeric) - COALESCE(r.quantity, 0::numeric) AS "AvailableQuantity"
            FROM physical p FULL OUTER JOIN reserved r
                ON p."BatchId" = r."BatchId" AND p."StorageLocationId" = r."StorageLocationId";
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToLocationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_transfers", x => x.DocumentId);
                    table.CheckConstraint("ck_stock_transfer_locations", "\"FromLocationId\" <> \"ToLocationId\"");
                    table.ForeignKey(
                        name: "FK_stock_transfers_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_transfer_lines", x => x.Id);
                    table.CheckConstraint("ck_stock_transfer_line_no", "\"LineNo\" > 0");
                    table.CheckConstraint("ck_stock_transfer_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_stock_transfer_lines_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_transfer_lines_stock_transfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "stock_transfers",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER', 'EXPENSE', 'CUSTOMER_RETURN', 'SUPPLIER_RETURN', 'STOCK_TRANSFER')");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_lines_BatchId",
                table: "stock_transfer_lines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_lines_StockTransferId_BatchId",
                table: "stock_transfer_lines",
                columns: new[] { "StockTransferId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_lines_StockTransferId_LineNo",
                table: "stock_transfer_lines",
                columns: new[] { "StockTransferId", "LineNo" });

            migrationBuilder.Sql("""
                ALTER TABLE stock_transfer_lines ADD CONSTRAINT uq_stock_transfer_batch
                    UNIQUE ("StockTransferId", "BatchId") DEFERRABLE INITIALLY DEFERRED;
                ALTER TABLE stock_transfer_lines ADD CONSTRAINT uq_stock_transfer_line_no
                    UNIQUE ("StockTransferId", "LineNo") DEFERRABLE INITIALLY DEFERRED;

                CREATE FUNCTION enforce_stock_transfer_header() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE transfer_org uuid; location_id uuid;
                BEGIN
                    SELECT "OrganizationId" INTO transfer_org FROM documents
                        WHERE "Id" = NEW."DocumentId" AND "DocumentType" = 'STOCK_TRANSFER';
                    IF transfer_org IS NULL THEN
                        RAISE EXCEPTION 'StockTransfer requires STOCK_TRANSFER DocumentType' USING ERRCODE = '23514';
                    END IF;
                    FOREACH location_id IN ARRAY ARRAY[NEW."FromLocationId", NEW."ToLocationId"] LOOP
                        IF NOT EXISTS (SELECT 1 FROM purchase_invoices p JOIN documents d ON d."Id" = p."DocumentId"
                                WHERE p."StorageLocationId" = location_id AND d."OrganizationId" = transfer_org)
                            AND NOT EXISTS (SELECT 1 FROM sale_invoices s JOIN documents d ON d."Id" = s."DocumentId"
                                WHERE s."StorageLocationId" = location_id AND d."OrganizationId" = transfer_org) THEN
                            RAISE EXCEPTION 'Both transfer locations must belong to its organization' USING ERRCODE = '23514';
                        END IF;
                    END LOOP;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER stock_transfer_header BEFORE INSERT OR UPDATE ON stock_transfers
                    FOR EACH ROW EXECUTE FUNCTION enforce_stock_transfer_header();

                CREATE FUNCTION enforce_stock_transfer_batch_scope() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE transfer_id uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'stock_transfers' THEN transfer_id := NEW."DocumentId";
                    ELSE SELECT "StockTransferId" INTO transfer_id FROM stock_transfer_lines WHERE "Id" = NEW."Id";
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM stock_transfer_lines line
                        JOIN stock_transfers transfer ON transfer."DocumentId" = line."StockTransferId"
                        JOIN documents document ON document."Id" = transfer."DocumentId"
                        JOIN batches batch ON batch."Id" = line."BatchId"
                        JOIN purchase_invoice_lines source ON source."Id" = batch."PurchaseInvoiceLineId"
                        JOIN purchase_invoices purchase ON purchase."DocumentId" = source."PurchaseInvoiceId"
                        JOIN documents purchase_document ON purchase_document."Id" = purchase."DocumentId"
                        WHERE line."StockTransferId" = transfer_id AND purchase_document."OrganizationId" <> document."OrganizationId"
                    ) THEN RAISE EXCEPTION 'Transfer Batch must belong to its organization' USING ERRCODE = '23514'; END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER stock_transfer_line_scope AFTER INSERT OR UPDATE ON stock_transfer_lines
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_stock_transfer_batch_scope();
                CREATE CONSTRAINT TRIGGER stock_transfer_header_line_scope AFTER INSERT OR UPDATE ON stock_transfers
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_stock_transfer_batch_scope();
                """);
            migrationBuilder.Sql(StockBalanceSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PreviousStockBalanceSql);
            migrationBuilder.Sql("""
                DROP TRIGGER stock_transfer_header_line_scope ON stock_transfers;
                DROP TRIGGER stock_transfer_line_scope ON stock_transfer_lines;
                DROP FUNCTION enforce_stock_transfer_batch_scope();
                DROP TRIGGER stock_transfer_header ON stock_transfers;
                DROP FUNCTION enforce_stock_transfer_header();
                """);
            migrationBuilder.DropTable(
                name: "stock_transfer_lines");

            migrationBuilder.DropTable(
                name: "stock_transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER', 'EXPENSE', 'CUSTOMER_RETURN', 'SUPPLIER_RETURN')");
        }
    }
}
