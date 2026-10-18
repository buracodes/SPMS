using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    [Migration("20261018210000_Kpi_Weight_And_Parent_Checks")]
    public partial class Kpi_Weight_And_Parent_Checks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tighten the parent relationship from "at most one parent" to "exactly
            // one parent" (FR-303 / FR-902): every KPI rolls up to either a KRA or a
            // division goal, never both and never neither.
            migrationBuilder.DropCheckConstraint(
                name: "CK_Kpis_KraOrDivisionGoal",
                table: "Kpis");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Kpis_KraOrDivisionGoal",
                table: "Kpis",
                sql: "([KraId] IS NOT NULL AND [DivisionGoalId] IS NULL) OR ([KraId] IS NULL AND [DivisionGoalId] IS NOT NULL)");

            // SQL-dialect matches the established Goal/KRA weight-range migrations
            // (provider-agnostic CAST form).
            migrationBuilder.AddCheckConstraint(
                name: "CK_Kpis_Weight_Range",
                table: "Kpis",
                sql: "CAST([Weight] AS REAL) > 0.0 AND CAST([Weight] AS REAL) <= 100.0");

            // Historical KPI targets are now preserved by soft-deleting stale rows
            // (FR-304/FR-302 lineage), so the upsert uniqueness only applies to active
            // rows and a rewritten combination can be recreated later.
            migrationBuilder.DropIndex(
                name: "IX_KpiTargets_KpiId_FiscalPeriodId_Granularity",
                table: "KpiTargets");

            migrationBuilder.CreateIndex(
                name: "IX_KpiTargets_KpiId_FiscalPeriodId_Granularity",
                table: "KpiTargets",
                columns: new[] { "KpiId", "FiscalPeriodId", "Granularity" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KpiTargets_KpiId_FiscalPeriodId_Granularity",
                table: "KpiTargets");

            migrationBuilder.CreateIndex(
                name: "IX_KpiTargets_KpiId_FiscalPeriodId_Granularity",
                table: "KpiTargets",
                columns: new[] { "KpiId", "FiscalPeriodId", "Granularity" },
                unique: true);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Kpis_Weight_Range",
                table: "Kpis");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Kpis_KraOrDivisionGoal",
                table: "Kpis");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Kpis_KraOrDivisionGoal",
                table: "Kpis",
                sql: "([KraId] IS NULL OR [DivisionGoalId] IS NULL)");
        }
    }
}
