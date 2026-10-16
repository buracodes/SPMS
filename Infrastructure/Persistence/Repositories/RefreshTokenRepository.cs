using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly SpmsDbContext context;

        public RefreshTokenRepository(SpmsDbContext _context)
        {
            context = _context;
        }

        public async Task<RefreshToken?> GetByHashAsync(string tokenHash)
        {
            return await context.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);
        }

        public async Task AddAsync(RefreshToken token)
        {
            await context.RefreshTokens.AddAsync(token);
            await context.SaveChangesAsync();
        }

        public async Task RevokeAsync(RefreshToken token)
        {
            context.RefreshTokens.Update(token);
            await context.SaveChangesAsync();
        }

        public async Task<int> RevokeAllForUserAsync(Guid userId, string reason, DateTime asOf)
        {
            return await context.RefreshTokens
                .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null && rt.ExpiresAtUtc > asOf)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(rt => rt.RevokedAtUtc, asOf)
                    .SetProperty(rt => rt.ReasonRevoked, reason));
        }

        public async Task<int> DeleteExpiredAsync(DateTime asOf)
        {
            return await context.RefreshTokens
                .Where(rt => rt.RevokedAtUtc != null || rt.ExpiresAtUtc <= asOf)
                .ExecuteDeleteAsync();
        }
    }
}