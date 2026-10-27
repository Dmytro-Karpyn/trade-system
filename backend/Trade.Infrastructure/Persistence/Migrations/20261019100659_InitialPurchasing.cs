using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPurchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "document_numbers");

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: true),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    InternalNote = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PostedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                    table.CheckConstraint("ck_documents_posted_audit", "(\"Status\" = 'DRAFT' AND \"PostedAt\" IS NULL AND \"PostedBy\" IS NULL) OR (\"Status\" = 'POSTED' AND \"Number\" IS NOT NULL AND \"PostedAt\" IS NOT NULL AND \"PostedBy\" IS NOT NULL)");
                    table.CheckConstraint("ck_documents_status", "\"Status\" IN ('DRAFT', 'POSTED')");
                    table.CheckConstraint("ck_documents_type", "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK')");
                    table.CheckConstraint("ck_documents_version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemType = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Barcode = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Attributes = table.Column<string>(type: "jsonb", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                    table.CheckConstraint("ck_products_item_type", "\"ItemType\" IN ('GOODS', 'SERVICE')");
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoices",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    StorageLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    ExternalNumber = table.Column<string>(type: "text", nullable: true),
                    ExternalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_invoices", x => x.DocumentId);
                    table.CheckConstraint("ck_purchase_discount", "\"DiscountPercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_purchase_exchange_rate", "\"ExchangeRate\" > 0");
                    table.ForeignKey(
                        name: "FK_purchase_invoices_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoice_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    UnitPriceCurrency = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_invoice_lines", x => x.Id);
                    table.CheckConstraint("ck_purchase_line_no", "\"LineNo\" > 0");
                    table.CheckConstraint("ck_purchase_line_price", "\"UnitPriceCurrency\" >= 0");
                    table.CheckConstraint("ck_purchase_line_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_purchase_invoice_lines_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_invoice_lines_purchase_invoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "purchase_invoices",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseInvoiceLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_batches_purchase_invoice_lines_PurchaseInvoiceLineId",
                        column: x => x.PurchaseInvoiceLineId,
                        principalTable: "purchase_invoice_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_movements_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_batches_PurchaseInvoiceLineId",
                table: "batches",
                column: "PurchaseInvoiceLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documents_OrganizationId_DocumentType_Number",
                table: "documents",
                columns: new[] { "OrganizationId", "DocumentType", "Number" },
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_products_OrganizationId_Code",
                table: "products",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_invoice_lines_ProductId",
                table: "purchase_invoice_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_invoice_lines_PurchaseInvoiceId",
                table: "purchase_invoice_lines",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_BatchId",
                table: "stock_movements",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_DocumentId_BatchId_StorageLocationId",
                table: "stock_movements",
                columns: new[] { "DocumentId", "BatchId", "StorageLocationId" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE VIEW v_stock_balance AS
                SELECT "BatchId", "StorageLocationId", SUM("Quantity") AS "PhysicalQuantity"
                FROM stock_movements
                GROUP BY "BatchId", "StorageLocationId";

                CREATE FUNCTION enforce_batched_line_product() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."ProductId" IS DISTINCT FROM OLD."ProductId"
                       AND EXISTS (SELECT 1 FROM batches WHERE "PurchaseInvoiceLineId" = OLD."Id") THEN
                        RAISE EXCEPTION 'ProductId cannot change after Batch creation'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_batched_line_product_immutable';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER batched_line_product_immutable
                BEFORE UPDATE OF "ProductId" ON purchase_invoice_lines
                FOR EACH ROW EXECUTE FUNCTION enforce_batched_line_product();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP VIEW v_stock_balance;
                DROP TRIGGER batched_line_product_immutable ON purchase_invoice_lines;
                DROP FUNCTION enforce_batched_line_product();
                """);

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "batches");

            migrationBuilder.DropTable(
                name: "purchase_invoice_lines");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "purchase_invoices");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropSequence(
                name: "document_numbers");
        }
    }
}
