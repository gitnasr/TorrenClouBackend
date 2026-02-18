using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.Entities.Jobs;

namespace TorreClou.Core.Interfaces
{
    public interface IStorageProfilesService
    {
        Task<List<StorageProfileDto>> GetStorageProfilesAsync(int userId);
        Task<StorageProfileDetailDto> GetStorageProfileAsync(int userId, int id);
        Task SetDefaultProfileAsync(int userId, int id);
        Task<bool> HasDefaultProfile(int userId);
        Task Save();
        Task DisconnectProfileAsync(int userId, int id);
        Task DeleteStorageProfileAsync(int userId, int profileId);
        Task<UserStorageProfile> Add(UserStorageProfile userStorageProfile);
        Task<UserStorageProfile?> GetDefaultStorageProfileAsync(int userId);
        Task<UserStorageProfile?> GetProfileBySpecAsync(int userId, int profileId, bool activeOnly = true);
        Task<bool> HasDuplicateEmailAsync(int userId, int excludeProfileId, string email);
        Task<UserStorageProfile> AddWithoutSaveAsync(UserStorageProfile profile);
    }
}
