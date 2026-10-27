using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                    table.CheckConstraint("ck_role_name", "btrim(\"Name\") <> ''");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                    table.CheckConstraint("ck_user_username", "btrim(\"UserName\") <> ''");
                    table.ForeignKey(
                        name: "FK_users_counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalTable: "counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_role_permissions_permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_roles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "Id", "Code", "Description" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "product.view", "View products and selling prices" },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "product.create", "Create products" },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "product.edit", "Edit products" },
                    { new Guid("10000000-0000-0000-0000-000000000004"), "purchase.view", "View purchases and supplier returns" },
                    { new Guid("10000000-0000-0000-0000-000000000005"), "purchase.create", "Create/edit purchases and supplier returns" },
                    { new Guid("10000000-0000-0000-0000-000000000006"), "purchase.post", "Post purchases and supplier returns" },
                    { new Guid("10000000-0000-0000-0000-000000000007"), "purchase.unpost", "Unpost purchases and supplier returns" },
                    { new Guid("10000000-0000-0000-0000-000000000008"), "purchase.delete", "Delete draft purchases/returns" },
                    { new Guid("10000000-0000-0000-0000-000000000009"), "sale.view", "View sales and customer returns" },
                    { new Guid("10000000-0000-0000-0000-000000000010"), "sale.create", "Create/edit sales and customer returns" },
                    { new Guid("10000000-0000-0000-0000-000000000011"), "sale.post", "Post sales and customer returns" },
                    { new Guid("10000000-0000-0000-0000-000000000012"), "sale.unpost", "Unpost sales and customer returns" },
                    { new Guid("10000000-0000-0000-0000-000000000013"), "sale.delete", "Delete draft sales/returns" },
                    { new Guid("10000000-0000-0000-0000-000000000014"), "sale.change_price", "Sell below the recommended price" },
                    { new Guid("10000000-0000-0000-0000-000000000015"), "stock.view", "View stock and available batches" },
                    { new Guid("10000000-0000-0000-0000-000000000016"), "stock.transfer", "Manage stock transfers" },
                    { new Guid("10000000-0000-0000-0000-000000000017"), "stock.writeoff", "Manage write-offs and reasons" },
                    { new Guid("10000000-0000-0000-0000-000000000018"), "cost.view", "View calculated costs" },
                    { new Guid("10000000-0000-0000-0000-000000000019"), "money.view", "View money accounts/documents/balances" },
                    { new Guid("10000000-0000-0000-0000-000000000020"), "money.receipt", "Manage money receipts" },
                    { new Guid("10000000-0000-0000-0000-000000000021"), "money.payment", "Manage money payments" },
                    { new Guid("10000000-0000-0000-0000-000000000022"), "money.transfer", "Manage money transfers" },
                    { new Guid("10000000-0000-0000-0000-000000000023"), "expense.view", "View expenses/categories" },
                    { new Guid("10000000-0000-0000-0000-000000000024"), "expense.create", "Create/edit expenses/categories" },
                    { new Guid("10000000-0000-0000-0000-000000000025"), "expense.post", "Post expenses" },
                    { new Guid("10000000-0000-0000-0000-000000000026"), "expense.unpost", "Unpost expenses" },
                    { new Guid("10000000-0000-0000-0000-000000000027"), "expense.delete", "Delete draft expenses" },
                    { new Guid("10000000-0000-0000-0000-000000000028"), "settlement.view", "View counterparties and settlements" },
                    { new Guid("10000000-0000-0000-0000-000000000029"), "reports.view", "View reports" },
                    { new Guid("10000000-0000-0000-0000-000000000030"), "profit.view", "View profit" },
                    { new Guid("10000000-0000-0000-0000-000000000031"), "users.manage", "Manage users" },
                    { new Guid("10000000-0000-0000-0000-000000000032"), "roles.manage", "Manage roles and their permissions" },
                    { new Guid("10000000-0000-0000-0000-000000000033"), "settings.manage", "Manage counterparties and pricing settings" },
                    { new Guid("10000000-0000-0000-0000-000000000034"), "audit.view", "View audit data" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_permissions_Code",
                table: "permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_PermissionId",
                table: "role_permissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_roles_OrganizationId_NormalizedName",
                table: "roles",
                columns: new[] { "OrganizationId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_RoleId",
                table: "user_roles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_users_CounterpartyId",
                table: "users",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedUserName",
                table: "users",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_OrganizationId",
                table: "users",
                column: "OrganizationId");
            migrationBuilder.Sql("""
                CREATE FUNCTION enforce_user_identity_scope() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Users cannot be physically deleted' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'UPDATE' AND NEW."OrganizationId" <> OLD."OrganizationId" THEN
                        RAISE EXCEPTION 'User organization is immutable' USING ERRCODE = '23514';
                    END IF;
                    IF NEW."OrganizationId" = '00000000-0000-0000-0000-000000000000'::uuid OR
                        (NEW."CounterpartyId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM counterparties
                            WHERE "Id" = NEW."CounterpartyId" AND "OrganizationId" = NEW."OrganizationId")) THEN
                        RAISE EXCEPTION 'User counterparty must belong to its organization' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER user_identity_scope BEFORE INSERT OR UPDATE OR DELETE ON users
                    FOR EACH ROW EXECUTE FUNCTION enforce_user_identity_scope();
                CREATE FUNCTION enforce_role_identity_scope() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."OrganizationId" = '00000000-0000-0000-0000-000000000000'::uuid OR
                        (TG_OP = 'UPDATE' AND NEW."OrganizationId" <> OLD."OrganizationId") THEN
                        RAISE EXCEPTION 'Role organization is required and immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER role_identity_scope BEFORE INSERT OR UPDATE ON roles
                    FOR EACH ROW EXECUTE FUNCTION enforce_role_identity_scope();
                CREATE FUNCTION enforce_user_role_scope() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM users u JOIN roles r ON r."OrganizationId" = u."OrganizationId"
                        WHERE u."Id" = NEW."UserId" AND r."Id" = NEW."RoleId") THEN
                        RAISE EXCEPTION 'User and role must belong to the same organization' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER user_role_scope BEFORE INSERT OR UPDATE ON user_roles
                    FOR EACH ROW EXECUTE FUNCTION enforce_user_role_scope();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER user_role_scope ON user_roles;
                DROP FUNCTION enforce_user_role_scope();
                DROP TRIGGER role_identity_scope ON roles;
                DROP FUNCTION enforce_role_identity_scope();
                DROP TRIGGER user_identity_scope ON users;
                DROP FUNCTION enforce_user_identity_scope();
                """);
            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
