using System.Collections;
using Microsoft.EntityFrameworkCore;
using TorreClou.Core.Entities;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Repositories;

namespace TorreClou.Infrastructure.Data
{
    public class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
    {
        private Hashtable _repositories;

        public async Task<int> Complete()
        {
            return await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            context.Dispose();
        }

        public IGenericRepository<T> Repository<T>() where T : BaseEntity
        {
            _repositories ??= new Hashtable();

            var type = typeof(T).Name;

            if (!_repositories.ContainsKey(type))
            {
                var repositoryType = typeof(GenericRepository<>);
                var repositoryInstance = Activator.CreateInstance(repositoryType.MakeGenericType(typeof(T)), context);

                _repositories.Add(type, repositoryInstance);
            }

            return (IGenericRepository<T>)_repositories[type];
        }

        public void Detach<T>(T entity) where T : BaseEntity
        {
            var entry = context.Entry(entity);
            if (entry != null)
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
