using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services.Storage
{
    public class StorageProfilesService(IUnitOfWork unitOfWork, IJobService jobService) : IStorageProfilesService
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


            // Check if there are any active jobs using this profile
            var activeJobs = await jobService.GetActiveJobsByStorageProfileIdAsync(profile.Id);
            if (activeJobs.IsFailure)
            {
                return Result.Failure(activeJobs.Error.Code, activeJobs.Error.Message);
            }

            if (activeJobs.Value != null && activeJobs.Value.Any())
            {
                return Result.Failure("PROFILE_IN_USE", "Cannot disconnect profile while there are active jobs using it");
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

        public async Task<Result<StorageProfileResultDto>> ConfigureS3StorageAsync(
            int userId,
            string profileName,
            string s3Endpoint,
            string s3AccessKey,
            string s3SecretKey,
            string s3BucketName,
            string s3Region,
            bool setAsDefault)
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(profileName))
                return Result<StorageProfileResultDto>.Failure("INVALID_PROFILE_NAME", "Profile name is required");
            
            if (string.IsNullOrWhiteSpace(s3Endpoint) || string.IsNullOrWhiteSpace(s3BucketName))
                return Result<StorageProfileResultDto>.Failure("INVALID_S3_CONFIG", "S3 endpoint and bucket name are required");

            // Create credentials JSON (should be encrypted in production)
            var credentialsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                endpoint = s3Endpoint,
                accessKey = s3AccessKey,
                secretKey = s3SecretKey,
                bucketName = s3BucketName,
                region = s3Region
            });

            // If setting as default, unset other defaults
            if (setAsDefault)
            {
                var allProfilesSpec = new BaseSpecification<UserStorageProfile>(p => p.UserId == userId && p.IsActive);
                var allProfiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(allProfilesSpec);
                foreach (var p in allProfiles)
                {
                    p.IsDefault = false;
                }
            }

            // Create new storage profile
            var profile = new UserStorageProfile
            {
                UserId = userId,
                ProfileName = profileName,
                ProviderType = Core.Enums.StorageProviderType.AwsS3,
                CredentialsJson = credentialsJson,
                IsDefault = setAsDefault,
                IsActive = true
            };

            unitOfWork.Repository<UserStorageProfile>().Add(profile);
            await unitOfWork.Complete();

            return Result.Success(new StorageProfileResultDto
            {
                Success = true,
                StorageProfileId = profile.Id,
                Message = "S3 storage configured successfully"
            });
        }

        public async Task<Result<StorageProvidersListDto>> GetUserStorageProvidersAsync(int userId)
        {
            var spec = new BaseSpecification<UserStorageProfile>(p => p.UserId == userId && p.IsActive);
            var profiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(spec);

            var providers = profiles
                .OrderBy(p => p.IsDefault ? 0 : 1)
                .ThenBy(p => p.CreatedAt)
                .Select(p =>
                {
                    var dto = new StorageProviderDto
                    {
                        Id = p.Id,
                        ProfileName = p.ProfileName,
                        ProviderType = p.ProviderType.ToString(),
                        IsDefault = p.IsDefault,
                        IsActive = p.IsActive,
                        CreatedAt = p.CreatedAt
                    };

                    // Parse S3 credentials if it's an S3 provider
                    if (p.ProviderType == Core.Enums.StorageProviderType.AwsS3 && !string.IsNullOrEmpty(p.CredentialsJson))
                    {
                        try
                        {
                            var creds = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(p.CredentialsJson);
                            dto = dto with
                            {
                                S3Endpoint = creds.GetProperty("endpoint").GetString(),
                                S3BucketName = creds.GetProperty("bucketName").GetString()
                            };
                        }
                        catch
                        {
                            // Ignore deserialization errors
                        }
                    }

                    return dto;
                }).ToList();

            return Result.Success(new StorageProvidersListDto { Providers = providers });
        }

        public async Task<Result<bool>> DeleteStorageProfileAsync(int userId, int profileId)
        {
            var spec = new BaseSpecification<UserStorageProfile>(p => p.Id == profileId && p.UserId == userId);
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
                return Result<bool>.Failure("PROFILE_NOT_FOUND", "Storage profile not found");

            // Check for active jobs
            var activeJobs = await jobService.GetActiveJobsByStorageProfileIdAsync(profileId);
            if (activeJobs.IsSuccess && activeJobs.Value != null && activeJobs.Value.Any())
                return Result<bool>.Failure("PROFILE_IN_USE", "Cannot delete profile while there are active jobs using it");

            // Soft delete by setting IsActive = false
            profile.IsActive = false;
            if (profile.IsDefault)
                profile.IsDefault = false;

            await unitOfWork.Complete();

            return Result.Success(true);
        }
    }
}
