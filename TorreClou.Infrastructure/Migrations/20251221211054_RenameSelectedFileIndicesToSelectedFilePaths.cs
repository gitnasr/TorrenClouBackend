using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameSelectedFileIndicesToSelectedFilePaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename column and change type from integer[] to text[]
            migrationBuilder.RenameColumn(
                name: "SelectedFileIndices",
                schema: "dev",
                table: "UserJobs",
                newName: "SelectedFilePaths");

            migrationBuilder.AlterColumn<string[]>(
                name: "SelectedFilePaths",
                schema: "dev",
                table: "UserJobs",
                type: "text[]",
                nullable: false,
                oldClrType: typeof(int[]),
                oldType: "integer[]");

            migrationBuilder.AddColumn<string>(
                name: "HangfireUploadJobId",
                schema: "dev",
                table: "UserJobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HangfireUploadJobId",
                schema: "dev",
                table: "UserJobs");

            // Revert column name and type
            migrationBuilder.AlterColumn<int[]>(
                name: "SelectedFilePaths",
                schema: "dev",
                table: "UserJobs",
                type: "integer[]",
                nullable: false,
                oldClrType: typeof(string[]),
                oldType: "text[]");

            migrationBuilder.RenameColumn(
                name: "SelectedFilePaths",
                schema: "dev",
                table: "UserJobs",
                newName: "SelectedFileIndices");
        }
    }
}
