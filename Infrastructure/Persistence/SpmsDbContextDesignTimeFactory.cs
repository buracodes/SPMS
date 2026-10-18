using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence;

/// <summary>
/// Design-time factory for `dotnet ef migrations add/list/script`. Used when the API
/// host cannot be built (e.g. a debug session holds the file locks); the connection
/// string mirrors API/appsettings.json so editing and deployment always target the
/// same (localdb) environment.
/// </summary>
public sealed class SpmsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SpmsDbContext>
{
    public SpmsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpmsDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SPMS;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        return new SpmsDbContext(options);
    }
}
