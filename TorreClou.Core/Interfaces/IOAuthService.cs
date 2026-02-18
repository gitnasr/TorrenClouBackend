using TorreClou.Core.Entities;

namespace TorreClou.Application.Services.OAuth
{
    public interface IOAuthService
    {
        Task<UserOAuthCredential?> GetUserOAuthCredentialByClientId(string clientId, int userId);
        Task<UserOAuthCredential?> GetUserOAuthCredentialById(int credId, int userId);
        Task Update(UserOAuthCredential credential);
        Task<UserOAuthCredential> Add(UserOAuthCredential credential);
        Task<IReadOnlyCollection<UserOAuthCredential>> GetAll(int userId);
    }
}