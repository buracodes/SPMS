using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Wp1_DivisionDirectory_And_KpiOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_DivisionId",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DivisionId",
                table: "Users",
                column: "DivisionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Kpis_Owner_AtMostOne",
                table: "Kpis",
                sql: "NOT ([OwnerId] IS NOT NULL AND [DivisionOwnerId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_DivisionId",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Kpis_Owner_AtMostOne",
                table: "Kpis");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DivisionId",
                table: "Users",
                column: "DivisionId",
                unique: true,
                filter: "[DivisionId] IS NOT NULL");
        }
    }
}
