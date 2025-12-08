using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixedInvoiceTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IsRefunded",
                table: "Invoices");

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "Invoices");

            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRefunded",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
