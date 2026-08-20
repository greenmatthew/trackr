using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientsAndAllergens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValueSql, which EF does not generate for an array column: the column is NOT
            // NULL and every existing row needs a value, so without it this migration fails on any
            // database that already has a catalog in it.
            migrationBuilder.AddColumn<List<string>>(
                name: "Allergens",
                table: "FoodItems",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            // defaultValueSql, which EF does not generate for an array column: the column is NOT
            // NULL and every existing row needs a value, so without it this migration fails on any
            // database that already has a catalog in it.
            migrationBuilder.AddColumn<List<string>>(
                name: "DietFlags",
                table: "FoodItems",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "IngredientsText",
                table: "FoodItems",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FoodItems_Allergens",
                table: "FoodItems",
                column: "Allergens")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_FoodItems_DietFlags",
                table: "FoodItems",
                column: "DietFlags")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FoodItems_Allergens",
                table: "FoodItems");

            migrationBuilder.DropIndex(
                name: "IX_FoodItems_DietFlags",
                table: "FoodItems");

            migrationBuilder.DropColumn(
                name: "Allergens",
                table: "FoodItems");

            migrationBuilder.DropColumn(
                name: "DietFlags",
                table: "FoodItems");

            migrationBuilder.DropColumn(
                name: "IngredientsText",
                table: "FoodItems");
        }
    }
}
