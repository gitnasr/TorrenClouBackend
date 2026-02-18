using System.Text.Json;
using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Exceptions;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services.Storage
{
    public class StorageProfilesService(IUnitOfWork unitOfWork, IJobService jobService) : IStorageProfilesService
    {
        public async Task<List<StorageProfileDto>> GetStorageProfilesAsync(int userId)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.UserId == userId && p.IsActive
            );

            var profiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(spec);

            return profiles
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
                    NeedsReauth = p.NeedsReauth,
                    IsConfigured = IsProfileConfigured(p),
                    CreatedAt = p.CreatedAt
                }).ToList();
        }

        public async Task<StorageProfileDetailDto> GetStorageProfileAsync(int userId, int id)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.Id == id && p.UserId == userId && p.IsActive
            );
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
                throw new NotFoundException("ProfileNotFound", "Storage profile not found");

            return new StorageProfileDetailDto
            {
                Id = profile.Id,
                ProfileName = profile.ProfileName,
                ProviderType = profile.ProviderType.ToString(),
                Email = profile.Email,
                IsDefault = profile.IsDefault,
                IsActive = profile.IsActive,
                NeedsReauth = profile.NeedsReauth,
                IsConfigured = IsProfileConfigured(profile),
                CreatedAt = profile.CreatedAt,
                UpdatedAt = profile.UpdatedAt
            };
        }

        public async Task SetDefaultProfileAsync(int userId, int id)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.Id == id && p.UserId == userId && p.IsActive
            );
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
                throw new NotFoundException("ProfileNotFound", "Storage profile not found");

            var allProfilesSpec = new BaseSpecification<UserStorageProfile>(
                p => p.UserId == userId && p.IsActive
            );
            var allProfiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(allProfilesSpec);

            foreach (var p in allProfiles)
                p.IsDefault = false;

            profile.IsDefault = true;
            await unitOfWork.Complete();
        }

        public async Task DisconnectProfileAsync(int userId, int id)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.Id == id && p.UserId == userId
            );
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
                throw new NotFoundException("ProfileNotFound", "Storage profile not found");

            if (!profile.IsActive)
                throw new ConflictException("AlreadyDisconnected", "Storage profile is already disconnected");

            var activeJobs = await jobService.GetActiveJobsByStorageProfileIdAsync(profile.Id);

            if (activeJobs != null && activeJobs.Any())
                throw new BusinessRuleException("ProfileInUse", "Cannot disconnect profile while there are active jobs using it");

            profile.IsActive = false;

            if (profile.IsDefault)
                profile.IsDefault = false;

            await unitOfWork.Complete();
        }

        public async Task DeleteStorageProfileAsync(int userId, int profileId)
        {
            var spec = new BaseSpecification<UserStorageProfile>(p => p.Id == profileId && p.UserId == userId);
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
                throw new NotFoundException("ProfileNotFound", "Storage profile not found");

            var activeJobs = await jobService.GetActiveJobsByStorageProfileIdAsync(profileId);
            if (activeJobs != null && activeJobs.Any())
                throw new BusinessRuleException("ProfileInUse", "Cannot delete profile while there are active jobs using it");

            profile.IsActive = false;
            if (profile.IsDefault)
                profile.IsDefault = false;

            await unitOfWork.Complete();
        }

        public async Task<UserStorageProfile?> GetDefaultStorageProfileAsync(int userId)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.UserId == userId && p.IsActive && p.IsDefault
            );
            return await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);
        }

        public async Task<UserStorageProfile> Add(UserStorageProfile userStorageProfile)
        {
            unitOfWork.Repository<UserStorageProfile>().Add(userStorageProfile);
            await unitOfWork.Complete();
            return userStorageProfile;
        }

        private static bool IsProfileConfigured(UserStorageProfile profile)
        {
            if (profile.ProviderType != StorageProviderType.GoogleDrive)
                return true;

            try
            {
                var credentials = JsonSerializer.Deserialize<GoogleDriveCredentials>(profile.CredentialsJson);
                return credentials != null && !string.IsNullOrEmpty(credentials.RefreshToken);
            }
            catch
            {
                return false;
            }
        }

        public async Task<UserStorageProfile?> GetProfileBySpecAsync(int userId, int profileId, bool activeOnly = true)
        {
            var spec = activeOnly
                ? new BaseSpecification<UserStorageProfile>(p => p.Id == profileId && p.UserId == userId && p.IsActive)
                : new BaseSpecification<UserStorageProfile>(p => p.Id == profileId && p.UserId == userId);
            return await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);
        }

        public async Task<bool> HasDuplicateEmailAsync(int userId, int excludeProfileId, string email)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.UserId == userId
                    && p.Id != excludeProfileId
                    && p.ProviderType == StorageProviderType.GoogleDrive
                    && p.IsActive
                    && p.Email != null
                    && p.Email.ToLower() == email.ToLower()
            );
            var duplicate = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);
            return duplicate != null;
        }

        public Task<UserStorageProfile> AddWithoutSaveAsync(UserStorageProfile profile)
        {
            unitOfWork.Repository<UserStorageProfile>().Add(profile);
            return Task.FromResult(profile);
        }

        public async Task<bool> HasDefaultProfile(int userId)
        {
            var defaultProfileSpec = new BaseSpecification<UserStorageProfile>(
                p => p.UserId == userId && p.IsDefault && p.IsActive
            );
            return await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(defaultProfileSpec) != null;
        }

        public async Task Save()
        {
            await unitOfWork.Complete();
        }
    }
}
