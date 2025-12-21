using System.Threading.Tasks;
using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services.Storage
{
    public class StorageProfilesService(IUnitOfWork unitOfWork) : IStorageProfilesService
    {
        public async Task<Result<UserStorageProfile>> ValidateActiveStorageProfileByUserId(int userId, int profileId)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.Id == profileId && p.UserId == userId && p.IsActive
            );
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);
            if (profile == null)
            {
                return Result<UserStorageProfile>.Failure("PROFILE_NOT_FOUND", "Storage profile not found");
            }
            return Result.Success(profile);
        }
        public async Task<Result<List<StorageProfileDto>>> GetStorageProfilesAsync(int userId)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.UserId == userId && p.IsActive
            );

            var profiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(spec);

            var dtos = profiles
                .OrderBy(p => p.IsDefault ? 0 : 1)
                .ThenBy(p => p.CreatedAt)
                .Select(p => new StorageProfileDto
                {
                    Id = p.Id,
                    ProfileName = p.ProfileName,
                    ProviderType = p.ProviderType.ToString(),
                    Email = p.Email,
                    IsDefault = p.IsDefault,
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt
                }).ToList();

            return Result.Success(dtos);
        }

        public async Task<Result<StorageProfileDetailDto>> GetStorageProfileAsync(int userId, int id)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.Id == id && p.UserId == userId && p.IsActive
            );
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
            {
                return Result<StorageProfileDetailDto>.Failure("PROFILE_NOT_FOUND", "Storage profile not found");
            }

            var dto = new StorageProfileDetailDto
            {
                Id = profile.Id,
                ProfileName = profile.ProfileName,
                ProviderType = profile.ProviderType.ToString(),
                Email = profile.Email,
                IsDefault = profile.IsDefault,
                IsActive = profile.IsActive,
                CreatedAt = profile.CreatedAt,
                UpdatedAt = profile.UpdatedAt
            };

            return Result.Success(dto);
        }

        public async Task<Result> SetDefaultProfileAsync(int userId, int id)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.Id == id && p.UserId == userId && p.IsActive
            );
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
            {
                return Result.Failure("PROFILE_NOT_FOUND", "Storage profile not found");
            }

            // Unset all other default profiles for this user
            var allProfilesSpec = new BaseSpecification<UserStorageProfile>(
                p => p.UserId == userId && p.IsActive
            );
            var allProfiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(allProfilesSpec);

            foreach (var p in allProfiles)
            {
                p.IsDefault = false;
            }

            // Set this profile as default
            profile.IsDefault = true;
            await unitOfWork.Complete();

            return Result.Success();
        }

        public async Task<Result> DisconnectProfileAsync(int userId, int id)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.Id == id && p.UserId == userId
            );
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
            {
                return Result.Failure("PROFILE_NOT_FOUND", "Storage profile not found");
            }

            if (!profile.IsActive)
            {
                return Result.Failure("ALREADY_DISCONNECTED", "Storage profile is already disconnected");
            }

            // Set profile as inactive
            profile.IsActive = false;
            
            // If this was the default profile, unset it
            if (profile.IsDefault)
            {
                profile.IsDefault = false;
            }

            await unitOfWork.Complete();

            return Result.Success();
        }
    }
}
