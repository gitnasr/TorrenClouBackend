using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddingUserToTorrentFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UploadedByUserId",
                table: "TorrentFiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "UploadedByUserId1",
                table: "TorrentFiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TorrentFiles_UploadedByUserId1",
                table: "TorrentFiles",
                column: "UploadedByUserId1");

            migrationBuilder.AddForeignKey(
                name: "FK_TorrentFiles_Users_UploadedByUserId1",
                table: "TorrentFiles",
                column: "UploadedByUserId1",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TorrentFiles_Users_UploadedByUserId1",
                table: "TorrentFiles");

            migrationBuilder.DropIndex(
                name: "IX_TorrentFiles_UploadedByUserId1",
                table: "TorrentFiles");

            migrationBuilder.DropColumn(
                name: "UploadedByUserId",
                table: "TorrentFiles");

            migrationBuilder.DropColumn(
                name: "UploadedByUserId1",
                table: "TorrentFiles");
        }
    }
}
