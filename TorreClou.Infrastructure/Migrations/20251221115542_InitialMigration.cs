using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TorreClou.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dev");

            migrationBuilder.CreateTable(
                name: "FlashSales",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DiscountPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    TargetRegion = table.Column<string>(type: "text", nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashSales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    OAuthProvider = table.Column<string>(type: "text", nullable: false),
                    OAuthSubjectId = table.Column<string>(type: "text", nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: false),
                    IsPhoneNumberVerified = table.Column<bool>(type: "boolean", nullable: false),
                    Region = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vouchers",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: false),
                    MaxUsesTotal = table.Column<int>(type: "integer", nullable: true),
                    MaxUsesPerUser = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Deposits",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    PaymentProvider = table.Column<string>(type: "text", nullable: false),
                    GatewayTransactionId = table.Column<string>(type: "text", nullable: false),
                    PaymentUrl = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    WalletTransactionId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deposits_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dev",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequestedFiles",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Files = table.Column<string[]>(type: "text[]", nullable: false),
                    InfoHash = table.Column<string>(type: "text", nullable: false),
                    FileType = table.Column<string>(type: "text", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "integer", nullable: false),
                    DirectUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestedFiles_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalSchema: "dev",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserStorageProfiles",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ProfileName = table.Column<string>(type: "text", nullable: false),
                    ProviderType = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    CredentialsJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStorageProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserStorageProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dev",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserStrikes",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ViolationType = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStrikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserStrikes_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dev",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    ReferenceId = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dev",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserVoucherUsages",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    VoucherId = table.Column<int>(type: "integer", nullable: false),
                    JobId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVoucherUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserVoucherUsages_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dev",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserVoucherUsages_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalSchema: "dev",
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserJobs",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    StorageProfileId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    RequestFileId = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CurrentState = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastHeartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HangfireJobId = table.Column<string>(type: "text", nullable: true),
                    DownloadPath = table.Column<string>(type: "text", nullable: true),
                    BytesDownloaded = table.Column<long>(type: "bigint", nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    SelectedFileIndices = table.Column<int[]>(type: "integer[]", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserJobs_RequestedFiles_RequestFileId",
                        column: x => x.RequestFileId,
                        principalSchema: "dev",
                        principalTable: "RequestedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserJobs_UserStorageProfiles_StorageProfileId",
                        column: x => x.StorageProfileId,
                        principalSchema: "dev",
                        principalTable: "UserStorageProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserJobs_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dev",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    JobId = table.Column<int>(type: "integer", nullable: true),
                    OriginalAmountInUSD = table.Column<decimal>(type: "numeric", nullable: false),
                    FinalAmountInUSD = table.Column<decimal>(type: "numeric", nullable: false),
                    FinalAmountInNCurrency = table.Column<decimal>(type: "numeric", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "numeric", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PricingSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WalletTransactionId = table.Column<int>(type: "integer", nullable: true),
                    VoucherId = table.Column<int>(type: "integer", nullable: true),
                    TorrentFileId = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_RequestedFiles_TorrentFileId",
                        column: x => x.TorrentFileId,
                        principalSchema: "dev",
                        principalTable: "RequestedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invoices_UserJobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "dev",
                        principalTable: "UserJobs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Invoices_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalSchema: "dev",
                        principalTable: "Vouchers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Invoices_WalletTransactions_WalletTransactionId",
                        column: x => x.WalletTransactionId,
                        principalSchema: "dev",
                        principalTable: "WalletTransactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Syncs",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LocalFilePath = table.Column<string>(type: "text", nullable: true),
                    S3KeyPrefix = table.Column<string>(type: "text", nullable: true),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    BytesSynced = table.Column<long>(type: "bigint", nullable: false),
                    FilesTotal = table.Column<int>(type: "integer", nullable: false),
                    FilesSynced = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastHeartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HangfireJobId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                name: "S3SyncProgresses",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<int>(type: "integer", nullable: false),
                    SyncId = table.Column<int>(type: "integer", nullable: false),
                    LocalFilePath = table.Column<string>(type: "text", nullable: false),
                    S3Key = table.Column<string>(type: "text", nullable: false),
                    UploadId = table.Column<string>(type: "text", nullable: true),
                    PartSize = table.Column<long>(type: "bigint", nullable: false),
                    TotalParts = table.Column<int>(type: "integer", nullable: false),
                    PartsCompleted = table.Column<int>(type: "integer", nullable: false),
                    BytesUploaded = table.Column<long>(type: "bigint", nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    PartETags = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LastPartNumber = table.Column<int>(type: "integer", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_S3SyncProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_S3SyncProgresses_Syncs_SyncId",
                        column: x => x.SyncId,
                        principalSchema: "dev",
                        principalTable: "Syncs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_S3SyncProgresses_UserJobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "dev",
                        principalTable: "UserJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deposits_GatewayTransactionId",
                schema: "dev",
                table: "Deposits",
                column: "GatewayTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Deposits_UserId",
                schema: "dev",
                table: "Deposits",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_JobId",
                schema: "dev",
                table: "Invoices",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TorrentFileId",
                schema: "dev",
                table: "Invoices",
                column: "TorrentFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_VoucherId",
                schema: "dev",
                table: "Invoices",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_WalletTransactionId",
                schema: "dev",
                table: "Invoices",
                column: "WalletTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestedFiles_InfoHash_UploadedByUserId",
                schema: "dev",
                table: "RequestedFiles",
                columns: new[] { "InfoHash", "UploadedByUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestedFiles_UploadedByUserId",
                schema: "dev",
                table: "RequestedFiles",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_S3SyncProgresses_JobId",
                schema: "dev",
                table: "S3SyncProgresses",
                column: "JobId");

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
                name: "IX_UserJobs_RequestFileId",
                schema: "dev",
                table: "UserJobs",
                column: "RequestFileId");

            migrationBuilder.CreateIndex(
                name: "IX_UserJobs_StorageProfileId",
                schema: "dev",
                table: "UserJobs",
                column: "StorageProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_UserJobs_UserId",
                schema: "dev",
                table: "UserJobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                schema: "dev",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserStorageProfiles_UserId",
                schema: "dev",
                table: "UserStorageProfiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserStrikes_UserId",
                schema: "dev",
                table: "UserStrikes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserVoucherUsages_UserId",
                schema: "dev",
                table: "UserVoucherUsages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserVoucherUsages_VoucherId",
                schema: "dev",
                table: "UserVoucherUsages",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_Code",
                schema: "dev",
                table: "Vouchers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_UserId",
                schema: "dev",
                table: "WalletTransactions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deposits",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "FlashSales",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "Invoices",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "S3SyncProgresses",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "UserStrikes",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "UserVoucherUsages",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "WalletTransactions",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "Syncs",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "Vouchers",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "UserJobs",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "RequestedFiles",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "UserStorageProfiles",
                schema: "dev");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "dev");
        }
    }
}
