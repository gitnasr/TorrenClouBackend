using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixingTypos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_TorrentFiles_torrentFileId",
                table: "Invoices");

            migrationBuilder.RenameColumn(
                name: "torrentFileId",
                table: "Invoices",
                newName: "TorrentFileId");

            migrationBuilder.RenameIndex(
                name: "IX_Invoices_torrentFileId",
                table: "Invoices",
                newName: "IX_Invoices_TorrentFileId");

            migrationBuilder.AlterColumn<int>(
                name: "TorrentFileId",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_TorrentFiles_TorrentFileId",
                table: "Invoices",
                column: "TorrentFileId",
                principalTable: "TorrentFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_TorrentFiles_TorrentFileId",
                table: "Invoices");

            migrationBuilder.RenameColumn(
                name: "TorrentFileId",
                table: "Invoices",
                newName: "torrentFileId");

            migrationBuilder.RenameIndex(
                name: "IX_Invoices_TorrentFileId",
                table: "Invoices",
                newName: "IX_Invoices_torrentFileId");

            migrationBuilder.AlterColumn<int>(
                name: "torrentFileId",
                table: "Invoices",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_TorrentFiles_torrentFileId",
                table: "Invoices",
                column: "torrentFileId",
                principalTable: "TorrentFiles",
                principalColumn: "Id");
        }
    }
}
