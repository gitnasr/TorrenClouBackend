using TorreClou.Core.DTOs.Storage.Google_Drive;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IGoogleApiClient
    {
        Task<Result<TokenResponse>> ExchangeCodeForTokensAsync(string code, string clientId, string clientSecret, string redirectUri);
        Task<Result<UserInfoResponse>> GetUserInfoAsync(string accessToken);
    }
}
