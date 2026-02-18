using TorreClou.Core.Entities;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services.OAuth
{
    public class OAuthService(IUnitOfWork unitOfWork) : IOAuthService
    {
        public async Task<UserOAuthCredential?> GetUserOAuthCredentialByClientId(string clientId, int userId)
        {
            var existingSpec = new BaseSpecification<UserOAuthCredential>(c => c.UserId == userId && c.ClientId == clientId);
            return await unitOfWork.Repository<UserOAuthCredential>().GetEntityWithSpec(existingSpec);

        }
        public async Task Update(UserOAuthCredential credential)
        {

            unitOfWork.Repository<UserOAuthCredential>().Update(credential);
            await unitOfWork.Complete();

        }

        public async Task<UserOAuthCredential> Add(UserOAuthCredential credential)
        {
            unitOfWork.Repository<UserOAuthCredential>().Add(credential);
            await unitOfWork.Complete();

            return credential;
        }

        public async Task<IReadOnlyCollection<UserOAuthCredential>> GetAll(int userId)
        {
            var spec = new BaseSpecification<UserOAuthCredential>(c => c.UserId == userId);
            var credentials = await unitOfWork.Repository<UserOAuthCredential>().ListAsync(spec);
            return credentials;



        }

        public Task<UserOAuthCredential?> GetUserOAuthCredentialById(int credId, int userId)
        {

            var existingSpec = new BaseSpecification<UserOAuthCredential>(c => c.UserId == userId && c.Id == credId);
            return unitOfWork.Repository<UserOAuthCredential>().GetEntityWithSpec(existingSpec);

        }
    }
}
