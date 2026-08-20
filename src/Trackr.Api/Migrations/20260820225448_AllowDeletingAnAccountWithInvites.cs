using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AllowDeletingAnAccountWithInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invites_AspNetUsers_CreatedByUserId",
                table: "Invites");

            migrationBuilder.DropForeignKey(
                name: "FK_Invites_AspNetUsers_RedeemedByUserId",
                table: "Invites");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "Invites",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_Invites_AspNetUsers_CreatedByUserId",
                table: "Invites",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Invites_AspNetUsers_RedeemedByUserId",
                table: "Invites",
                column: "RedeemedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invites_AspNetUsers_CreatedByUserId",
                table: "Invites");

            migrationBuilder.DropForeignKey(
                name: "FK_Invites_AspNetUsers_RedeemedByUserId",
                table: "Invites");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "Invites",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Invites_AspNetUsers_CreatedByUserId",
                table: "Invites",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invites_AspNetUsers_RedeemedByUserId",
                table: "Invites",
                column: "RedeemedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
