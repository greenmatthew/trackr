using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeFoodItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Yield",
                table: "FoodItems",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FoodItemComponents",
                columns: table => new
                {
                    ParentFoodItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildFoodItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodItemComponents", x => new { x.ParentFoodItemId, x.ChildFoodItemId });
                    table.CheckConstraint("CK_FoodItemComponents_NotSelf", "\"ParentFoodItemId\" <> \"ChildFoodItemId\"");
                    table.ForeignKey(
                        name: "FK_FoodItemComponents_FoodItems_ChildFoodItemId",
                        column: x => x.ChildFoodItemId,
                        principalTable: "FoodItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FoodItemComponents_FoodItems_ParentFoodItemId",
                        column: x => x.ParentFoodItemId,
                        principalTable: "FoodItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_FoodItems_YieldPositive",
                table: "FoodItems",
                sql: "\"Yield\" IS NULL OR \"Yield\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_FoodItemComponents_ChildFoodItemId",
                table: "FoodItemComponents",
                column: "ChildFoodItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FoodItemComponents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FoodItems_YieldPositive",
                table: "FoodItems");

            migrationBuilder.DropColumn(
                name: "Yield",
                table: "FoodItems");
        }
    }
}
