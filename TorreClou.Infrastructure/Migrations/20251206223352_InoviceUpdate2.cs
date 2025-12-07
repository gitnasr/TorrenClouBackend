using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InoviceUpdate2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserJobs_CachedTorrents_CachedTorrentId",
                table: "UserJobs");

            migrationBuilder.DropTable(
                name: "CachedTorrents");

            migrationBuilder.RenameColumn(
                name: "CachedTorrentId",
                table: "UserJobs",
                newName: "TorrentFileId");

            migrationBuilder.RenameIndex(
                name: "IX_UserJobs_CachedTorrentId",
                table: "UserJobs",
                newName: "IX_UserJobs_TorrentFileId");

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalPriceInUsd",
                table: "Invoices",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TorrentId",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "torrentFileId",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TorrentFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Files = table.Column<string[]>(type: "text[]", nullable: false),
                    InfoHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TorrentFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_torrentFileId",
                table: "Invoices",
                column: "torrentFileId");

            migrationBuilder.CreateIndex(
                name: "IX_TorrentFiles_InfoHash",
                table: "TorrentFiles",
                column: "InfoHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_TorrentFiles_torrentFileId",
                table: "Invoices",
                column: "torrentFileId",
                principalTable: "TorrentFiles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserJobs_TorrentFiles_TorrentFileId",
                table: "UserJobs",
                column: "TorrentFileId",
                principalTable: "TorrentFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_TorrentFiles_torrentFileId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_UserJobs_TorrentFiles_TorrentFileId",
                table: "UserJobs");

            migrationBuilder.DropTable(
                name: "TorrentFiles");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_torrentFileId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FinalPriceInUsd",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TorrentId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "torrentFileId",
                table: "Invoices");

            migrationBuilder.RenameColumn(
                name: "TorrentFileId",
                table: "UserJobs",
                newName: "CachedTorrentId");

            migrationBuilder.RenameIndex(
                name: "IX_UserJobs_TorrentFileId",
                table: "UserJobs",
                newName: "IX_UserJobs_CachedTorrentId");

            migrationBuilder.CreateTable(
                name: "CachedTorrents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InfoHash = table.Column<string>(type: "text", nullable: false),
                    LocalFilePath = table.Column<string>(type: "text", nullable: true),
                    MagnetLink = table.Column<string>(type: "text", nullable: false),
                    SeedersCountSnapshot = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedTorrents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CachedTorrents_InfoHash",
                table: "CachedTorrents",
                column: "InfoHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserJobs_CachedTorrents_CachedTorrentId",
                table: "UserJobs",
                column: "CachedTorrentId",
                principalTable: "CachedTorrents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
