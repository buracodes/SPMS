using Application.CQRS.Kpi.Handlers.Commands;
using Application.CQRS.Kpi.Requests.Commands;
using Application.DTOS.AccessControl;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Tests.TestSupport;
using Application.Validation.Kpi;
using Domain.Entities;
using Domain.Enums;
using FluentValidation.TestHelper;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class KpiOwnerAssignmentTests
{
    private sealed record Hierarchy(
        FiscalYear Year,
        Scorecard Scorecard,
        Goal Goal,
        Kra Kra,
        Division ActiveDivision,
        Division InactiveDivision,
        EmployeeHRInfoDto Employee);

    private static Hierarchy Seed(TestDbContextLease lease)
    {
        var year = new FiscalYear
        {
            Id = Guid.NewGuid(),
            Label = "FY2026",
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2027, 6, 30)
        };
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Corporate 2026",
            Scope = ScorecardScope.Corporate
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow volume", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Win mandates", Weight = 100 };
        var activeDivision = new Division { Id = Guid.NewGuid(), Name = "Trading", Code = "TRD" };
        var inactiveDivision = new Division { Id = Guid.NewGuid(), Name = "Retired", Code = "RET", IsActive = false };
        var employee = new EmployeeHRInfoDto
        {
            EmpId = Guid.NewGuid(),
            IdCode = "EMP-1001",
            FirstName = "Dereje",
            MiddleName = "Kebede",
            LastName = "Girma",
            Email = "dereje.kebede@ecx.com.et",
            PositionName = "Planner",
            DivisionId = activeDivision.Id,
            DivisionName = activeDivision.Name
        };

        lease.Context.FiscalYears.Add(year);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Divisions.AddRange(activeDivision, inactiveDivision);
        lease.Context.SaveChanges();

        return new Hierarchy(year, scorecard, goal, kra, activeDivision, inactiveDivision, employee);
    }

    private static CreateKpiCommandRequest CreateRequest(
        Hierarchy hierarchy,
        string name,
        decimal weight,
        Guid? ownerId = null,
        Guid? divisionOwnerId = null)
        => new()
        {
            ScorecardId = hierarchy.Scorecard.Id,
            KraId = hierarchy.Kra.Id,
            OwnerId = ownerId,
            DivisionOwnerId = divisionOwnerId,
            Name = name,
            Weight = weight,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging,
            OptimizationType = KpiOptimizationType.Maximize
        };

    private static CreateKpiCommandHandler CreateHandler(
        TestDbContextLease lease,
        IHRInfoRepository? hrRepository = null)
        => new(
            new KpiRepository(lease.Context),
            new KraRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            new UserRepository(lease.Context),
            hrRepository ?? new TestHrInfoRepository(),
            new TestCurrentUserService { UserId = Guid.NewGuid() });

    private static UpdateKpiCommandHandler UpdateHandler(
        TestDbContextLease lease,
        IHRInfoRepository? hrRepository = null)
        => new(
            new KpiRepository(lease.Context),
            new KraRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context),
            new UserRepository(lease.Context),
            hrRepository ?? new TestHrInfoRepository(),
            new TestCurrentUserService { UserId = Guid.NewGuid() });

    // ---------- Validators ----------

    [Fact]
    public void CreateValidator_RequiresExactlyOneOwner()
    {
        var validator = new CreateKpiValidator();
        var noOwner = CreateRequestFor(Guid.NewGuid());
        var bothOwners = CreateRequestFor(Guid.NewGuid());
        bothOwners.OwnerId = Guid.NewGuid();
        bothOwners.DivisionOwnerId = Guid.NewGuid();

        validator.TestValidate(noOwner).ShouldHaveValidationErrorFor(x => x);
        validator.TestValidate(bothOwners).ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void CreateValidator_AcceptsIndividualOwner()
    {
        var request = CreateRequestFor(Guid.NewGuid());
        request.OwnerId = Guid.NewGuid();

        new CreateKpiValidator().TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateValidator_AcceptsDivisionOwner()
    {
        var request = CreateRequestFor(Guid.NewGuid());
        request.DivisionOwnerId = Guid.NewGuid();

        new CreateKpiValidator().TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateValidator_RejectsBothOwners()
    {
        var request = new UpdateKpiCommandRequest
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            DivisionOwnerId = Guid.NewGuid()
        };

        new UpdateKpiValidator().TestValidate(request).ShouldHaveValidationErrorFor(x => x);
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_WithoutOwner_IsRejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease).Handle(CreateRequest(hierarchy, "Unowned", 50m), CancellationToken.None));

        Assert.Contains("exactly one owner", ex.Message);
    }

    [Fact]
    public async Task Create_WithBothOwners_IsRejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);
        var request = CreateRequest(hierarchy, "Ambiguous", 50m, hierarchy.Employee.EmpId, hierarchy.ActiveDivision.Id);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease, new TestHrInfoRepository([hierarchy.Employee]))
                .Handle(request, CancellationToken.None));

        Assert.Contains("exactly one owner", ex.Message);
    }

    [Fact]
    public async Task Create_IndividualOwner_ProvisionsOneLocalAccountAndClearsDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);
        var handler = CreateHandler(lease, new TestHrInfoRepository([hierarchy.Employee]));

        var first = await handler.Handle(
            CreateRequest(hierarchy, "First", 40m, ownerId: hierarchy.Employee.EmpId),
            CancellationToken.None);
        var second = await handler.Handle(
            CreateRequest(hierarchy, "Second", 40m, ownerId: hierarchy.Employee.EmpId),
            CancellationToken.None);

        Assert.Equal(hierarchy.Employee.EmpId, first.Data!.OwnerId);
        Assert.Null(first.Data.DivisionOwnerId);
        Assert.Equal("Dereje Kebede", first.Data.OwnerName);

        var saved = await lease.Context.Kpis.AsNoTracking().SingleAsync(kpi => kpi.Id == second.Data!.Id);
        Assert.Equal(hierarchy.Employee.EmpId, saved.OwnerId);
        Assert.Null(saved.DivisionOwnerId);

        var localUsers = await lease.Context.Users
            .Where(user => user.Id == hierarchy.Employee.EmpId)
            .ToListAsync();
        Assert.Single(localUsers);
    }

    [Fact]
    public async Task Create_IndividualOwner_RejectsEmployeeMissingFromActiveHr()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);

        // The local row exists, but HR says the employee is no longer active.
        lease.Context.Users.Add(new User
        {
            Id = hierarchy.Employee.EmpId!.Value,
            Username = "dereje.kebede",
            FullName = "Dereje Kebede Girma",
            IsActive = true
        });
        await lease.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease).Handle(
                CreateRequest(hierarchy, "Orphaned", 50m, ownerId: hierarchy.Employee.EmpId),
                CancellationToken.None));

        Assert.Contains("not an active HR employee", ex.Message);
        Assert.False(await lease.Context.Kpis.AnyAsync(kpi => kpi.Name == "Orphaned"));
    }

    [Fact]
    public async Task Create_IndividualOwner_RejectsInactiveLocalAccount()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);
        lease.Context.Users.Add(new User
        {
            Id = hierarchy.Employee.EmpId!.Value,
            Username = "dereje.kebede",
            FullName = "Dereje Kebede Girma",
            IsActive = false
        });
        await lease.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease, new TestHrInfoRepository([hierarchy.Employee]))
                .Handle(CreateRequest(hierarchy, "Inactive", 50m, ownerId: hierarchy.Employee.EmpId), CancellationToken.None));

        Assert.Contains("inactive", ex.Message);
    }

    [Fact]
    public async Task Create_DivisionOwner_SavesLocalDivisionAndClearsIndividual()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);

        var response = await CreateHandler(lease).Handle(
            CreateRequest(hierarchy, "Division owned", 50m, divisionOwnerId: hierarchy.ActiveDivision.Id),
            CancellationToken.None);

        Assert.Null(response.Data!.OwnerId);
        Assert.Equal(hierarchy.ActiveDivision.Id, response.Data.DivisionOwnerId);
        Assert.Equal(hierarchy.ActiveDivision.Name, response.Data.DivisionOwnerName);
    }

    [Fact]
    public async Task Create_DivisionOwner_RejectsUnknownOrInactiveDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateHandler(lease).Handle(
                CreateRequest(hierarchy, "Ghost", 30m, divisionOwnerId: Guid.NewGuid()),
                CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateHandler(lease).Handle(
                CreateRequest(hierarchy, "Retired", 30m, divisionOwnerId: hierarchy.InactiveDivision.Id),
                CancellationToken.None));
    }

    // ---------- Update ----------

    [Fact]
    public async Task Update_SwitchesDivisionOwnerToIndividual()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);
        var created = await CreateHandler(lease).Handle(
            CreateRequest(hierarchy, "Owned by division", 40m, divisionOwnerId: hierarchy.ActiveDivision.Id),
            CancellationToken.None);

        var response = await UpdateHandler(lease, new TestHrInfoRepository([hierarchy.Employee]))
            .Handle(new UpdateKpiCommandRequest
            {
                Id = created.Data!.Id,
                OwnerId = hierarchy.Employee.EmpId
            }, CancellationToken.None);

        Assert.Equal(hierarchy.Employee.EmpId, response.Data!.OwnerId);
        Assert.Null(response.Data.DivisionOwnerId);
        Assert.Equal("Dereje Kebede", response.Data.OwnerName);

        var saved = await lease.Context.Kpis.AsNoTracking().SingleAsync(kpi => kpi.Id == created.Data.Id);
        Assert.Equal(hierarchy.Employee.EmpId, saved.OwnerId);
        Assert.Null(saved.DivisionOwnerId);
    }

    [Fact]
    public async Task Update_SwitchesIndividualOwnerToDivision()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);
        var created = await CreateHandler(lease, new TestHrInfoRepository([hierarchy.Employee]))
            .Handle(CreateRequest(hierarchy, "Owned by employee", 40m, ownerId: hierarchy.Employee.EmpId), CancellationToken.None);

        var response = await UpdateHandler(lease).Handle(new UpdateKpiCommandRequest
        {
            Id = created.Data!.Id,
            DivisionOwnerId = hierarchy.ActiveDivision.Id
        }, CancellationToken.None);

        Assert.Null(response.Data!.OwnerId);
        Assert.Equal(hierarchy.ActiveDivision.Id, response.Data.DivisionOwnerId);

        var saved = await lease.Context.Kpis.AsNoTracking().SingleAsync(kpi => kpi.Id == created.Data.Id);
        Assert.Null(saved.OwnerId);
        Assert.Equal(hierarchy.ActiveDivision.Id, saved.DivisionOwnerId);
    }

    [Fact]
    public async Task Update_KeepsExistingOwnerWhenNeitherIsSupplied()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);
        var created = await CreateHandler(lease).Handle(
            CreateRequest(hierarchy, "Owned by division", 40m, divisionOwnerId: hierarchy.ActiveDivision.Id),
            CancellationToken.None);

        var response = await UpdateHandler(lease).Handle(new UpdateKpiCommandRequest
        {
            Id = created.Data!.Id,
            Name = "Renamed"
        }, CancellationToken.None);

        Assert.Equal("Renamed", response.Data!.Name);
        Assert.Equal(hierarchy.ActiveDivision.Id, response.Data.DivisionOwnerId);
        Assert.Null(response.Data.OwnerId);
    }

    [Fact]
    public async Task Update_RejectsBothOwners()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);
        var created = await CreateHandler(lease).Handle(
            CreateRequest(hierarchy, "Owned by division", 40m, divisionOwnerId: hierarchy.ActiveDivision.Id),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            UpdateHandler(lease).Handle(new UpdateKpiCommandRequest
            {
                Id = created.Data!.Id,
                OwnerId = Guid.NewGuid(),
                DivisionOwnerId = hierarchy.ActiveDivision.Id
            }, CancellationToken.None));

        Assert.Contains("not both", ex.Message);
    }

    [Fact]
    public async Task Update_RejectsLegacyUnownedKpi()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = Seed(lease);
        var legacy = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = hierarchy.Scorecard.Id,
            KraId = hierarchy.Kra.Id,
            Name = "Legacy unowned",
            Weight = 100
        };
        lease.Context.Kpis.Add(legacy);
        await lease.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            UpdateHandler(lease).Handle(new UpdateKpiCommandRequest
            {
                Id = legacy.Id,
                Name = "Still unowned"
            }, CancellationToken.None));

        Assert.Contains("exactly one owner", ex.Message);
    }

    private static CreateKpiCommandRequest CreateRequestFor(Guid kraId)
        => new()
        {
            ScorecardId = Guid.NewGuid(),
            KraId = kraId,
            Name = "KPI",
            Weight = 50m
        };
}
