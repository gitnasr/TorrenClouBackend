using Microsoft.Extensions.Configuration;
using TorreClou.Core.DTOs.Auth;
using TorreClou.Core.Exceptions;
using TorreClou.Core.Interfaces;

namespace TorreClou.Application.Services
{
    public class AuthService(
        IConfiguration configuration,
        ITokenService tokenService,
        IUserService userService
        ) : IAuthService
    {
        public async Task<AuthResponseDto> LoginAsync(string email, string password)
        {
            var adminEmail = configuration["ADMIN_EMAIL"];
            var adminPassword = configuration["ADMIN_PASSWORD"];
            var adminName = configuration["ADMIN_NAME"] ?? "TorrenClou Admin";

            if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
                throw new BusinessRuleException("ServerConfigError", "Admin credentials not configured. Set ADMIN_EMAIL and ADMIN_PASSWORD in environment variables.");

            if (!email.Equals(adminEmail, StringComparison.OrdinalIgnoreCase) || password != adminPassword)
            {
                await Task.Delay(100); // Prevent timing attacks
                throw new UnauthorizedException("InvalidCredentials", "Invalid email or password");
            }

            var user = await userService.GetUserByEmailAsync(adminEmail);
            user ??= await userService.CreateUser(adminEmail, adminName);

            var token = tokenService.CreateToken(user);

            return new AuthResponseDto
            {
                AccessToken = token,
                Email = user.Email,
                FullName = user.FullName
            };
        }
    }
}
