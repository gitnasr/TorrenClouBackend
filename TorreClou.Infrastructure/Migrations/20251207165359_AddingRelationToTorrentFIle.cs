using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddingRelationToTorrentFIle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TorrentFiles_Users_UploadedByUserId1",
                table: "TorrentFiles");

            migrationBuilder.DropIndex(
                name: "IX_TorrentFiles_UploadedByUserId1",
                table: "TorrentFiles");

            migrationBuilder.DropColumn(
                name: "UploadedByUserId1",
                table: "TorrentFiles");

            migrationBuilder.AddColumn<int>(
                name: "UploadedByUserId",
                table: "TorrentFiles",
                type: "integer",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_TorrentFiles_UploadedByUserId",
                table: "TorrentFiles",
                column: "UploadedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TorrentFiles_Users_UploadedByUserId",
                table: "TorrentFiles",
                column: "UploadedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
    }
}
