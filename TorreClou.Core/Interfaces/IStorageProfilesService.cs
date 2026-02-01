using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IStorageProfilesService
    {
        Task<Result<List<StorageProfileDto>>> GetStorageProfilesAsync(int userId);
        Task<Result<StorageProfileDetailDto>> GetStorageProfileAsync(int userId, int id);
        Task<Result> SetDefaultProfileAsync(int userId, int id);
        Task<Result<UserStorageProfile>> ValidateActiveStorageProfileByUserId(int userId, int profileId);
        Task<Result> DisconnectProfileAsync(int userId, int id);
        
        // S3 Provider Configuration
        Task<Result<StorageProfileResultDto>> ConfigureS3StorageAsync(
            int userId,
            string profileName,
            string s3Endpoint,
            string s3AccessKey,
            string s3SecretKey,
            string s3BucketName,
            string s3Region,
            bool setAsDefault);
        
        Task<Result<StorageProvidersListDto>> GetUserStorageProvidersAsync(int userId);
        Task<Result<bool>> DeleteStorageProfileAsync(int userId, int profileId);
    }
}







