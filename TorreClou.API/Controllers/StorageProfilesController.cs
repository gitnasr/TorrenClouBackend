using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Options;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using TorreClou.Core.Entities.Jobs;
using System.Web;

namespace TorreClou.API.Controllers
{
    [Route("api/storage")]
    [Authorize]
    public class StorageProfilesController(
        IUnitOfWork unitOfWork
        ) : BaseApiController
    {


        [HttpGet("profiles")]
        public async Task<IActionResult> GetStorageProfiles()
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.UserId == UserId && p.IsActive
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

            return Ok(dtos);
        }

        [HttpGet("profiles/{id}")]
        public async Task<IActionResult> GetStorageProfile(int id)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.Id == id && p.UserId == UserId && p.IsActive
            );
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
            {
                return HandleResult(Result<StorageProfileDetailDto>.Failure("PROFILE_NOT_FOUND", "Storage profile not found"));
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

            return Ok(dto);
        }

        [HttpPost("profiles/{id}/set-default")]
        public async Task<IActionResult> SetDefaultProfile(int id)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.Id == id && p.UserId == UserId && p.IsActive
            );
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
            {
                return HandleResult(Result.Failure("PROFILE_NOT_FOUND", "Storage profile not found"));
            }

            // Unset all other default profiles for this user
            var allProfilesSpec = new BaseSpecification<UserStorageProfile>(
                p => p.UserId == UserId && p.IsActive
            );
            var allProfiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(allProfilesSpec);

            foreach (var p in allProfiles)
            {
                p.IsDefault = false;
            }

            // Set this profile as default
            profile.IsDefault = true;
            await unitOfWork.Complete();

            return HandleResult(Result.Success());
        }

        [HttpPost("profiles/{id}/disconnect")]
        public async Task<IActionResult> DisconnectProfile(int id)
        {
            var spec = new BaseSpecification<UserStorageProfile>(
                p => p.Id == id && p.UserId == UserId
            );
            var profile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(spec);

            if (profile == null)
            {
                return HandleResult(Result.Failure("PROFILE_NOT_FOUND", "Storage profile not found"));
            }

            if (!profile.IsActive)
            {
                return HandleResult(Result.Failure("ALREADY_DISCONNECTED", "Storage profile is already disconnected"));
            }

            // Set profile as inactive
            profile.IsActive = false;
            
            // If this was the default profile, unset it
            if (profile.IsDefault)
            {
                profile.IsDefault = false;
            }

            await unitOfWork.Complete();

            return HandleResult(Result.Success());
        }
    }
}

