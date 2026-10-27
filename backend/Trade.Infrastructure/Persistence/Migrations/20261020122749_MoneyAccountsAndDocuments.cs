using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoneyAccountsAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.CreateTable(
                name: "money_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AccountType = table.Column<string>(type: "text", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_money_accounts", x => x.Id);
                    table.CheckConstraint("ck_money_account_name", "btrim(\"Name\") <> ''");
                    table.CheckConstraint("ck_money_account_type", "\"AccountType\" IN ('CASH', 'BANK', 'SAFE', 'OTHER')");
                });

            migrationBuilder.CreateTable(
                name: "money_documents",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: true),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    DestinationAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    SettlementCurrencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    SettlementAmount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    SettlementExchangeRate = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_money_documents", x => x.DocumentId);
                    table.CheckConstraint("ck_money_amounts", "(\"SourceAmount\" IS NULL OR \"SourceAmount\" > 0) AND (\"DestinationAmount\" IS NULL OR \"DestinationAmount\" > 0) AND (\"SettlementAmount\" IS NULL OR \"SettlementAmount\" > 0)");
                    table.CheckConstraint("ck_money_purpose", "\"Purpose\" IS NULL OR \"Purpose\" IN ('SETTLEMENT', 'OWNER', 'OPENING_BALANCE')");
                    table.CheckConstraint("ck_money_settlement_fields", "(COALESCE(\"Purpose\" = 'SETTLEMENT', false) AND \"CounterpartyId\" IS NOT NULL AND \"SettlementCurrencyId\" IS NOT NULL AND \"SettlementAmount\" IS NOT NULL AND \"SettlementExchangeRate\" IS NOT NULL AND \"SettlementExchangeRate\" > 0 AND (\"SettlementCurrencyId\" <> '00000000-0000-0000-0000-000000000980'::uuid OR \"SettlementExchangeRate\" = 1)) OR (NOT COALESCE(\"Purpose\" = 'SETTLEMENT', false) AND \"SettlementCurrencyId\" IS NULL AND \"SettlementAmount\" IS NULL AND \"SettlementExchangeRate\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_money_documents_counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalTable: "counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_money_documents_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_money_documents_money_accounts_DestinationAccountId",
                        column: x => x.DestinationAccountId,
                        principalTable: "money_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_money_documents_money_accounts_SourceAccountId",
                        column: x => x.SourceAccountId,
                        principalTable: "money_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "money_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MoneyAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_money_movements", x => x.Id);
                    table.CheckConstraint("ck_money_movement_amount", "\"Amount\" <> 0");
                    table.ForeignKey(
                        name: "FK_money_movements_money_accounts_MoneyAccountId",
                        column: x => x.MoneyAccountId,
                        principalTable: "money_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_money_movements_money_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "money_documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER')");

            migrationBuilder.CreateIndex(
                name: "IX_money_accounts_OrganizationId_Name",
                table: "money_accounts",
                columns: new[] { "OrganizationId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_money_documents_CounterpartyId",
                table: "money_documents",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_money_documents_DestinationAccountId",
                table: "money_documents",
                column: "DestinationAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_money_documents_SourceAccountId",
                table: "money_documents",
                column: "SourceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_money_movements_DocumentId_MoneyAccountId",
                table: "money_movements",
                columns: new[] { "DocumentId", "MoneyAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_money_movements_MoneyAccountId",
                table: "money_movements",
                column: "MoneyAccountId");

            migrationBuilder.Sql("""
                CREATE VIEW v_money_account_balance AS
                SELECT m."MoneyAccountId", a."CurrencyId", SUM(m."Amount") AS "Balance"
                FROM money_movements m JOIN money_accounts a ON a."Id" = m."MoneyAccountId"
                GROUP BY m."MoneyAccountId", a."CurrencyId";

                CREATE FUNCTION enforce_money_document_references() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE doc_org uuid; doc_type text; source_currency uuid; destination_currency uuid;
                BEGIN
                    SELECT "OrganizationId", "DocumentType" INTO doc_org, doc_type FROM documents WHERE "Id" = NEW."DocumentId";
                    IF doc_type IS NULL OR doc_type NOT IN ('MONEY_RECEIPT','MONEY_PAYMENT','MONEY_TRANSFER') THEN
                        RAISE EXCEPTION 'MoneyDocument requires a money DocumentType' USING ERRCODE='23514';
                    END IF;
                    IF EXISTS (SELECT 1 FROM money_accounts WHERE "Id" IN (NEW."SourceAccountId", NEW."DestinationAccountId") AND "OrganizationId" <> doc_org)
                        OR EXISTS (SELECT 1 FROM counterparties WHERE "Id" = NEW."CounterpartyId" AND "OrganizationId" <> doc_org) THEN
                        RAISE EXCEPTION 'Money document references must belong to its organization' USING ERRCODE='23514';
                    END IF;
                    IF doc_type = 'MONEY_RECEIPT' THEN
                        IF NEW."SourceAccountId" IS NOT NULL OR NEW."SourceAmount" IS NOT NULL
                            OR NEW."DestinationAccountId" IS NULL OR NEW."DestinationAmount" IS NULL THEN
                            RAISE EXCEPTION 'Receipt requires only destination account and amount' USING ERRCODE='23514';
                        END IF;
                    ELSIF doc_type = 'MONEY_PAYMENT' THEN
                        IF NEW."DestinationAccountId" IS NOT NULL OR NEW."DestinationAmount" IS NOT NULL
                            OR NEW."SourceAccountId" IS NULL OR NEW."SourceAmount" IS NULL THEN
                            RAISE EXCEPTION 'Payment requires only source account and amount' USING ERRCODE='23514';
                        END IF;
                    ELSE
                        IF NEW."SourceAccountId" IS NULL OR NEW."DestinationAccountId" IS NULL
                            OR NEW."SourceAmount" IS NULL OR NEW."DestinationAmount" IS NULL
                            OR NEW."SourceAccountId" = NEW."DestinationAccountId" OR NEW."Purpose" = 'SETTLEMENT' THEN
                            RAISE EXCEPTION 'Transfer requires two distinct accounts and amounts without settlement' USING ERRCODE='23514';
                        END IF;
                        SELECT "CurrencyId" INTO source_currency FROM money_accounts WHERE "Id" = NEW."SourceAccountId";
                        SELECT "CurrencyId" INTO destination_currency FROM money_accounts WHERE "Id" = NEW."DestinationAccountId";
                        IF source_currency = destination_currency AND NEW."SourceAmount" <> NEW."DestinationAmount" THEN
                            RAISE EXCEPTION 'Same-currency transfer requires equal amounts' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER money_document_references BEFORE INSERT OR UPDATE ON money_documents
                FOR EACH ROW EXECUTE FUNCTION enforce_money_document_references();

                CREATE FUNCTION protect_used_money_account_currency() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."CurrencyId" IS DISTINCT FROM OLD."CurrencyId" AND
                        (EXISTS (SELECT 1 FROM money_documents WHERE "SourceAccountId" = OLD."Id" OR "DestinationAccountId" = OLD."Id")
                         OR EXISTS (SELECT 1 FROM money_movements WHERE "MoneyAccountId" = OLD."Id")) THEN
                        RAISE EXCEPTION 'Used MoneyAccount currency cannot change' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER used_money_account_currency BEFORE UPDATE OF "CurrencyId" ON money_accounts
                FOR EACH ROW EXECUTE FUNCTION protect_used_money_account_currency();

                CREATE FUNCTION enforce_money_movement_reference() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE money money_documents%ROWTYPE; doc_org uuid; account_org uuid;
                BEGIN
                    SELECT * INTO money FROM money_documents WHERE "DocumentId" = NEW."DocumentId";
                    SELECT "OrganizationId" INTO doc_org FROM documents WHERE "Id" = NEW."DocumentId";
                    SELECT "OrganizationId" INTO account_org FROM money_accounts WHERE "Id" = NEW."MoneyAccountId";
                    IF doc_org <> account_org OR NOT COALESCE(
                        (NEW."MoneyAccountId" = money."SourceAccountId" AND NEW."Amount" = -money."SourceAmount")
                        OR (NEW."MoneyAccountId" = money."DestinationAccountId" AND NEW."Amount" = money."DestinationAmount"), false) THEN
                        RAISE EXCEPTION 'MoneyMovement must match its document account, amount and organization' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER money_movement_reference BEFORE INSERT OR UPDATE ON money_movements
                FOR EACH ROW EXECUTE FUNCTION enforce_money_movement_reference();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP VIEW v_money_account_balance;
                DROP TRIGGER money_movement_reference ON money_movements;
                DROP FUNCTION enforce_money_movement_reference();
                DROP TRIGGER used_money_account_currency ON money_accounts;
                DROP FUNCTION protect_used_money_account_currency();
                DROP TRIGGER money_document_references ON money_documents;
                DROP FUNCTION enforce_money_document_references();
                """);
            migrationBuilder.DropTable(
                name: "money_movements");

            migrationBuilder.DropTable(
                name: "money_documents");

            migrationBuilder.DropTable(
                name: "money_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE')");
        }
    }
}
