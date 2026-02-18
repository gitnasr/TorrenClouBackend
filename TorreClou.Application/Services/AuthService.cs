using Microsoft.Extensions.Configuration;
using TorreClou.Core.DTOs.Auth;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.Application.Services
{
    public class AuthService(
        IConfiguration configuration,
        ITokenService tokenService,
        IUserService userService
        ) : IAuthService
    {
        public async Task<Result<AuthResponseDto>> LoginAsync(string email, string password)
        {
            // Get admin credentials from environment variables
            var adminEmail = configuration["ADMIN_EMAIL"];
            var adminPassword = configuration["ADMIN_PASSWORD"];
            var adminName = configuration["ADMIN_NAME"] ?? "TorrenClou Admin";

            if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
            {
                return Result<AuthResponseDto>.Failure(
                    ErrorCode.ServerConfigError,
                    "Admin credentials not configured. Set ADMIN_EMAIL and ADMIN_PASSWORD in environment variables.");
            }

            if (!email.Equals(adminEmail, StringComparison.OrdinalIgnoreCase) || password != adminPassword)
            {
                await Task.Delay(100); // Prevent timing attacks
                return Result<AuthResponseDto>.Failure(ErrorCode.InvalidCredentials, "Invalid email or password");
            }

            var user = await userService.GetUserByEmailAsync(adminEmail);
            user ??= await userService.CreateUser(adminEmail, adminName);

            var token = tokenService.CreateToken(user);

            return Result.Success(new AuthResponseDto
            {
                AccessToken = token,
                Email = user.Email,
                FullName = user.FullName
            });
        }
    }
}
