using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabStage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Notifications",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailedAt",
                table: "Notifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderNumber",
                table: "Notifications",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    InSite = table.Column<bool>(type: "bit", nullable: false),
                    Email = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId_Category",
                table: "NotificationPreferences",
                columns: new[] { "UserId", "Category" },
                unique: true);

            // Existing notifications: category from their type, order number from their link.
            migrationBuilder.Sql(@"UPDATE [Notifications] SET [Category] = CASE
                WHEN [Type] IN ('NewOrder','OrderAccepted','OrderRejected','PriceProposed','PriceApproved','OrderCompleted') THEN 'Orders'
                WHEN [Type] IN ('StageStarted','StageUpdated','StageCompleted','StageApproved','ReviewNeeded','RevisionRequested') THEN 'Production'
                WHEN [Type] IN ('PaymentDue','PaymentConfirmed','PaymentReceived','PaymentCollected','RefundExecuted','SettlementPaid') THEN 'Payments'
                WHEN [Type] IN ('Delivery','OutForDelivery','Delivered') THEN 'Delivery'
                WHEN [Type] = 'NewMessage' THEN 'Chat' ELSE 'Account' END
              WHERE [Category] = N''");
            migrationBuilder.Sql(@"UPDATE [Notifications] SET [OrderNumber] = LEFT(SUBSTRING([LinkUrl], CHARINDEX('orderNumber=', [LinkUrl]) + 12, 32),
                CASE WHEN CHARINDEX('&', SUBSTRING([LinkUrl], CHARINDEX('orderNumber=', [LinkUrl]) + 12, 32)) > 0
                     THEN CHARINDEX('&', SUBSTRING([LinkUrl], CHARINDEX('orderNumber=', [LinkUrl]) + 12, 32)) - 1 ELSE 32 END)
              WHERE [OrderNumber] IS NULL AND CHARINDEX('orderNumber=', [LinkUrl]) > 0");
            // Seeded/old notifications were never meant to be emailed again.
            migrationBuilder.Sql(@"UPDATE [Notifications] SET [EmailedAt] = [CreatedAt] WHERE [EmailedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_IsRead_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EmailedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "Notifications");
        }
    }
}
