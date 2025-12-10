using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTypeToUserJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "UserJobs",
                type: "text",
                nullable: false,
                defaultValue: "Torrent");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "UserJobs");
        }
    }
}
