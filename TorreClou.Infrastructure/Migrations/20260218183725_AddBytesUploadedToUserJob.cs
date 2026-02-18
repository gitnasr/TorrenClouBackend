using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBytesUploadedToUserJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserStrike",
                schema: "dev");

            migrationBuilder.DropColumn(
                name: "GoogleDriveEmail",
                schema: "dev",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GoogleDriveRefreshToken",
                schema: "dev",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GoogleDriveTokenCreatedAt",
                schema: "dev",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsGoogleDriveConnected",
                schema: "dev",
                table: "Users");

            migrationBuilder.AddColumn<long>(
                name: "BytesUploaded",
                schema: "dev",
                table: "UserJobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BytesUploaded",
                schema: "dev",
                table: "UserJobs");

            migrationBuilder.AddColumn<string>(
                name: "GoogleDriveEmail",
                schema: "dev",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleDriveRefreshToken",
                schema: "dev",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GoogleDriveTokenCreatedAt",
                schema: "dev",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGoogleDriveConnected",
                schema: "dev",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "UserStrike",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ViolationType = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStrike", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserStrike_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dev",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserStrike_UserId",
                schema: "dev",
                table: "UserStrike",
                column: "UserId");
        }
    }
}
