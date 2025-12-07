using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixInvoiceFileds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FinalPriceInUsd",
                table: "Invoices",
                newName: "FinalAmountInUSD");

            migrationBuilder.RenameColumn(
                name: "AmountInNCurrency",
                table: "Invoices",
                newName: "FinalAmountInNCurrency");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FinalAmountInUSD",
                table: "Invoices",
                newName: "FinalPriceInUsd");

            migrationBuilder.RenameColumn(
                name: "FinalAmountInNCurrency",
                table: "Invoices",
                newName: "AmountInNCurrency");
        }
    }
}
