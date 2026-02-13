using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeparateOAuthCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OAuthCredentialId",
                schema: "dev",
                table: "UserStorageProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserOAuthCredentials",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<string>(type: "text", nullable: false),
                    ClientSecret = table.Column<string>(type: "text", nullable: false),
                    RedirectUri = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOAuthCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserOAuthCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dev",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserStorageProfiles_OAuthCredentialId",
                schema: "dev",
                table: "UserStorageProfiles",
                column: "OAuthCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOAuthCredentials_UserId_ClientId",
                schema: "dev",
                table: "UserOAuthCredentials",
                columns: new[] { "UserId", "ClientId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserStorageProfiles_UserOAuthCredentials_OAuthCredentialId",
                schema: "dev",
                table: "UserStorageProfiles",
                column: "OAuthCredentialId",
                principalSchema: "dev",
                principalTable: "UserOAuthCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserStorageProfiles_UserOAuthCredentials_OAuthCredentialId",
                schema: "dev",
                table: "UserStorageProfiles");

            migrationBuilder.DropTable(
                name: "UserOAuthCredentials",
                schema: "dev");

            migrationBuilder.DropIndex(
                name: "IX_UserStorageProfiles_OAuthCredentialId",
                schema: "dev",
                table: "UserStorageProfiles");

            migrationBuilder.DropColumn(
                name: "OAuthCredentialId",
                schema: "dev",
                table: "UserStorageProfiles");
        }
    }
}
