using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DivisionGoalName_FilteredUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DivisionGoals_DivisionPlanId_Name",
                table: "DivisionGoals");

            migrationBuilder.CreateIndex(
                name: "IX_DivisionGoals_DivisionPlanId_Name",
                table: "DivisionGoals",
                columns: new[] { "DivisionPlanId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DivisionGoals_DivisionPlanId_Name",
                table: "DivisionGoals");

            migrationBuilder.CreateIndex(
                name: "IX_DivisionGoals_DivisionPlanId_Name",
                table: "DivisionGoals",
                columns: new[] { "DivisionPlanId", "Name" },
                unique: true);
        }
    }
}
