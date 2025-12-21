using TorreClou.Core.Entities;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services
{

    public class UserService(IUnitOfWork unitOfWork) : IUserService
    {
        public Task<User?> GetActiveUserById(int userId)
        {

            var spec = new BaseSpecification<User>(u => u.Role == UserRole.User && u.Id == userId);

            return unitOfWork.Repository<User>()
                .GetEntityWithSpec(spec);

        }
    }
}