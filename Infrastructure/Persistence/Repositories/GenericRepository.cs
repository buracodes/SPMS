using Application.Interface.IRepository;
using Domain.Common;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Repositories
{
    public class GenericRepository<T> : IGenericRepository<T> where T : CommonEntity
    {
        public readonly SpmsDbContext context;
        public GenericRepository(SpmsDbContext _context)
        {
            context = _context;
        }
        public async Task<IReadOnlyList<T>> GetAll()
        {
            return await context.Set<T>()
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<T?> GetById(Guid id)
        {
            return await context.Set<T>()
                .FirstOrDefaultAsync(e => e.Id == id);
        }

        public async Task<bool> Exists(Guid id)
        {
            var entity = await GetById(id);
            return entity != null;
        }

        public async Task Add(T entity)
        {
            await context.Set<T>().AddAsync(entity);
            await context.SaveChangesAsync();
        }

        public async Task AddRange(IEnumerable<T> entities)
        {
            await context.Set<T>().AddRangeAsync(entities);
            await context.SaveChangesAsync();
        }

        public async Task AddWithoutSave(T entity)
        {
            await context.Set<T>().AddAsync(entity);
        }

        public async Task Update(T entity)
        {
            //context.Entry(entity).State = EntityState.Modified;
            context.Set<T>().Update(entity);
            await context.SaveChangesAsync();
        }
        public async Task UpdateRange(IEnumerable<T> entities)
        {
            context.Set<T>().UpdateRange(entities);
            await context.SaveChangesAsync();
        }
        public async Task Delete(T entity)
        {
            // System-wide delete convention: a delete never removes the row. It deactivates the
            // record - the shared IsDeleted flag plus, where the entity has one, the generic
            // EntityStatus lifecycle flag moving from Active (1) to Inactive (0). SpmsDbContext's
            // global query filter hides both from every read, so the record leaves the UI while the
            // row and its audit history survive in the database.
            entity.IsDeleted = true;
            SetInactiveStatus(entity);
            entity.UpdatedDate = DateTime.UtcNow;

            context.Set<T>().Update(entity);
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Flips <see cref="CommonEntity.Status"/> to <see cref="EntityStatus.Inactive"/> where that
        /// property really is the lifecycle flag. Initiative and Milestone shadow it with
        /// <c>new InitiativeStatus Status</c> / <c>new MilestoneStatus Status</c>, because for those
        /// "status" means the execution state (Planned/InProgress/Completed) rather than the generic
        /// lifecycle flag, and 0 is not a valid value there. Those entities are deactivated through
        /// IsDeleted alone, which leaves their workflow status intact.
        /// </summary>
        private static void SetInactiveStatus(T entity)
        {
            // DeclaredOnly avoids an AmbiguousMatchException: Initiative and Milestone each have two
            // Status properties (the base EntityStatus one plus their own execution enum), so an
            // unrestricted GetProperty("Status") throws for exactly the entities we must skip.
            var shadowing = entity.GetType().GetProperty(
                nameof(CommonEntity.Status),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (shadowing is not null && shadowing.PropertyType != typeof(EntityStatus))
            {
                return;
            }

            ((CommonEntity)entity).Status = EntityStatus.Inactive;
        }

        public IQueryable<T> GetQueryable()
        {
            return context.Set<T>().AsNoTracking();
        }
    }
}
