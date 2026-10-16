using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class OrganizationProfileRepository : GenericRepository<OrganizationProfile>, IOrganizationProfileRepository
{
    public OrganizationProfileRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<OrganizationProfile?> GetActiveProfileAsync()
    {
        return await context.OrganizationProfiles
            .FirstOrDefaultAsync();
    }

    public async Task<OrganizationProfile?> GetFullProfileAsync()
    {
        return await context.OrganizationProfiles
            .Include(op => op.CoreValues.OrderBy(cv => cv.Title))
            .Include(op => op.StrategicThemes.OrderBy(st => st.Title))
            .Include(op => op.SwotItems.OrderBy(si => si.Category).ThenBy(si => si.Title))
            .Include(op => op.PestelItems.OrderBy(pi => pi.Category).ThenBy(pi => pi.Title))
            .FirstOrDefaultAsync();
    }

    public async Task<List<SwotItem>> GetSwotItemsAsync()
    {
        return await context.SwotItems
            .OrderBy(si => si.Category)
            .ThenBy(si => si.Title)
            .ToListAsync();
    }
}
