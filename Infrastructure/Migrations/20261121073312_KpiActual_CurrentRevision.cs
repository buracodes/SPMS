using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KpiActual_CurrentRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                table: "KpiActualValues",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesActualId",
                table: "KpiActualValues",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KpiActualValues_KpiId_FiscalPeriodId",
                table: "KpiActualValues",
                columns: new[] { "KpiId", "FiscalPeriodId" },
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_KpiActualValues_SupersedesActualId",
                table: "KpiActualValues",
                column: "SupersedesActualId");

            migrationBuilder.AddForeignKey(
                name: "FK_KpiActualValues_KpiActualValues_SupersedesActualId",
                table: "KpiActualValues",
                column: "SupersedesActualId",
                principalTable: "KpiActualValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KpiActualValues_KpiActualValues_SupersedesActualId",
                table: "KpiActualValues");

            migrationBuilder.DropIndex(
                name: "IX_KpiActualValues_KpiId_FiscalPeriodId",
                table: "KpiActualValues");

            migrationBuilder.DropIndex(
                name: "IX_KpiActualValues_SupersedesActualId",
                table: "KpiActualValues");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                table: "KpiActualValues");

            migrationBuilder.DropColumn(
                name: "SupersedesActualId",
                table: "KpiActualValues");
        }
    }
}
