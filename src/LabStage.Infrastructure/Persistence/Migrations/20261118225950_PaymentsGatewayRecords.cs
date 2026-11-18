using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabStage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentsGatewayRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderRefundRef",
                table: "RefundRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AmountPiastres",
                table: "PaymentProviderEvents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutId",
                table: "PaymentProviderEvents",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "PaymentProviderEvents",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Succeeded",
                table: "PaymentProviderEvents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TransactionRef",
                table: "PaymentProviderEvents",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderEvents_CheckoutId",
                table: "PaymentProviderEvents",
                column: "CheckoutId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentProviderEvents_CheckoutId",
                table: "PaymentProviderEvents");

            migrationBuilder.DropColumn(
                name: "ProviderRefundRef",
                table: "RefundRequests");

            migrationBuilder.DropColumn(
                name: "AmountPiastres",
                table: "PaymentProviderEvents");

            migrationBuilder.DropColumn(
                name: "CheckoutId",
                table: "PaymentProviderEvents");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "PaymentProviderEvents");

            migrationBuilder.DropColumn(
                name: "Succeeded",
                table: "PaymentProviderEvents");

            migrationBuilder.DropColumn(
                name: "TransactionRef",
                table: "PaymentProviderEvents");
        }
    }
}
