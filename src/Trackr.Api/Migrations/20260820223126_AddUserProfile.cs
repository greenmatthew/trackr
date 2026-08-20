using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackr.Api.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// The four enum defaults are hand-edited from the empty string the scaffolder emits, and that
    /// edit is the whole risk in this migration. These columns are text-converted enums on a table
    /// that already has rows: an empty string backfilled into them parses as no member at all, so
    /// every existing account would fail to load - on a server that has been in use, and only
    /// there.
    /// </remarks>
    public partial class AddUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActivityLevel",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Sedentary");

            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "AspNetUsers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnergyUnit",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Kilocalories");

            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                table: "AspNetUsers",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeasurementSystem",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Metric");

            migrationBuilder.AddColumn<string>(
                name: "Sex",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unspecified");

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "AspNetUsers",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivityLevel",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EnergyUnit",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MeasurementSystem",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Sex",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "AspNetUsers");
        }
    }
}
