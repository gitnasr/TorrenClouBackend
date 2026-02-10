using Microsoft.Extensions.Configuration;
using TorreClou.Core.DTOs.Auth;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly ITokenService _tokenService;
        private readonly IUnitOfWork _unitOfWork;

        public AuthService(
            IConfiguration configuration,
            ITokenService tokenService,
            IUnitOfWork unitOfWork)
        {
            _configuration = configuration;
            _tokenService = tokenService;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<AuthResponseDto>> LoginAsync(string email, string password)
        {
            // Get admin credentials from environment
            var adminEmail = _configuration["ADMIN_EMAIL"] ?? _configuration["Auth:AdminEmail"] ?? "admin@gitnasr.com";
            var adminPassword = _configuration["ADMIN_PASSWORD"] ?? _configuration["Auth:AdminPassword"] ?? "P@ssword123!";
            var adminName = _configuration["ADMIN_NAME"] ?? _configuration["Auth:AdminName"] ?? "Admin";

            if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
            {
                return Result<AuthResponseDto>.Failure(
                    ErrorCode.ServerConfigError,
                    "Admin credentials not configured. Set ADMIN_EMAIL and ADMIN_PASSWORD in environment variables.");
            }

            // Simple credential validation (case-insensitive email)
            if (!email.Equals(adminEmail, StringComparison.OrdinalIgnoreCase) || password != adminPassword)
            {
                await Task.Delay(100); // Prevent timing attacks
                return Result<AuthResponseDto>.Failure(ErrorCode.InvalidCredentials, "Invalid email or password");
            }

            // Get or create user in database
            var spec = new BaseSpecification<Core.Entities.User>(u => u.Email.ToLower() == adminEmail.ToLower());
            var user = await _unitOfWork.Repository<Core.Entities.User>().GetEntityWithSpec(spec);

            if (user == null)
            {
                user = new Core.Entities.User
                {
                    Email = adminEmail,
                    FullName = adminName
                };
                _unitOfWork.Repository<Core.Entities.User>().Add(user);
                await _unitOfWork.Complete();
            }

            var token = _tokenService.CreateToken(user);

            return Result.Success(new AuthResponseDto
            {
                AccessToken = token,
                Email = user.Email,
                FullName = user.FullName
            });
        }
    }
}
