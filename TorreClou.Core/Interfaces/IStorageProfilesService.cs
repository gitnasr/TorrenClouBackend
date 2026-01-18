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
    }
}







