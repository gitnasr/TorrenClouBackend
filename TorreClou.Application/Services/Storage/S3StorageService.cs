using TorreClou.Application.Validators;
using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services.Storage
{
    public class S3StorageService(IUnitOfWork unitOfWork) : IS3StorageService
    {
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
            var profileNameValidation = StorageProfileValidator.ValidateProfileName(profileName);
            if (profileNameValidation.IsFailure)
                return Result<StorageProfileResultDto>.Failure(profileNameValidation.Error);

            if (string.IsNullOrWhiteSpace(s3Endpoint))
                return Result<StorageProfileResultDto>.Failure(ErrorCode.InvalidS3Config, "S3 endpoint is required");

            if (string.IsNullOrWhiteSpace(s3BucketName))
                return Result<StorageProfileResultDto>.Failure(ErrorCode.InvalidS3Config, "S3 bucket name is required");

            if (string.IsNullOrWhiteSpace(s3AccessKey))
                return Result<StorageProfileResultDto>.Failure(ErrorCode.InvalidS3Config, "S3 access key is required");

            if (string.IsNullOrWhiteSpace(s3SecretKey))
                return Result<StorageProfileResultDto>.Failure(ErrorCode.InvalidS3Config, "S3 secret key is required");

            if (string.IsNullOrWhiteSpace(s3Region))
                return Result<StorageProfileResultDto>.Failure(ErrorCode.InvalidS3Config, "S3 region is required");

            var credentialsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                endpoint = s3Endpoint,
                accessKey = s3AccessKey,
                secretKey = s3SecretKey,
                bucketName = s3BucketName,
                region = s3Region
            });

            if (setAsDefault)
            {
                var allProfilesSpec = new BaseSpecification<UserStorageProfile>(p => p.UserId == userId && p.IsActive);
                var allProfiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(allProfilesSpec);
                foreach (var p in allProfiles)
                {
                    p.IsDefault = false;
                }
            }

            var profile = new UserStorageProfile
            {
                UserId = userId,
                ProfileName = profileName,
                ProviderType = StorageProviderType.AwsS3,
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
    }
}
