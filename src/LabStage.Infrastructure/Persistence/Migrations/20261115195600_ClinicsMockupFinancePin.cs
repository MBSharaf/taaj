using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabStage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClinicsMockupFinancePin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMockup",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FinancePinHash",
                table: "Laboratories",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinancePinSetAt",
                table: "Laboratories",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clinics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Governorate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clinics_Users_DoctorUserId",
                        column: x => x.DoctorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ClinicId",
                table: "Orders",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_DoctorUserId",
                table: "Clinics",
                column: "DoctorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Clinics_ClinicId",
                table: "Orders",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Existing dentists: their profile clinic becomes their default clinic, and their orders point to it.
            migrationBuilder.Sql(@"
INSERT INTO [Clinics] ([Id],[DoctorUserId],[Name],[Address],[City],[Governorate],[Phone],[IsDefault],[CreatedAt])
SELECT NEWID(), p.[UserId], p.[ClinicName], p.[Address], p.[City], p.[Governorate], ISNULL(u.[Phone], N''), 1, SYSDATETIMEOFFSET()
FROM [DoctorProfiles] p JOIN [Users] u ON u.[Id] = p.[UserId]
WHERE p.[ClinicName] <> N'' AND NOT EXISTS (SELECT 1 FROM [Clinics] c WHERE c.[DoctorUserId] = p.[UserId]);
UPDATE o SET o.[ClinicId] = c.[Id]
FROM [Orders] o JOIN [Clinics] c ON c.[DoctorUserId] = o.[DentistUserId] AND c.[IsDefault] = 1
WHERE o.[ClinicId] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Clinics_ClinicId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "Clinics");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ClinicId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsMockup",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinancePinHash",
                table: "Laboratories");

            migrationBuilder.DropColumn(
                name: "FinancePinSetAt",
                table: "Laboratories");
        }
    }
}
