using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSyncEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_S3SyncProgresses_Syncs_SyncId",
                schema: "dev",
                table: "S3SyncProgresses");

            migrationBuilder.DropTable(
                name: "SyncStatusHistories",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "Syncs",
                schema: "dev");

            migrationBuilder.DropIndex(
                name: "IX_S3SyncProgresses_SyncId",
                schema: "dev",
                table: "S3SyncProgresses");

            migrationBuilder.DropColumn(
                name: "SyncId",
                schema: "dev",
                table: "S3SyncProgresses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SyncId",
                schema: "dev",
                table: "S3SyncProgresses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Syncs",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<int>(type: "integer", nullable: false),
                    BytesSynced = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    FilesSynced = table.Column<int>(type: "integer", nullable: false),
                    FilesTotal = table.Column<int>(type: "integer", nullable: false),
                    HangfireJobId = table.Column<string>(type: "text", nullable: true),
                    LastHeartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LocalFilePath = table.Column<string>(type: "text", nullable: true),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    S3KeyPrefix = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Syncs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Syncs_UserJobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "dev",
                        principalTable: "UserJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncStatusHistories",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SyncId = table.Column<int>(type: "integer", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    FromStatus = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    ToStatus = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncStatusHistories_Syncs_SyncId",
                        column: x => x.SyncId,
                        principalSchema: "dev",
                        principalTable: "Syncs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_S3SyncProgresses_SyncId",
                schema: "dev",
                table: "S3SyncProgresses",
                column: "SyncId");

            migrationBuilder.CreateIndex(
                name: "IX_Syncs_JobId",
                schema: "dev",
                table: "Syncs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncStatusHistories_SyncId_ChangedAt",
                schema: "dev",
                table: "SyncStatusHistories",
                columns: new[] { "SyncId", "ChangedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_S3SyncProgresses_Syncs_SyncId",
                schema: "dev",
                table: "S3SyncProgresses",
                column: "SyncId",
                principalSchema: "dev",
                principalTable: "Syncs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
