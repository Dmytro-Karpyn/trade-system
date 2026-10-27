using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WriteOffsAndCurrentCost : Migration
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
                UNION ALL
                SELECT line."BatchId", header."StorageLocationId", line."Quantity"
                FROM write_off_lines line
                JOIN write_offs header ON header."DocumentId" = line."WriteOffId"
                JOIN documents document ON document."Id" = header."DocumentId"
                WHERE document."DeletedAt" IS NULL AND document."Status" = 'DRAFT' AND document."DocumentType" = 'WRITE_OFF'
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
                name: "write_off_reasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_write_off_reasons", x => x.Id);
                    table.CheckConstraint("ck_write_off_reason_name", "btrim(\"Name\") <> ''");
                    table.ForeignKey(
                        name: "FK_write_off_reasons_expense_categories_ExpenseCategoryId",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "expense_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "write_offs",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WriteOffReasonId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_write_offs", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_write_offs_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_write_offs_write_off_reasons_WriteOffReasonId",
                        column: x => x.WriteOffReasonId,
                        principalTable: "write_off_reasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "write_off_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WriteOffId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_write_off_lines", x => x.Id);
                    table.CheckConstraint("ck_write_off_line_no", "\"LineNo\" > 0");
                    table.CheckConstraint("ck_write_off_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_write_off_lines_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_write_off_lines_write_offs_WriteOffId",
                        column: x => x.WriteOffId,
                        principalTable: "write_offs",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER', 'EXPENSE', 'CUSTOMER_RETURN', 'SUPPLIER_RETURN', 'STOCK_TRANSFER', 'WRITE_OFF')");

            migrationBuilder.CreateIndex(
                name: "IX_write_off_lines_BatchId",
                table: "write_off_lines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_write_off_lines_WriteOffId_BatchId",
                table: "write_off_lines",
                columns: new[] { "WriteOffId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_write_off_lines_WriteOffId_LineNo",
                table: "write_off_lines",
                columns: new[] { "WriteOffId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_write_off_reasons_ExpenseCategoryId",
                table: "write_off_reasons",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_write_off_reasons_OrganizationId_Name",
                table: "write_off_reasons",
                columns: new[] { "OrganizationId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_write_offs_WriteOffReasonId",
                table: "write_offs",
                column: "WriteOffReasonId");

            migrationBuilder.Sql("""
                ALTER TABLE write_off_lines ADD CONSTRAINT uq_write_off_batch
                    UNIQUE ("WriteOffId", "BatchId") DEFERRABLE INITIALLY DEFERRED;
                ALTER TABLE write_off_lines ADD CONSTRAINT uq_write_off_line_no
                    UNIQUE ("WriteOffId", "LineNo") DEFERRABLE INITIALLY DEFERRED;

                CREATE FUNCTION enforce_write_off_reason_category() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM expense_categories
                        WHERE "Id" = NEW."ExpenseCategoryId" AND "OrganizationId" = NEW."OrganizationId") THEN
                        RAISE EXCEPTION 'Reason category must belong to its organization' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER write_off_reason_category BEFORE INSERT OR UPDATE ON write_off_reasons
                    FOR EACH ROW EXECUTE FUNCTION enforce_write_off_reason_category();

                CREATE FUNCTION enforce_write_off_header() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE write_off_org uuid;
                BEGIN
                    SELECT "OrganizationId" INTO write_off_org FROM documents
                        WHERE "Id" = NEW."DocumentId" AND "DocumentType" = 'WRITE_OFF';
                    IF write_off_org IS NULL OR NOT EXISTS (SELECT 1 FROM write_off_reasons
                        WHERE "Id" = NEW."WriteOffReasonId" AND "OrganizationId" = write_off_org) THEN
                        RAISE EXCEPTION 'WriteOff and reason must belong to the same organization' USING ERRCODE = '23514';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM purchase_invoices p JOIN documents d ON d."Id" = p."DocumentId"
                            WHERE p."StorageLocationId" = NEW."StorageLocationId" AND d."OrganizationId" = write_off_org)
                        AND NOT EXISTS (SELECT 1 FROM sale_invoices s JOIN documents d ON d."Id" = s."DocumentId"
                            WHERE s."StorageLocationId" = NEW."StorageLocationId" AND d."OrganizationId" = write_off_org) THEN
                        RAISE EXCEPTION 'WriteOff storage must belong to its organization' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER write_off_header BEFORE INSERT OR UPDATE ON write_offs
                    FOR EACH ROW EXECUTE FUNCTION enforce_write_off_header();

                CREATE FUNCTION enforce_write_off_batch_scope() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE write_off_id uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'write_offs' THEN write_off_id := NEW."DocumentId";
                    ELSE SELECT "WriteOffId" INTO write_off_id FROM write_off_lines WHERE "Id" = NEW."Id";
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM write_off_lines line
                        JOIN write_offs header ON header."DocumentId" = line."WriteOffId"
                        JOIN documents document ON document."Id" = header."DocumentId"
                        JOIN batches batch ON batch."Id" = line."BatchId"
                        JOIN purchase_invoice_lines source ON source."Id" = batch."PurchaseInvoiceLineId"
                        JOIN purchase_invoices purchase ON purchase."DocumentId" = source."PurchaseInvoiceId"
                        JOIN documents purchase_document ON purchase_document."Id" = purchase."DocumentId"
                        WHERE line."WriteOffId" = write_off_id AND purchase_document."OrganizationId" <> document."OrganizationId"
                    ) THEN RAISE EXCEPTION 'WriteOff Batch must belong to its organization' USING ERRCODE = '23514'; END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER write_off_line_scope AFTER INSERT OR UPDATE ON write_off_lines
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_write_off_batch_scope();
                CREATE CONSTRAINT TRIGGER write_off_header_line_scope AFTER INSERT OR UPDATE ON write_offs
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_write_off_batch_scope();
                """);
            migrationBuilder.Sql(StockBalanceSql);
            migrationBuilder.Sql("""
                CREATE VIEW v_write_off_cost AS
                SELECT header."DocumentId" AS "WriteOffId", document."Id" AS "DocumentId", document."DocumentDate",
                    header."StorageLocationId", header."WriteOffReasonId", reason."ExpenseCategoryId",
                    trim_scale(COALESCE(SUM(line."Quantity" * cost."UnitCostUah"), 0::numeric)) AS "TotalCostUah"
                FROM write_offs header JOIN documents document ON document."Id" = header."DocumentId"
                JOIN write_off_reasons reason ON reason."Id" = header."WriteOffReasonId"
                LEFT JOIN write_off_lines line ON line."WriteOffId" = header."DocumentId"
                LEFT JOIN v_batch_cost cost ON cost."BatchId" = line."BatchId"
                WHERE document."DeletedAt" IS NULL
                GROUP BY header."DocumentId", document."Id", document."DocumentDate", header."StorageLocationId",
                    header."WriteOffReasonId", reason."ExpenseCategoryId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW v_write_off_cost;");
            migrationBuilder.Sql(PreviousStockBalanceSql);
            migrationBuilder.Sql("""
                DROP TRIGGER write_off_header_line_scope ON write_offs;
                DROP TRIGGER write_off_line_scope ON write_off_lines;
                DROP FUNCTION enforce_write_off_batch_scope();
                DROP TRIGGER write_off_header ON write_offs;
                DROP FUNCTION enforce_write_off_header();
                DROP TRIGGER write_off_reason_category ON write_off_reasons;
                DROP FUNCTION enforce_write_off_reason_category();
                """);
            migrationBuilder.DropTable(
                name: "write_off_lines");

            migrationBuilder.DropTable(
                name: "write_offs");

            migrationBuilder.DropTable(
                name: "write_off_reasons");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER', 'EXPENSE', 'CUSTOMER_RETURN', 'SUPPLIER_RETURN', 'STOCK_TRANSFER')");
        }
    }
}
