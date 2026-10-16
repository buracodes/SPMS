using Application.CQRS.Goals.Handlers.Queries;
using Application.CQRS.Goals.Requests.Queries;
using Application.Mapping;
using Application.Tests.TestSupport;
using AutoMapper;
using Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Goals;

public class GetAllGoalRequestHandlerTests
{
    [Fact]
    public async Task Handle_WhenNoGoalsExist_ReturnsSuccessfulEmptyList()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var mapperConfiguration = new MapperConfiguration(
            configuration => configuration.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance);
        var handler = new GetAllGoalRequestHandler(
            new GoalRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            mapperConfiguration.CreateMapper());

        var response = await handler.Handle(new GetAllGoalRequest(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("Goals retrieved successfully.", response.Message);
        Assert.NotNull(response.Data);
        Assert.Empty(response.Data);
    }
}
