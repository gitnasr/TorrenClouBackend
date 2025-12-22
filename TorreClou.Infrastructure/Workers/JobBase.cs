using Microsoft.Extensions.Logging;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Workers
{
    /// <summary>
    /// Abstract base class for all Hangfire jobs providing shared infrastructure.
    /// This is entity-agnostic and provides only common utilities like logging and unit of work.
    /// </summary>
    /// <typeparam name="TJob">The concrete job type for logger categorization.</typeparam>
    public abstract class JobBase<TJob>(
        IUnitOfWork unitOfWork,
        ILogger<TJob> logger) where TJob : class
    {
        protected readonly IUnitOfWork UnitOfWork = unitOfWork;
        protected readonly ILogger<TJob> Logger = logger;

        /// <summary>
        /// Log prefix for consistent logging (e.g., "[DOWNLOAD]", "[UPLOAD]", "[S3:SYNC]").
        /// </summary>
        protected abstract string LogPrefix { get; }
    }
}

