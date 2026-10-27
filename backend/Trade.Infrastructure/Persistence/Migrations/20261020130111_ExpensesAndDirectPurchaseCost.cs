using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpensesAndDirectPurchaseCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.CreateTable(
                name: "expense_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_categories", x => x.Id);
                    table.CheckConstraint("ck_expense_category_name", "btrim(\"Name\") <> ''");
                    table.CheckConstraint("ck_expense_category_parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
                    table.ForeignKey(
                        name: "FK_expense_categories_expense_categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "expense_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses", x => x.DocumentId);
                    table.CheckConstraint("ck_expense_amount", "\"Amount\" > 0");
                    table.CheckConstraint("ck_expense_exchange_rate", "\"ExchangeRate\" > 0");
                    table.ForeignKey(
                        name: "FK_expenses_counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalTable: "counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expenses_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expenses_expense_categories_ExpenseCategoryId",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "expense_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expenses_purchase_invoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "purchase_invoices",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER', 'EXPENSE')");

            migrationBuilder.CreateIndex(
                name: "IX_expense_categories_OrganizationId_Name",
                table: "expense_categories",
                columns: new[] { "OrganizationId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_expense_categories_ParentId",
                table: "expense_categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_CounterpartyId",
                table: "expenses",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_ExpenseCategoryId",
                table: "expenses",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_PurchaseInvoiceId",
                table: "expenses",
                column: "PurchaseInvoiceId");

            migrationBuilder.Sql("""
                CREATE FUNCTION enforce_expense_category_tree() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended(NEW."OrganizationId"::text, 72831));
                    IF NEW."ParentId" IS NOT NULL THEN
                        IF NOT EXISTS (SELECT 1 FROM expense_categories
                            WHERE "Id" = NEW."ParentId" AND "OrganizationId" = NEW."OrganizationId") THEN
                            RAISE EXCEPTION 'Parent category must belong to the same organization' USING ERRCODE = '23514';
                        END IF;
                        IF EXISTS (
                            WITH RECURSIVE ancestors AS (
                                SELECT "Id", "ParentId" FROM expense_categories WHERE "Id" = NEW."ParentId"
                                UNION
                                SELECT parent."Id", parent."ParentId" FROM expense_categories parent
                                JOIN ancestors child ON parent."Id" = child."ParentId"
                            ) SELECT 1 FROM ancestors WHERE "Id" = NEW."Id"
                        ) THEN
                            RAISE EXCEPTION 'ExpenseCategory tree cannot contain cycles' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER expense_category_tree BEFORE INSERT OR UPDATE ON expense_categories
                FOR EACH ROW EXECUTE FUNCTION enforce_expense_category_tree();

                CREATE FUNCTION enforce_expense_references() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE expense_org uuid; purchase_org uuid; purchase_type text; purchase_status text;
                    purchase_deleted timestamptz; purchase_discount numeric;
                BEGIN
                    SELECT "OrganizationId" INTO expense_org FROM documents
                        WHERE "Id" = NEW."DocumentId" AND "DocumentType" = 'EXPENSE';
                    IF expense_org IS NULL OR NOT EXISTS (SELECT 1 FROM counterparties
                        WHERE "Id" = NEW."CounterpartyId" AND "OrganizationId" = expense_org)
                        OR NOT EXISTS (SELECT 1 FROM expense_categories
                        WHERE "Id" = NEW."ExpenseCategoryId" AND "OrganizationId" = expense_org) THEN
                        RAISE EXCEPTION 'Expense references must belong to its organization' USING ERRCODE = '23514';
                    END IF;
                    IF NEW."PurchaseInvoiceId" IS NOT NULL THEN
                        SELECT "OrganizationId", "DocumentType", "Status", "DeletedAt"
                            INTO purchase_org, purchase_type, purchase_status, purchase_deleted
                            FROM documents WHERE "Id" = NEW."PurchaseInvoiceId" FOR SHARE;
                        IF purchase_org IS DISTINCT FROM expense_org OR purchase_type <> 'PURCHASE_INVOICE'
                            OR purchase_status <> 'POSTED' OR purchase_deleted IS NOT NULL THEN
                            RAISE EXCEPTION 'A direct expense requires a POSTED purchase in its organization' USING ERRCODE = '23514';
                        END IF;
                        SELECT "DiscountPercent" INTO purchase_discount FROM purchase_invoices
                            WHERE "DocumentId" = NEW."PurchaseInvoiceId";
                        IF purchase_discount IS NULL OR purchase_discount = 100 OR NOT EXISTS (
                            SELECT 1 FROM purchase_invoice_lines WHERE "PurchaseInvoiceId" = NEW."PurchaseInvoiceId"
                                AND "RemovedAt" IS NULL AND "UnitPriceCurrency" > 0
                        ) THEN
                            RAISE EXCEPTION 'INVALID_PURCHASE_EXPENSE_ALLOCATION' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER expense_references BEFORE INSERT OR UPDATE ON expenses
                FOR EACH ROW EXECUTE FUNCTION enforce_expense_references();

                CREATE FUNCTION protect_purchase_expense_dependencies() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."DocumentType" = 'PURCHASE_INVOICE'
                        AND (NEW."Status" <> 'POSTED' OR NEW."DeletedAt" IS NOT NULL)
                        AND EXISTS (SELECT 1 FROM expenses expense JOIN documents document ON document."Id" = expense."DocumentId"
                            WHERE expense."PurchaseInvoiceId" = OLD."Id" AND document."DeletedAt" IS NULL) THEN
                        RAISE EXCEPTION 'DOCUMENT_HAS_DEPENDENCIES' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER purchase_expense_dependencies BEFORE UPDATE OF "Status", "DeletedAt" ON documents
                FOR EACH ROW EXECUTE FUNCTION protect_purchase_expense_dependencies();

                CREATE OR REPLACE VIEW v_batch_cost AS
                WITH line_costs AS (
                    SELECT line."Id", line."PurchaseInvoiceId", line."ProductId", line."Quantity", line."RemovedAt",
                        line."UnitPriceCurrency" * (1 - purchase."DiscountPercent" / 100::numeric)
                            * purchase."ExchangeRate" AS unit_base
                    FROM purchase_invoice_lines line
                    JOIN purchase_invoices purchase ON purchase."DocumentId" = line."PurchaseInvoiceId"
                ), purchase_totals AS (
                    SELECT "PurchaseInvoiceId", SUM("Quantity" * unit_base) AS total_base
                    FROM line_costs WHERE "RemovedAt" IS NULL GROUP BY "PurchaseInvoiceId"
                ), direct_expenses AS (
                    SELECT expense."PurchaseInvoiceId", SUM(expense."Amount" * expense."ExchangeRate") AS total_uah
                    FROM expenses expense JOIN documents document ON document."Id" = expense."DocumentId"
                    WHERE expense."PurchaseInvoiceId" IS NOT NULL AND document."Status" = 'POSTED' AND document."DeletedAt" IS NULL
                    GROUP BY expense."PurchaseInvoiceId"
                )
                SELECT batch."Id" AS "BatchId", line."Id" AS "PurchaseInvoiceLineId", line."ProductId",
                    -- (quantity * unit_base / total_base * expenses) / quantity = unit_base * expenses / total_base.
                    -- Keep the base unit cost exact; no allocation rows, snapshots or per-line rounding.
                    trim_scale(line.unit_base + CASE WHEN line."RemovedAt" IS NULL AND totals.total_base > 0
                        THEN COALESCE(expense.total_uah, 0) * line.unit_base / totals.total_base ELSE 0 END) AS "UnitCostUah"
                FROM batches batch JOIN line_costs line ON line."Id" = batch."PurchaseInvoiceLineId"
                LEFT JOIN purchase_totals totals ON totals."PurchaseInvoiceId" = line."PurchaseInvoiceId"
                LEFT JOIN direct_expenses expense ON expense."PurchaseInvoiceId" = line."PurchaseInvoiceId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW v_batch_cost AS
                SELECT batch."Id" AS "BatchId", line."Id" AS "PurchaseInvoiceLineId", line."ProductId",
                    trim_scale(line."UnitPriceCurrency" * (1 - purchase."DiscountPercent" / 100::numeric)
                        * purchase."ExchangeRate") AS "UnitCostUah"
                FROM batches batch
                JOIN purchase_invoice_lines line ON line."Id" = batch."PurchaseInvoiceLineId"
                JOIN purchase_invoices purchase ON purchase."DocumentId" = line."PurchaseInvoiceId";
                DROP TRIGGER purchase_expense_dependencies ON documents;
                DROP FUNCTION protect_purchase_expense_dependencies();
                DROP TRIGGER expense_references ON expenses;
                DROP FUNCTION enforce_expense_references();
                DROP TRIGGER expense_category_tree ON expense_categories;
                DROP FUNCTION enforce_expense_category_tree();
                """);
            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "expense_categories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER')");
        }
    }
}
