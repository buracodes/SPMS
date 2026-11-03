using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initiative_DivisionOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId",
                table: "Initiatives",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionOwnerId",
                table: "Initiatives",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Initiatives_DivisionOwnerId",
                table: "Initiatives",
                column: "DivisionOwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Initiatives_Divisions_DivisionOwnerId",
                table: "Initiatives",
                column: "DivisionOwnerId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Initiatives_Divisions_DivisionOwnerId",
                table: "Initiatives");

            migrationBuilder.DropIndex(
                name: "IX_Initiatives_DivisionOwnerId",
                table: "Initiatives");

            migrationBuilder.DropColumn(
                name: "DivisionOwnerId",
                table: "Initiatives");

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId",
                table: "Initiatives",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
