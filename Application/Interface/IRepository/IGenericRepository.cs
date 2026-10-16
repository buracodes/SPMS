using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Interface.IRepository
{
    public interface IGenericRepository<T> where T : class
    {
        Task<IReadOnlyList<T>> GetAll();
        Task<T?> GetById(Guid id);
        Task<bool> Exists(Guid id);
        Task Add(T entity);

        /// <summary>
        /// Stages the entity and defers the commit to <see cref="IUnitOfWork.SaveChangesAsync"/>.
        /// Use this when the new row and other pending changes must commit in one transaction, which
        /// <see cref="Add"/> cannot guarantee because it saves immediately.
        /// </summary>
        Task AddWithoutSave(T entity);

        Task AddRange(IEnumerable<T> entities);
        Task Update(T entity);
        Task UpdateRange(IEnumerable<T> entities);

        /// <summary>
        /// Deactivates the entity (sets <c>IsDeleted</c>) instead of removing it. The row and its
        /// audit history survive in the database; the global query filter hides it from all reads.
        /// This is the only supported way to delete a record in this system.
        /// </summary>
        Task Delete(T entity);
        IQueryable<T> GetQueryable();
    }
}
