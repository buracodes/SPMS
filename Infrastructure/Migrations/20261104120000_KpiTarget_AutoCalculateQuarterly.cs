using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    // Authored without `dotnet ef migrations add`, so the attributes it puts in the generated
    // Designer file are declared here.
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(Infrastructure.Persistence.Context.SpmsDbContext))]
    [Migration("20261104120000_KpiTarget_AutoCalculateQuarterly")]
    public partial class KpiTarget_AutoCalculateQuarterly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDerivedFromMonthly",
                table: "KpiTargets",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDerivedFromMonthly",
                table: "KpiTargets");
        }
    }
}
