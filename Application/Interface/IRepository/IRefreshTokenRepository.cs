using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Interface.IRepository
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByHashAsync(string tokenHash);
        Task AddAsync(RefreshToken token);
        Task RevokeAsync(RefreshToken token);
        Task<int> RevokeAllForUserAsync(Guid userId, string reason, DateTime asOf);
        Task<int> DeleteExpiredAsync(DateTime asOf);
    }
}