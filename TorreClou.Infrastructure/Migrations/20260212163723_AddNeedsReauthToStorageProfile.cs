using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNeedsReauthToStorageProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserStrikes_Users_UserId",
                schema: "dev",
                table: "UserStrikes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserStrikes",
                schema: "dev",
                table: "UserStrikes");

            migrationBuilder.RenameTable(
                name: "UserStrikes",
                schema: "dev",
                newName: "UserStrike",
                newSchema: "dev");

            migrationBuilder.RenameIndex(
                name: "IX_UserStrikes_UserId",
                schema: "dev",
                table: "UserStrike",
                newName: "IX_UserStrike_UserId");

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReauth",
                schema: "dev",
                table: "UserStorageProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserStrike",
                schema: "dev",
                table: "UserStrike",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserStrike_Users_UserId",
                schema: "dev",
                table: "UserStrike",
                column: "UserId",
                principalSchema: "dev",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserStrike_Users_UserId",
                schema: "dev",
                table: "UserStrike");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserStrike",
                schema: "dev",
                table: "UserStrike");

            migrationBuilder.DropColumn(
                name: "NeedsReauth",
                schema: "dev",
                table: "UserStorageProfiles");

            migrationBuilder.RenameTable(
                name: "UserStrike",
                schema: "dev",
                newName: "UserStrikes",
                newSchema: "dev");

            migrationBuilder.RenameIndex(
                name: "IX_UserStrike_UserId",
                schema: "dev",
                table: "UserStrikes",
                newName: "IX_UserStrikes_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserStrikes",
                schema: "dev",
                table: "UserStrikes",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserStrikes_Users_UserId",
                schema: "dev",
                table: "UserStrikes",
                column: "UserId",
                principalSchema: "dev",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
