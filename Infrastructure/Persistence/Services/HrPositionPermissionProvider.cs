using Application.Interface.IService;
using Infrastructure.Options;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Persistence.Services;

/// <summary>
/// Reads the user's HR position levels from the external HR database and maps them to
/// permissions through <see cref="HrPositionAccessOptions"/>. The integration is opt-in:
/// with <see cref="HrPositionAccessOptions.Enabled"/> off it performs no I/O. Any HR
/// failure is logged and treated as "no position grants" so login never depends on HR.
/// </summary>
public class HrPositionPermissionProvider : IPositionPermissionProvider
{
    private static readonly IReadOnlySet<string> NoGrants = new HashSet<string>();

    private readonly hrDbContext hrContext;
    private readonly HrPositionAccessOptions options;
    private readonly IMemoryCache cache;
    private readonly ILogger<HrPositionPermissionProvider> logger;

    public HrPositionPermissionProvider(
        hrDbContext hrContext,
        IOptions<HrPositionAccessOptions> options,
        IMemoryCache cache,
        ILogger<HrPositionPermissionProvider> logger)
    {
        this.hrContext = hrContext;
        this.options = options.Value;
        this.cache = cache;
        this.logger = logger;
    }

    public async Task<IReadOnlySet<string>> GetGrantedPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || options.PositionLevelPermissions.Count == 0 || userId == Guid.Empty)
        {
            return NoGrants;
        }

        var cacheKey = $"hr-position-levels:{userId:N}";
        if (cache.TryGetValue<List<string>>(cacheKey, out var cachedLevels) && cachedLevels != null)
        {
            return PositionPermissionMapper.Map(cachedLevels, options.PositionLevelPermissions);
        }

        List<string> levels;
        try
        {
            levels = await hrContext.Database
                .SqlQueryRaw<string>(
                    @"SELECT DISTINCT ISNULL(pl.name, '') AS Value
                      FROM Employee e
                      INNER JOIN EmployeePosition ep ON ep.EmpId = e.EmpId
                      INNER JOIN Position p ON p.Id = ep.PositionId
                      INNER JOIN PositionalLevel pl ON pl.Id = p.PLevelId
                      WHERE e.EmpId = {0} AND ep.CurrentStatus = 1 AND ep.Status = 1 AND e.Status = 1",
                    userId)
                .ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "HR position lookup failed for {UserId}; falling back to no position grants.", userId);
            return NoGrants;
        }

        if (options.CacheMinutes > 0)
        {
            cache.Set(cacheKey, levels, TimeSpan.FromMinutes(options.CacheMinutes));
        }

        return PositionPermissionMapper.Map(levels, options.PositionLevelPermissions);
    }
}
