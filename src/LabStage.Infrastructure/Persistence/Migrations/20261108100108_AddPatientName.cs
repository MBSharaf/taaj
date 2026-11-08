using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabStage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PatientName",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            // Existing orders: the case name is what the dentist typed (the new-case form stored the patient there).
            migrationBuilder.Sql("UPDATE [Orders] SET [PatientName] = [CaseReference] WHERE [PatientName] IS NULL AND [CaseReference] <> N''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PatientName",
                table: "Orders");
        }
    }
}
