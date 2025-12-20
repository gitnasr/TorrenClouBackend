using Microsoft.EntityFrameworkCore;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Entities.Marketing;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Entities.Compliance;
using TorreClou.Core.Enums;

namespace TorreClou.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // 1. تعريف الجداول
        public DbSet<User> Users { get; set; }
        public DbSet<UserStorageProfile> UserStorageProfiles { get; set; }
        public DbSet<WalletTransaction> WalletTransactions { get; set; }
        public DbSet<RequestedFile> RequestedFiles { get; set; }
        public DbSet<UserJob> UserJobs { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<UserStrike> UserStrikes { get; set; }
        public DbSet<Sync> Syncs { get; set; }
        public DbSet<S3SyncProgress> S3SyncProgresses { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // --- User Configuration ---
            builder.Entity<User>()
                .HasIndex(u => u.Email).IsUnique(); // الإيميل لازم يكون فريد

            builder.Entity<User>()
                .Property(u => u.Role)
                .HasConversion<string>();

            builder.Entity<User>()
                .Property(u => u.Region)
                .HasConversion<string>();

            builder.Entity<UserStorageProfile>()
                .Property(p => p.ProviderType)
                .HasConversion<string>();
            builder.Entity<UserStorageProfile>()
                .Property(p => p.CredentialsJson)
                .HasColumnType("jsonb");

            builder.Entity<WalletTransaction>()
                .Property(t => t.Type)
                .HasConversion<string>();

            // --- Job Configuration ---
            builder.Entity<UserJob>()
                .Property(j => j.Status)
                .HasConversion<string>();

            builder.Entity<UserJob>()
                .Property(j => j.Type)
                .HasConversion<string>();

            // --- Sync Configuration ---
            builder.Entity<Sync>()
                .Property(s => s.Status)
                .HasConversion<string>();

            builder.Entity<Sync>()
                .HasOne(s => s.UserJob)
                .WithMany()
                .HasForeignKey(s => s.JobId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- S3SyncProgress Configuration ---
            builder.Entity<S3SyncProgress>()
                .Property(p => p.Status)
                .HasConversion<string>();

            builder.Entity<S3SyncProgress>()
                .HasOne(p => p.UserJob)
                .WithMany()
                .HasForeignKey(p => p.JobId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<S3SyncProgress>()
                .HasOne(p => p.Sync)
                .WithMany(s => s.FileProgress)
                .HasForeignKey(p => p.SyncId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- Invoice Configuration ---
            builder.Entity<Invoice>()
                .Property(i => i.PricingSnapshotJson)
                .HasColumnType("jsonb"); // JSONB للفواتير كمان

            // --- Relationships (تأكيد العلاقات) ---
            // User -> Wallet (One-to-Many)
            builder.Entity<WalletTransaction>()
                .HasOne(w => w.User)
                .WithMany(u => u.WalletTransactions)
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);


            builder.Entity<Voucher>()
                .HasIndex(v => v.Code).IsUnique();

            builder.Entity<Voucher>()
                .Property(v => v.Type)
                .HasConversion<string>();

            builder.Entity<FlashSale>()
                .Property(f => f.TargetRegion)
                .HasConversion<string>();

            builder.Entity<UserVoucherUsage>()
                .HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId);

            builder.Entity<UserVoucherUsage>()
                .HasOne(x => x.Voucher)
                .WithMany(v => v.Usages)
                .HasForeignKey(x => x.VoucherId);

            // --- UserStrike Configuration ---
            builder.Entity<UserStrike>()
                .Property(s => s.ViolationType)
                .HasConversion<string>();

            builder.Entity<UserStrike>()
                .HasOne(s => s.User)
                .WithMany(u => u.Strikes)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);


            builder.Entity<Deposit>()
        .Property(d => d.Status)
        .HasConversion<string>();

            builder.Entity<Deposit>()
                .HasIndex(d => d.GatewayTransactionId);

            builder.Entity<UserJob>()
                .Property(e => e.SelectedFileIndices)
                .HasColumnType("integer[]");

            // Composite unique index: allows multiple users to have the same torrent,
            // but prevents duplicate entries for the same user and torrent
            builder.Entity<RequestedFile>()
                .HasIndex(t => new { t.InfoHash, t.UploadedByUserId })
                .IsUnique();

            builder.Entity<RequestedFile>()
                .HasOne(t => t.UploadedByUser)
                .WithMany(u => u.UploadedTorrentFiles)
                .HasForeignKey(t => t.UploadedByUserId);

        }
    }
}