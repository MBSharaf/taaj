using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabStage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderNumberSequenceAndChatIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Conversations_OrderId",
                table: "Conversations",
                column: "OrderId");

            // Sequence for order numbers (LS-000123). Starts after every number already used, so existing
            // databases continue where they are; the old COUNT(*)+188 rule collided under concurrent submits.
            migrationBuilder.Sql(@"
DECLARE @start bigint = 188 + (SELECT COUNT(*) FROM dbo.Orders);
DECLARE @max bigint = (SELECT MAX(TRY_CAST(SUBSTRING(OrderNumber, 4, 30) AS bigint)) FROM dbo.Orders WHERE OrderNumber LIKE 'LS-%');
IF @max IS NOT NULL AND @max + 1 > @start SET @start = @max + 1;
DECLARE @sql nvarchar(400) = N'CREATE SEQUENCE dbo.OrderNumberSeq AS bigint START WITH ' + CAST(@start AS nvarchar(20)) + N' INCREMENT BY 1 CACHE 50;';
EXEC sp_executesql @sql;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS dbo.OrderNumberSeq;");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_OrderId",
                table: "Conversations");
        }
    }
}
