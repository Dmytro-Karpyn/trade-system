using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DirectoriesAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "units",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "storage_locations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    OldValues = table.Column<string>(type: "jsonb", nullable: true),
                    NewValues = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_categories", x => x.Id);
                    table.CheckConstraint("ck_product_category_name", "btrim(\"Name\") <> ''");
                    table.ForeignKey(
                        name: "FK_product_categories_product_categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "product_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_OrganizationId_ActorId_OccurredAt",
                table: "audit_logs",
                columns: new[] { "OrganizationId", "ActorId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_OrganizationId_DocumentId",
                table: "audit_logs",
                columns: new[] { "OrganizationId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_OrganizationId_OccurredAt_Id",
                table: "audit_logs",
                columns: new[] { "OrganizationId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_OrganizationId_ParentId_Name",
                table: "product_categories",
                columns: new[] { "OrganizationId", "ParentId", "Name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_ParentId",
                table: "product_categories",
                column: "ParentId");
            migrationBuilder.Sql("""
                CREATE FUNCTION trade_audit_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Audit entries are immutable' USING ERRCODE = '23514'; END;
                $$;
                CREATE TRIGGER audit_immutable BEFORE UPDATE OR DELETE ON audit_logs
                FOR EACH ROW EXECUTE FUNCTION trade_audit_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_immutable ON audit_logs; DROP FUNCTION IF EXISTS trade_audit_immutable();");
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "product_categories");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "units");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "storage_locations");
        }
    }
}
