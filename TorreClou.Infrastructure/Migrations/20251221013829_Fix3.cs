using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Fix3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestedFiles_InfoHash_UploadedByUserId",
                table: "RequestedFiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RequestedFiles_InfoHash_UploadedByUserId",
                table: "RequestedFiles",
                columns: new[] { "InfoHash", "UploadedByUserId" },
                unique: true);
        }
    }
}
