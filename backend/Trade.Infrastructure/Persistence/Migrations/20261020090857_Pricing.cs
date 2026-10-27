using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trade.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "price_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_types", x => x.Id);
                    table.CheckConstraint("ck_price_type_name", "btrim(\"Name\") <> ''");
                });

            migrationBuilder.CreateTable(
                name: "batch_prices",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_prices", x => new { x.BatchId, x.PriceTypeId });
                    table.CheckConstraint("ck_batch_price", "\"UnitPrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_batch_prices_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_batch_prices_price_types_PriceTypeId",
                        column: x => x.PriceTypeId,
                        principalTable: "price_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_prices",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_prices", x => new { x.ProductId, x.PriceTypeId });
                    table.CheckConstraint("ck_product_price", "\"UnitPrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_product_prices_price_types_PriceTypeId",
                        column: x => x.PriceTypeId,
                        principalTable: "price_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_prices_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_invoices_PriceTypeId",
                table: "sale_invoices",
                column: "PriceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_batch_prices_PriceTypeId",
                table: "batch_prices",
                column: "PriceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_price_types_OrganizationId",
                table: "price_types",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_product_prices_PriceTypeId",
                table: "product_prices",
                column: "PriceTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_sale_invoices_price_types_PriceTypeId",
                table: "sale_invoices",
                column: "PriceTypeId",
                principalTable: "price_types",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sale_invoices_price_types_PriceTypeId",
                table: "sale_invoices");

            migrationBuilder.DropTable(
                name: "batch_prices");

            migrationBuilder.DropTable(
                name: "product_prices");

            migrationBuilder.DropTable(
                name: "price_types");

            migrationBuilder.DropIndex(
                name: "IX_sale_invoices_PriceTypeId",
                table: "sale_invoices");
        }
    }
}
