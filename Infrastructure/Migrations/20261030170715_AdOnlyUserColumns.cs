using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// AD owns passwords, failed-attempt counting and lockout, and last-sign-in is not retained,
    /// so the local-auth columns are removed from Users. This migration is already applied to the
    /// development database as 20261030170715_AdOnlyUserColumns; the file is committed so the
    /// codebase and migration history agree.
    /// </summary>
    [DbContext(typeof(SpmsDbContext))]
    [Migration("20261030170715_AdOnlyUserColumns")]
    public partial class AdOnlyUserColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PasswordHash", table: "Users");
            migrationBuilder.DropColumn(name: "FailedLoginCount", table: "Users");
            migrationBuilder.DropColumn(name: "LockedUntilUtc", table: "Users");
            migrationBuilder.DropColumn(name: "LastLoginAtUtc", table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntilUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);
        }
    }
}
