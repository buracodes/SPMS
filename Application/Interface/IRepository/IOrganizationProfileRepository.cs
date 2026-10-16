using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IOrganizationProfileRepository : IGenericRepository<OrganizationProfile>
{
    Task<OrganizationProfile?> GetActiveProfileAsync();
    Task<OrganizationProfile?> GetFullProfileAsync();
    Task<List<SwotItem>> GetSwotItemsAsync();
}
