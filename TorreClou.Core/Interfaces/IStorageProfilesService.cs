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
        Task<bool> HasDefaultProfile(int userId);
        Task Save();
        Task<Result> DisconnectProfileAsync(int userId, int id);
        Task<Result<bool>> DeleteStorageProfileAsync(int userId, int profileId);
        Task<UserStorageProfile> Add(UserStorageProfile userStorageProfile);
        Task<UserStorageProfile?> GetDefaultStorageProfileAsync(int userId);
        Task<UserStorageProfile?> GetProfileBySpecAsync(int userId, int profileId, bool activeOnly = true);
        Task<bool> HasDuplicateEmailAsync(int userId, int excludeProfileId, string email);
        Task<UserStorageProfile> AddWithoutSaveAsync(UserStorageProfile profile);
    }
}
