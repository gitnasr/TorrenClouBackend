using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddedInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PaymentGatewayId",
                table: "Invoices",
                newName: "WalletTransactionId");

            migrationBuilder.AddColumn<int[]>(
                name: "SelectedFileIndices",
                table: "UserJobs",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<int>(
                name: "WalletTransactionId1",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_WalletTransactionId1",
                table: "Invoices",
                column: "WalletTransactionId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_WalletTransactions_WalletTransactionId1",
                table: "Invoices",
                column: "WalletTransactionId1",
                principalTable: "WalletTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_WalletTransactions_WalletTransactionId1",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_WalletTransactionId1",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SelectedFileIndices",
                table: "UserJobs");

            migrationBuilder.DropColumn(
                name: "WalletTransactionId1",
                table: "Invoices");

            migrationBuilder.RenameColumn(
                name: "WalletTransactionId",
                table: "Invoices",
                newName: "PaymentGatewayId");
        }
    }
}
