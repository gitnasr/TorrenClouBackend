using TorreClou.Core.Entities;
using System.Linq.Expressions;
using TorreClou.Core.Specifications;

namespace TorreClou.Core.Interfaces
{
    public interface IGenericRepository<T> where T : BaseEntity
    {
        Task<T?> GetByIdAsync(int id);
        Task<IReadOnlyList<T>> ListAllAsync();
        Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec);
        Task<T?> GetEntityWithSpec(ISpecification<T> spec);

        void Add(T entity);
        void Update(T entity);
        void Delete(T entity);

        // Count with condition
        Task<int> CountAsync(Expression<Func<T, bool>> criteria);
    }
}