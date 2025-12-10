using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BytesDownloaded",
                table: "UserJobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "DownloadPath",
                table: "UserJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HangfireJobId",
                table: "UserJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalBytes",
                table: "UserJobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BytesDownloaded",
                table: "UserJobs");

            migrationBuilder.DropColumn(
                name: "DownloadPath",
                table: "UserJobs");

            migrationBuilder.DropColumn(
                name: "HangfireJobId",
                table: "UserJobs");

            migrationBuilder.DropColumn(
                name: "TotalBytes",
                table: "UserJobs");
        }
    }
}
