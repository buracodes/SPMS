using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Persistence.Services;

/// <summary>
/// Read-only HR lookup backing the automatic baseline access. Only the active-employee query
/// and local division validations are issued; nothing is ever written to the HR database.
/// The HR result is cached briefly because it runs for every effective-permission evaluation;
/// explicit cross-division scopes are read live on every call so a revoked grant stops applying
/// on the next request.
/// </summary>
public class HrEmployeeAccessResolver : IEmployeeAccessResolver
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IHRInfoRepository hrInfoRepository;
    private readonly SpmsDbContext context;
    private readonly IMemoryCache cache;
    private readonly ILogger<HrEmployeeAccessResolver> logger;

    public HrEmployeeAccessResolver(
        IHRInfoRepository hrInfoRepository,
        SpmsDbContext context,
        IMemoryCache cache,
        ILogger<HrEmployeeAccessResolver> logger)
    {
        this.hrInfoRepository = hrInfoRepository;
        this.context = context;
        this.cache = cache;
        this.logger = logger;
    }

    public async Task<EmployeeAccessSnapshot> ResolveAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return EmployeeAccessSnapshot.None;
        }

        var cacheKey = $"hr-employee-access:{userId:N}";
        if (!cache.TryGetValue<HrBaseline>(cacheKey, out var baseline) || baseline is null)
        {
            try
            {
                var employee = await hrInfoRepository.FindActiveEmployeeByIdAsync(userId, cancellationToken);
                baseline = employee == null
                    ? new HrBaseline(false, null)
                    : new HrBaseline(true, await ResolveLocalDivisionAsync(employee.DivisionId, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "HR employee lookup failed for {UserId}; baseline access fails closed.", userId);
                return EmployeeAccessSnapshot.None;
            }

            cache.Set(cacheKey, baseline, CacheDuration);
        }

        // Explicit scopes are local authorization data, not HR data: resolve them live so
        // deactivating the row (or the division) takes effect immediately. A failure here
        // narrows the caller to the primary HR division rather than widening it.
        var explicitDivisionIds = await ResolveExplicitDivisionsAsync(userId, cancellationToken);
        return new EmployeeAccessSnapshot(baseline.IsActive, baseline.DivisionId, explicitDivisionIds);
    }

    /// <summary>
    /// The division must already exist locally through the HR synchronization; an HR node with
    /// no local row is not a usable scope, so the employee keeps baseline access but sees no
    /// division data until the next synchronization.
    /// </summary>
    private async Task<Guid?> ResolveLocalDivisionAsync(Guid? hrDivisionId, CancellationToken cancellationToken)
    {
        if (!hrDivisionId.HasValue || hrDivisionId.Value == Guid.Empty)
        {
            return null;
        }

        var divisionId = hrDivisionId.Value;
        var exists = await context.Divisions
            .AsNoTracking()
            .AnyAsync(
                d => d.Id == divisionId && d.IsActive && !d.IsDeleted && d.Status == EntityStatus.Active,
                cancellationToken);

        return exists ? divisionId : null;
    }

    /// <summary>
    /// Active explicit grants for the account, restricted to local active divisions. The
    /// soft-delete/inactive query filter on <see cref="Domain.Entities.UserDivisionScope"/>
    /// removes revoked rows, so a revocation needs no cache invalidation.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveExplicitDivisionsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var granted = await context.UserDivisionScopes
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .Select(s => s.DivisionId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (granted.Count == 0)
            {
                return Array.Empty<Guid>();
            }

            var active = await context.Divisions
                .AsNoTracking()
                .Where(d => granted.Contains(d.Id)
                            && d.IsActive
                            && !d.IsDeleted
                            && d.Status == EntityStatus.Active)
                .Select(d => d.Id)
                .ToListAsync(cancellationToken);

            return active.OrderBy(id => id).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Explicit division scope lookup failed for {UserId}; keeping the primary division only.", userId);
            return Array.Empty<Guid>();
        }
    }

    private sealed record HrBaseline(bool IsActive, Guid? DivisionId);
}
