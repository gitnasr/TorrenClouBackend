using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLastHeartbeatToUserJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeat",
                table: "UserJobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastHeartbeat",
                table: "UserJobs");
        }
    }
}
