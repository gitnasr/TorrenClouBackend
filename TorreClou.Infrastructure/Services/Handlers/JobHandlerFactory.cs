using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Services.Handlers
{
    /// <summary>
    /// Factory for resolving job handlers based on provider type and job type.
    /// Uses dependency injection to retrieve registered handlers.
    /// </summary>
    public class JobHandlerFactory(
        IEnumerable<IStorageProviderHandler> storageProviderHandlers,
        IEnumerable<IJobTypeHandler> jobTypeHandlers,
        IEnumerable<IJobCancellationHandler> cancellationHandlers) : IJobHandlerFactory
    {
        private readonly Dictionary<StorageProviderType, IStorageProviderHandler> _storageProviderHandlers = storageProviderHandlers.ToDictionary(h => h.ProviderType);
        private readonly Dictionary<JobType, IJobTypeHandler> _jobTypeHandlers = jobTypeHandlers.ToDictionary(h => h.JobType);
        private readonly Dictionary<JobType, IJobCancellationHandler> _cancellationHandlers = cancellationHandlers.ToDictionary(h => h.JobType);

        public IStorageProviderHandler? GetStorageProviderHandler(StorageProviderType providerType)
        {
            return _storageProviderHandlers.TryGetValue(providerType, out var handler) ? handler : null;
        }

        public IJobTypeHandler? GetJobTypeHandler(JobType jobType)
        {
            return _jobTypeHandlers.TryGetValue(jobType, out var handler) ? handler : null;
        }

        public IJobCancellationHandler? GetCancellationHandler(JobType jobType)
        {
            return _cancellationHandlers.TryGetValue(jobType, out var handler) ? handler : null;
        }

        public IEnumerable<IStorageProviderHandler> GetAllStorageProviderHandlers()
        {
            return _storageProviderHandlers.Values;
        }

        public IEnumerable<IJobTypeHandler> GetAllJobTypeHandlers()
        {
            return _jobTypeHandlers.Values;
        }

        public IEnumerable<IJobCancellationHandler> GetAllCancellationHandlers()
        {
            return _cancellationHandlers.Values;
        }
    }
}


