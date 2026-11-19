using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabStage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TwoFactorSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TwoFactorEnabledAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TwoFactorLastStep",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorRecoveryCodes",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorSecret",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwoFactorEnabledAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorLastStep",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorRecoveryCodes",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorSecret",
                table: "Users");
        }
    }
}
