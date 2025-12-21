using Microsoft.EntityFrameworkCore;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Entities.Marketing;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Entities.Compliance;

namespace TorreClou.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // --- Core Entities ---
        public DbSet<User> Users { get; set; }
        public DbSet<UserStorageProfile> UserStorageProfiles { get; set; }
        public DbSet<UserStrike> UserStrikes { get; set; }

        // --- Job & File Entities ---
        public DbSet<RequestedFile> RequestedFiles { get; set; }
        public DbSet<UserJob> UserJobs { get; set; }
        public DbSet<Sync> Syncs { get; set; }
        public DbSet<S3SyncProgress> S3SyncProgresses { get; set; }

        // --- Financial & Marketing Entities ---
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<WalletTransaction> WalletTransactions { get; set; }
        public DbSet<Voucher> Vouchers { get; set; } // Added
        public DbSet<UserVoucherUsage> UserVoucherUsages { get; set; } // Added
        public DbSet<FlashSale> FlashSales { get; set; } // Added
        public DbSet<Deposit> Deposits { get; set; } // Added

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Set default schema to 'dev'
            builder.HasDefaultSchema("dev");

            // --- User Configuration ---
            builder.Entity<User>()
                .HasIndex(u => u.Email).IsUnique();
            builder.Entity<User>()
                .Property(u => u.Role).HasConversion<string>();
            builder.Entity<User>()
                .Property(u => u.Region).HasConversion<string>();

            // --- Storage Profile ---
            builder.Entity<UserStorageProfile>()
                .Property(p => p.ProviderType).HasConversion<string>();
            builder.Entity<UserStorageProfile>()
                .Property(p => p.CredentialsJson).HasColumnType("jsonb");

            // --- Wallet ---
            builder.Entity<WalletTransaction>()
                .Property(t => t.Type).HasConversion<string>();
            builder.Entity<WalletTransaction>()
                .HasOne(w => w.User)
                .WithMany(u => u.WalletTransactions)
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- Jobs ---
            builder.Entity<UserJob>()
                .Property(j => j.Status).HasConversion<string>();
            builder.Entity<UserJob>()
                .Property(j => j.Type).HasConversion<string>();
            builder.Entity<UserJob>()
                .Property(e => e.SelectedFilePaths).HasColumnType("text[]"); // PostgreSQL Array

            // --- Sync ---
            builder.Entity<Sync>()
                .Property(s => s.Status).HasConversion<string>();
            builder.Entity<Sync>()
                .HasOne(s => s.UserJob)
                .WithMany()
                .HasForeignKey(s => s.JobId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- S3 Progress ---
            builder.Entity<S3SyncProgress>()
                .Property(p => p.Status).HasConversion<string>();
            builder.Entity<S3SyncProgress>()
                .HasOne(p => p.UserJob).WithMany().HasForeignKey(p => p.JobId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<S3SyncProgress>()
                .HasOne(p => p.Sync).WithMany(s => s.FileProgress).HasForeignKey(p => p.SyncId).OnDelete(DeleteBehavior.Cascade);

            // --- Invoice ---
            builder.Entity<Invoice>()
                .Property(i => i.PricingSnapshotJson).HasColumnType("jsonb");

            // --- Vouchers & Marketing ---
            builder.Entity<Voucher>()
                .HasIndex(v => v.Code).IsUnique();
            builder.Entity<Voucher>()
                .Property(v => v.Type).HasConversion<string>();

            builder.Entity<UserVoucherUsage>()
                .HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            builder.Entity<UserVoucherUsage>()
                .HasOne(x => x.Voucher).WithMany(v => v.Usages).HasForeignKey(x => x.VoucherId);

            builder.Entity<FlashSale>()
                .Property(f => f.TargetRegion).HasConversion<string>();

            // --- Strikes ---
            builder.Entity<UserStrike>()
                .Property(s => s.ViolationType).HasConversion<string>();
            builder.Entity<UserStrike>()
                .HasOne(s => s.User).WithMany(u => u.Strikes).HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);

            // --- Deposits ---
            builder.Entity<Deposit>()
                .Property(d => d.Status).HasConversion<string>();
            builder.Entity<Deposit>()
                .HasIndex(d => d.GatewayTransactionId);

            // --- Requested Files (Torrents) ---
            // Composite unique index: InfoHash + User = Unique Upload
            builder.Entity<RequestedFile>()
                .HasIndex(t => new { t.InfoHash, t.UploadedByUserId }).IsUnique();

            builder.Entity<RequestedFile>()
                .HasOne(t => t.UploadedByUser)
                .WithMany(u => u.UploadedTorrentFiles)
                .HasForeignKey(t => t.UploadedByUserId);
        }
    }
}