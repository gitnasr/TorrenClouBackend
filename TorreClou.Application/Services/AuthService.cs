using Microsoft.Extensions.Configuration;
using TorreClou.Core.DTOs.Auth;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly ITokenService _tokenService;

        public AuthService(
            IConfiguration configuration,
            ITokenService tokenService)
        {
            _configuration = configuration;
            _tokenService = tokenService;
        }

        public async Task<Result<AuthResponseDto>> LoginAsync(string email, string password)
        {
            // Get admin credentials from environment
            var adminEmail = _configuration["ADMIN_EMAIL"] ?? _configuration["Auth:AdminEmail"];
            var adminPassword = _configuration["ADMIN_PASSWORD"] ?? _configuration["Auth:AdminPassword"];
            var adminName = _configuration["ADMIN_NAME"] ?? _configuration["Auth:AdminName"] ?? "Admin";

            if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
            {
                return Result<AuthResponseDto>.Failure(
                    "SERVER_CONFIG_ERROR",
                    "Admin credentials not configured. Set ADMIN_EMAIL and ADMIN_PASSWORD in environment variables.");
            }

            // Simple credential validation (case-insensitive email)
            if (!email.Equals(adminEmail, StringComparison.OrdinalIgnoreCase) || password != adminPassword)
            {
                await Task.Delay(100); // Prevent timing attacks
                return Result<AuthResponseDto>.Failure("INVALID_CREDENTIALS", "Invalid email or password");
            }

            // Create a dummy user object for token generation
            var user = new Core.Entities.User
            {
                Id = 1,
                Email = adminEmail,
                FullName = adminName
            };

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
