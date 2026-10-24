using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLogEntryClientRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "LogEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_UserId_ClientRequestId",
                table: "LogEntries",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LogEntries_UserId_ClientRequestId",
                table: "LogEntries");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "LogEntries");
        }
    }
}
