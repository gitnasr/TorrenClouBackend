using Microsoft.EntityFrameworkCore;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Jobs;
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
        public DbSet<S3SyncProgress> S3SyncProgresses { get; set; }

        // --- Status History (Timeline) ---
        public DbSet<JobStatusHistory> JobStatusHistories { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Set default schema to 'dev'
            builder.HasDefaultSchema("dev");

            // --- User Configuration ---
            builder.Entity<User>()
                .HasIndex(u => u.Email).IsUnique();
      

            // --- Storage Profile ---
            builder.Entity<UserStorageProfile>()
                .Property(p => p.ProviderType).HasConversion<string>();
            builder.Entity<UserStorageProfile>()
                .Property(p => p.CredentialsJson).HasColumnType("jsonb");

      

            // --- Jobs ---
            builder.Entity<UserJob>()
                .Property(j => j.Status).HasConversion<string>();
            builder.Entity<UserJob>()
                .Property(j => j.Type).HasConversion<string>();
            builder.Entity<UserJob>()
                .Property(e => e.SelectedFilePaths).HasColumnType("text[]"); // PostgreSQL Array


            // --- Job Status History (Timeline) ---
            builder.Entity<JobStatusHistory>()
                .Property(h => h.FromStatus).HasConversion<string>();
            builder.Entity<JobStatusHistory>()
                .Property(h => h.ToStatus).HasConversion<string>();
            builder.Entity<JobStatusHistory>()
                .Property(h => h.Source).HasConversion<string>();
            builder.Entity<JobStatusHistory>()
                .Property(h => h.MetadataJson).HasColumnType("jsonb");
            builder.Entity<JobStatusHistory>()
                .HasOne(h => h.Job)
                .WithMany(j => j.StatusHistory)
                .HasForeignKey(h => h.JobId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<JobStatusHistory>()
                .HasIndex(h => new { h.JobId, h.ChangedAt });


            // --- S3 Progress ---
            builder.Entity<S3SyncProgress>()
                .Property(p => p.Status).HasConversion<string>();
            builder.Entity<S3SyncProgress>()
                .HasOne(p => p.UserJob).WithMany().HasForeignKey(p => p.JobId).OnDelete(DeleteBehavior.Cascade);

            // --- Strikes ---
            builder.Entity<UserStrike>()
                .Property(s => s.ViolationType).HasConversion<string>();
            builder.Entity<UserStrike>()
                .HasOne(s => s.User).WithMany(u => u.Strikes).HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);


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