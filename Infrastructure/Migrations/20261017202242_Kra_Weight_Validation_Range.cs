using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Kra_Weight_Validation_Range : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Kras_Weight_Range",
                table: "Kras",
                sql: "CAST([Weight] AS REAL) > 0.0 AND CAST([Weight] AS REAL) <= 100.0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Kras_Weight_Range",
                table: "Kras");
        }
    }
}
