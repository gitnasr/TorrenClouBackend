using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptinalJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_UserJobs_JobId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_WalletTransactions_WalletTransactionId1",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_WalletTransactionId1",
                table: "Invoices");

            // لو العمود ده كان موجود فعلاً
            migrationBuilder.DropColumn(
                name: "WalletTransactionId1",
                table: "Invoices");

            // 👇 بدل AlterColumn اللي عامل مشكلة
            // نحذف العمود القديم string
            migrationBuilder.DropColumn(
                name: "WalletTransactionId",
                table: "Invoices");

            // ونعيده من تاني كـ int? زي ما الكود بتاعك عايز
            migrationBuilder.AddColumn<int>(
                name: "WalletTransactionId",
                table: "Invoices",
                type: "integer",
                nullable: true);

            // JobId: التغيير هنا بس من not null → nullable، ده مفيهوش مشكلة
            migrationBuilder.AlterColumn<int>(
                name: "JobId",
                table: "Invoices",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

  



            migrationBuilder.CreateIndex(
                name: "IX_Invoices_WalletTransactionId",
                table: "Invoices",
                column: "WalletTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_UserJobs_JobId",
                table: "Invoices",
                column: "JobId",
                principalTable: "UserJobs",
                principalColumn: "Id");

    

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_WalletTransactions_WalletTransactionId",
                table: "Invoices",
                column: "WalletTransactionId",
                principalTable: "WalletTransactions",
                principalColumn: "Id");
        }

        /// <inheritdoc />

    }
}
