using TorreClou.Core.DTOs.Auth;
using TorreClou.Core.Entities;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;
using TorreClou.Core.Shared;

namespace TorreClou.Application.Services
{
    public class AuthService(IUnitOfWork unitOfWork, ITokenService tokenService) : IAuthService
    {
        public async Task<Result<AuthResponseDto>> LoginWithGoogleAsync(GoogleLoginDto model)
        {
            var googlePayload = await tokenService.VerifyGoogleTokenAsync(model.IdToken);

            var spec = new BaseSpecification<User>(u => u.Email == googlePayload.Email);
            spec.AddInclude(u => u.WalletTransactions);

            var existingUser = await unitOfWork.Repository<User>().GetEntityWithSpec(spec);

            User user;

            if (existingUser == null)
            {
                user = new User
                {
                    Email = googlePayload.Email,
                    FullName = googlePayload.Name,
                    OAuthProvider = "Google",
                    OAuthSubjectId = googlePayload.Subject, 
                    IsPhoneNumberVerified = googlePayload.EmailVerified, 
                    Role = UserRole.User
                };

            

                unitOfWork.Repository<User>().Add(user);
                await unitOfWork.Complete(); // Save to generate ID
            }
            else
            {
                user = existingUser;

            }

            var token = tokenService.CreateToken(user);

            return Result.Success(new AuthResponseDto
            {
                AccessToken = token,
                Email = user.Email,
                FullName = user.FullName,
                CurrentBalance = user.GetCurrentBalance(),
                Role = user.Role.ToString()
            });
        }
    }
}
