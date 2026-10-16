using Application.Interface.IService;
using Application.Services;
using Infrastructure.Persistence.Context;
using Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.TestSupport;

internal static class TestDbContextFactory
{
    public static async Task<TestDbContextLease> CreateAsync(
        ICurrentUserService? currentUserService = null,
        IAuditContext? auditContext = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateCollation("Latin1_General_100_CI_AS_SC", (x, y) =>
            string.Compare(x, y, StringComparison.OrdinalIgnoreCase));

        var interceptor = new AuditableEntitySaveChangesInterceptor(
            currentUserService ?? new TestCurrentUserService(),
            NullLogger<AuditableEntitySaveChangesInterceptor>.Instance,
            auditContext ?? new AuditContext());

        var options = new DbContextOptionsBuilder<SpmsDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        var context = new SpmsDbContext(options);
        await context.Database.EnsureCreatedAsync();

        return new TestDbContextLease(connection, context);
    }
}

internal sealed class TestDbContextLease : IAsyncDisposable
{
    public TestDbContextLease(SqliteConnection connection, SpmsDbContext context)
    {
        Connection = connection;
        Context = context;
    }

    public SqliteConnection Connection { get; }
    public SpmsDbContext Context { get; }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
