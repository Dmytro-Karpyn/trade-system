using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CounterpartiesAndSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "counterparties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LegalType = table.Column<string>(type: "text", nullable: true),
                    TaxId = table.Column<string>(type: "text", nullable: true),
                    RegistrationCode = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    DefaultPriceTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsCustomer = table.Column<bool>(type: "boolean", nullable: false),
                    IsSupplier = table.Column<bool>(type: "boolean", nullable: false),
                    IsEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counterparties", x => x.Id);
                    table.CheckConstraint("ck_counterparty_legal_type", "\"LegalType\" IS NULL OR \"LegalType\" IN ('PERSON', 'ENTREPRENEUR', 'COMPANY')");
                    table.CheckConstraint("ck_counterparty_name", "btrim(\"Name\") <> ''");
                    table.ForeignKey(
                        name: "FK_counterparties_price_types_DefaultPriceTypeId",
                        column: x => x.DefaultPriceTypeId,
                        principalTable: "price_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlement_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    BaseAmountUah = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_movements", x => x.Id);
                    table.CheckConstraint("ck_settlement_signs", "(\"Amount\" >= 0 AND \"BaseAmountUah\" >= 0) OR (\"Amount\" <= 0 AND \"BaseAmountUah\" <= 0)");
                    table.ForeignKey(
                        name: "FK_settlement_movements_counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalTable: "counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_settlement_movements_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_invoices_CounterpartyId",
                table: "sale_invoices",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_invoices_CounterpartyId",
                table: "purchase_invoices",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_counterparties_DefaultPriceTypeId",
                table: "counterparties",
                column: "DefaultPriceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_counterparties_OrganizationId_Name",
                table: "counterparties",
                columns: new[] { "OrganizationId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_settlement_movements_CounterpartyId_CurrencyId",
                table: "settlement_movements",
                columns: new[] { "CounterpartyId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_settlement_movements_DocumentId",
                table: "settlement_movements",
                column: "DocumentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_invoices_counterparties_CounterpartyId",
                table: "purchase_invoices",
                column: "CounterpartyId",
                principalTable: "counterparties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_invoices_counterparties_CounterpartyId",
                table: "sale_invoices",
                column: "CounterpartyId",
                principalTable: "counterparties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE VIEW v_counterparty_balance AS
                SELECT "CounterpartyId", "CurrencyId", SUM("Amount") AS "Balance"
                FROM settlement_movements GROUP BY "CounterpartyId", "CurrencyId";

                CREATE FUNCTION prevent_system_counterparty_delete() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."IsSystem" THEN
                        RAISE EXCEPTION 'A system Counterparty cannot be physically deleted'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_system_counterparty_delete';
                    END IF;
                    RETURN OLD;
                END;
                $$;
                CREATE TRIGGER system_counterparty_delete BEFORE DELETE ON counterparties
                FOR EACH ROW EXECUTE FUNCTION prevent_system_counterparty_delete();

                CREATE FUNCTION enforce_counterparty_price_type_organization() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."DefaultPriceTypeId" IS NOT NULL AND EXISTS (
                        SELECT 1 FROM price_types WHERE "Id" = NEW."DefaultPriceTypeId"
                            AND "OrganizationId" <> NEW."OrganizationId") THEN
                        RAISE EXCEPTION 'Counterparty and PriceType organizations must match' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER counterparty_price_type_organization BEFORE INSERT OR UPDATE ON counterparties
                FOR EACH ROW EXECUTE FUNCTION enforce_counterparty_price_type_organization();

                CREATE FUNCTION enforce_document_counterparty_organization() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."CounterpartyId" IS NOT NULL AND EXISTS (
                        SELECT 1 FROM counterparties c JOIN documents d ON d."Id" = NEW."DocumentId"
                        WHERE c."Id" = NEW."CounterpartyId" AND c."OrganizationId" <> d."OrganizationId") THEN
                        RAISE EXCEPTION 'Document and Counterparty organizations must match' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER purchase_counterparty_organization BEFORE INSERT OR UPDATE ON purchase_invoices
                FOR EACH ROW EXECUTE FUNCTION enforce_document_counterparty_organization();
                CREATE TRIGGER sale_counterparty_organization BEFORE INSERT OR UPDATE ON sale_invoices
                FOR EACH ROW EXECUTE FUNCTION enforce_document_counterparty_organization();

                CREATE FUNCTION enforce_settlement_organization() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM counterparties c JOIN documents d ON d."Id" = NEW."DocumentId"
                        WHERE c."Id" = NEW."CounterpartyId" AND c."OrganizationId" <> d."OrganizationId") THEN
                        RAISE EXCEPTION 'Settlement Document and Counterparty organizations must match' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER settlement_organization BEFORE INSERT OR UPDATE ON settlement_movements
                FOR EACH ROW EXECUTE FUNCTION enforce_settlement_organization();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP VIEW v_counterparty_balance;
                DROP TRIGGER settlement_organization ON settlement_movements;
                DROP FUNCTION enforce_settlement_organization();
                DROP TRIGGER purchase_counterparty_organization ON purchase_invoices;
                DROP TRIGGER sale_counterparty_organization ON sale_invoices;
                DROP FUNCTION enforce_document_counterparty_organization();
                DROP TRIGGER counterparty_price_type_organization ON counterparties;
                DROP FUNCTION enforce_counterparty_price_type_organization();
                DROP TRIGGER system_counterparty_delete ON counterparties;
                DROP FUNCTION prevent_system_counterparty_delete();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_purchase_invoices_counterparties_CounterpartyId",
                table: "purchase_invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_invoices_counterparties_CounterpartyId",
                table: "sale_invoices");

            migrationBuilder.DropTable(
                name: "settlement_movements");

            migrationBuilder.DropTable(
                name: "counterparties");

            migrationBuilder.DropIndex(
                name: "IX_sale_invoices_CounterpartyId",
                table: "sale_invoices");

            migrationBuilder.DropIndex(
                name: "IX_purchase_invoices_CounterpartyId",
                table: "purchase_invoices");
        }
    }
}
