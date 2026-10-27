using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierReturnsAndReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.CreateTable(
                name: "supplier_returns",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePurchaseInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_returns", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_supplier_returns_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_returns_purchase_invoices_SourcePurchaseInvoiceId",
                        column: x => x.SourcePurchaseInvoiceId,
                        principalTable: "purchase_invoices",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_return_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePurchaseLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_return_lines", x => x.Id);
                    table.CheckConstraint("ck_supplier_return_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_supplier_return_lines_purchase_invoice_lines_SourcePurchase~",
                        column: x => x.SourcePurchaseLineId,
                        principalTable: "purchase_invoice_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_return_lines_supplier_returns_SupplierReturnId",
                        column: x => x.SupplierReturnId,
                        principalTable: "supplier_returns",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER', 'EXPENSE', 'CUSTOMER_RETURN', 'SUPPLIER_RETURN')");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_return_lines_SourcePurchaseLineId",
                table: "supplier_return_lines",
                column: "SourcePurchaseLineId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_return_lines_SupplierReturnId_SourcePurchaseLineId",
                table: "supplier_return_lines",
                columns: new[] { "SupplierReturnId", "SourcePurchaseLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_returns_SourcePurchaseInvoiceId",
                table: "supplier_returns",
                column: "SourcePurchaseInvoiceId");

            migrationBuilder.Sql("""
                ALTER TABLE supplier_return_lines ADD CONSTRAINT uq_supplier_return_source_line
                    UNIQUE ("SupplierReturnId", "SourcePurchaseLineId") DEFERRABLE INITIALLY DEFERRED;

                CREATE FUNCTION enforce_supplier_return_header() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE return_org uuid; source_org uuid; source_status text;
                BEGIN
                    SELECT "OrganizationId" INTO return_org FROM documents
                        WHERE "Id" = NEW."DocumentId" AND "DocumentType" = 'SUPPLIER_RETURN';
                    SELECT "OrganizationId", "Status" INTO source_org, source_status FROM documents
                        WHERE "Id" = NEW."SourcePurchaseInvoiceId" AND "DocumentType" = 'PURCHASE_INVOICE'
                            AND "DeletedAt" IS NULL FOR UPDATE;
                    IF return_org IS NULL OR source_org IS DISTINCT FROM return_org OR source_status <> 'POSTED' THEN
                        RAISE EXCEPTION 'SupplierReturn requires a POSTED PURCHASE_INVOICE of its organization' USING ERRCODE = '23514';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM purchase_invoices p JOIN documents d ON d."Id" = p."DocumentId"
                            WHERE p."StorageLocationId" = NEW."StorageLocationId" AND d."OrganizationId" = return_org)
                        AND NOT EXISTS (SELECT 1 FROM sale_invoices s JOIN documents d ON d."Id" = s."DocumentId"
                            WHERE s."StorageLocationId" = NEW."StorageLocationId" AND d."OrganizationId" = return_org) THEN
                        RAISE EXCEPTION 'SupplierReturn storage must already be used by its organization' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER supplier_return_header BEFORE INSERT OR UPDATE ON supplier_returns
                    FOR EACH ROW EXECUTE FUNCTION enforce_supplier_return_header();

                CREATE FUNCTION enforce_supplier_return_lines() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE return_id uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'supplier_returns' THEN return_id := NEW."DocumentId";
                    ELSE SELECT "SupplierReturnId" INTO return_id FROM supplier_return_lines WHERE "Id" = NEW."Id";
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM supplier_return_lines line
                        JOIN supplier_returns header ON header."DocumentId" = line."SupplierReturnId"
                        JOIN purchase_invoice_lines source ON source."Id" = line."SourcePurchaseLineId"
                        JOIN products product ON product."Id" = source."ProductId"
                        JOIN documents document ON document."Id" = header."DocumentId"
                        LEFT JOIN batches batch ON batch."PurchaseInvoiceLineId" = source."Id"
                        WHERE line."SupplierReturnId" = return_id AND (source."PurchaseInvoiceId" <> header."SourcePurchaseInvoiceId"
                            OR source."RemovedAt" IS NOT NULL OR product."OrganizationId" <> document."OrganizationId"
                            OR product."ItemType" <> 'GOODS' OR batch."Id" IS NULL)
                    ) THEN RAISE EXCEPTION 'Invalid supplier return source line or Batch' USING ERRCODE = '23514'; END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER supplier_return_line_scope AFTER INSERT OR UPDATE ON supplier_return_lines
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_supplier_return_lines();
                CREATE CONSTRAINT TRIGGER supplier_return_header_line_scope AFTER INSERT OR UPDATE ON supplier_returns
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_supplier_return_lines();

                CREATE FUNCTION protect_purchase_supplier_return_dependencies() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."DocumentType" = 'PURCHASE_INVOICE' AND (NEW."Status" <> 'POSTED' OR NEW."DeletedAt" IS NOT NULL)
                        AND EXISTS (SELECT 1 FROM supplier_returns r JOIN documents d ON d."Id" = r."DocumentId"
                            WHERE r."SourcePurchaseInvoiceId" = OLD."Id" AND d."DeletedAt" IS NULL) THEN
                        RAISE EXCEPTION 'DOCUMENT_HAS_DEPENDENCIES' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER purchase_supplier_return_dependencies BEFORE UPDATE OF "Status", "DeletedAt" ON documents
                    FOR EACH ROW EXECUTE FUNCTION protect_purchase_supplier_return_dependencies();

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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                DROP TRIGGER purchase_supplier_return_dependencies ON documents;
                DROP FUNCTION protect_purchase_supplier_return_dependencies();
                DROP TRIGGER supplier_return_header_line_scope ON supplier_returns;
                DROP TRIGGER supplier_return_line_scope ON supplier_return_lines;
                DROP FUNCTION enforce_supplier_return_lines();
                DROP TRIGGER supplier_return_header ON supplier_returns;
                DROP FUNCTION enforce_supplier_return_header();
                """);
            migrationBuilder.DropTable(
                name: "supplier_return_lines");

            migrationBuilder.DropTable(
                name: "supplier_returns");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER', 'EXPENSE', 'CUSTOMER_RETURN')");
        }
    }
}
