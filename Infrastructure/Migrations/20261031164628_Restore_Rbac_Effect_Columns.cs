using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// The development database applied RBAC migrations that were later removed from the
    /// source tree; those dropped the Grant/Deny effect columns that the current model still
    /// maps. The columns are re-added only when missing so databases that still receive them
    /// from <c>20261019090000_Rbac_Access_Management</c> are left untouched.
    /// </summary>
    public partial class Restore_Rbac_Effect_Columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH(N'RolePermissions', N'Effect') IS NULL
                    ALTER TABLE [RolePermissions] ADD [Effect] int NOT NULL
                        CONSTRAINT [DF_RolePermissions_Effect] DEFAULT 1;

                IF COL_LENGTH(N'UserRights', N'Effect') IS NULL
                    ALTER TABLE [UserRights] ADD [Effect] int NOT NULL
                        CONSTRAINT [DF_UserRights_Effect] DEFAULT 1;

                IF COL_LENGTH(N'UserRights', N'Reason') IS NULL
                    ALTER TABLE [UserRights] ADD [Reason] nvarchar(500) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Leaving the repair in place keeps every schema state valid for the current model.
        }
    }
}
