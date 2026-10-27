using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.CreateTable(
                name: "customer_returns",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSaleInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_returns", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_customer_returns_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_returns_sale_invoices_SourceSaleInvoiceId",
                        column: x => x.SourceSaleInvoiceId,
                        principalTable: "sale_invoices",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_return_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSaleLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_return_lines", x => x.Id);
                    table.CheckConstraint("ck_customer_return_quantity", "\"Quantity\" >= 0");
                    table.CheckConstraint("ck_customer_return_refund", "\"RefundAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_customer_return_lines_customer_returns_CustomerReturnId",
                        column: x => x.CustomerReturnId,
                        principalTable: "customer_returns",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_return_lines_sale_invoice_lines_SourceSaleLineId",
                        column: x => x.SourceSaleLineId,
                        principalTable: "sale_invoice_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER', 'EXPENSE', 'CUSTOMER_RETURN')");

            migrationBuilder.CreateIndex(
                name: "IX_customer_return_lines_CustomerReturnId_SourceSaleLineId",
                table: "customer_return_lines",
                columns: new[] { "CustomerReturnId", "SourceSaleLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_return_lines_SourceSaleLineId",
                table: "customer_return_lines",
                column: "SourceSaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_returns_SourceSaleInvoiceId",
                table: "customer_returns",
                column: "SourceSaleInvoiceId");

            migrationBuilder.Sql("""
                ALTER TABLE customer_return_lines ADD CONSTRAINT uq_customer_return_source_line
                    UNIQUE ("CustomerReturnId", "SourceSaleLineId") DEFERRABLE INITIALLY DEFERRED;

                CREATE FUNCTION enforce_customer_return_header() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE return_org uuid; source_org uuid; source_status text;
                BEGIN
                    SELECT "OrganizationId" INTO return_org FROM documents
                        WHERE "Id" = NEW."DocumentId" AND "DocumentType" = 'CUSTOMER_RETURN';
                    SELECT "OrganizationId", "Status" INTO source_org, source_status FROM documents
                        WHERE "Id" = NEW."SourceSaleInvoiceId" AND "DocumentType" = 'SALE_INVOICE' AND "DeletedAt" IS NULL FOR UPDATE;
                    IF return_org IS NULL OR source_org IS DISTINCT FROM return_org OR source_status <> 'POSTED' THEN
                        RAISE EXCEPTION 'Return requires a POSTED source sale in its organization' USING ERRCODE = '23514';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM purchase_invoices p JOIN documents d ON d."Id" = p."DocumentId"
                            WHERE p."StorageLocationId" = NEW."StorageLocationId" AND d."OrganizationId" = return_org)
                        AND NOT EXISTS (SELECT 1 FROM sale_invoices s JOIN documents d ON d."Id" = s."DocumentId"
                            WHERE s."StorageLocationId" = NEW."StorageLocationId" AND d."OrganizationId" = return_org) THEN
                        RAISE EXCEPTION 'Return storage must already be used by its organization' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER customer_return_header BEFORE INSERT OR UPDATE ON customer_returns
                    FOR EACH ROW EXECUTE FUNCTION enforce_customer_return_header();

                CREATE FUNCTION enforce_customer_return_lines() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE return_id uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'customer_returns' THEN return_id := NEW."DocumentId";
                    ELSE SELECT "CustomerReturnId" INTO return_id FROM customer_return_lines WHERE "Id" = NEW."Id";
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM customer_return_lines line
                        JOIN customer_returns header ON header."DocumentId" = line."CustomerReturnId"
                        JOIN sale_invoice_lines source ON source."Id" = line."SourceSaleLineId"
                        JOIN products product ON product."Id" = source."ProductId"
                        JOIN documents document ON document."Id" = header."DocumentId"
                        WHERE line."CustomerReturnId" = return_id AND (source."SaleInvoiceId" <> header."SourceSaleInvoiceId"
                            OR source."RemovedAt" IS NOT NULL OR product."OrganizationId" <> document."OrganizationId"
                            OR (product."ItemType" = 'GOODS' AND (line."Quantity" <= 0 OR source."BatchId" IS NULL))
                            OR (product."ItemType" = 'SERVICE' AND (line."RefundAmount" <= 0 OR source."BatchId" IS NOT NULL)))
                    ) THEN RAISE EXCEPTION 'Invalid return source line or quantity/refund' USING ERRCODE = '23514'; END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER customer_return_line_scope AFTER INSERT OR UPDATE ON customer_return_lines
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_customer_return_lines();
                CREATE CONSTRAINT TRIGGER customer_return_header_line_scope AFTER INSERT OR UPDATE ON customer_returns
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_customer_return_lines();

                CREATE FUNCTION protect_sale_return_dependencies() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."DocumentType" = 'SALE_INVOICE' AND (NEW."Status" <> 'POSTED' OR NEW."DeletedAt" IS NOT NULL)
                        AND EXISTS (SELECT 1 FROM customer_returns r JOIN documents d ON d."Id" = r."DocumentId"
                            WHERE r."SourceSaleInvoiceId" = OLD."Id" AND d."DeletedAt" IS NULL) THEN
                        RAISE EXCEPTION 'DOCUMENT_HAS_DEPENDENCIES' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER sale_return_dependencies BEFORE UPDATE OF "Status", "DeletedAt" ON documents
                    FOR EACH ROW EXECUTE FUNCTION protect_sale_return_dependencies();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER sale_return_dependencies ON documents;
                DROP FUNCTION protect_sale_return_dependencies();
                DROP TRIGGER customer_return_header_line_scope ON customer_returns;
                DROP TRIGGER customer_return_line_scope ON customer_return_lines;
                DROP FUNCTION enforce_customer_return_lines();
                DROP TRIGGER customer_return_header ON customer_returns;
                DROP FUNCTION enforce_customer_return_header();
                """);
            migrationBuilder.DropTable(
                name: "customer_return_lines");

            migrationBuilder.DropTable(
                name: "customer_returns");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER', 'EXPENSE')");
        }
    }
}
