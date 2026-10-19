using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    [DbContext(typeof(SpmsDbContext))]
    [Migration("20261019090000_Rbac_Access_Management")]
    public partial class Rbac_Access_Management : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dynamic RBAC (ADR-011): extend the existing Role/Permission spine with an
            // explicit Grant/Deny effect and add direct per-user assignments. Legacy rows
            // default to Grant so current authorization behavior is untouched.

            migrationBuilder.AddColumn<int>(
                name: "Effect",
                table: "RolePermissions",
                type: "int",
                nullable: false,
                defaultValue: 1); // PermissionEffect.Grant

            migrationBuilder.CreateTable(
                name: "UserRights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Effect = table.Column<int>(type: "int", nullable: false, defaultValue: 1), // PermissionEffect.Grant
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRights_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRights_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserRights_PermissionId",
                table: "UserRights",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRights_UserId_PermissionId",
                table: "UserRights",
                columns: new[] { "UserId", "PermissionId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserRights");

            migrationBuilder.DropColumn(
                name: "Effect",
                table: "RolePermissions");
        }
    }
}
