using Application.CQRS.Kpi.Requests.Commands;
using Application.CQRS.Kpi.Handlers.Commands;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Tests.TestSupport;
using Application.Validation.Kpi;
using Domain.Entities;
using Domain.Enums;
using FluentValidation.TestHelper;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Kpis;

public class KpiIntegrityRuleTests
{
    private static (FiscalYear Year, Scorecard Scorecard, Goal Goal, Kra Kra) SeedHierarchy(TestDbContextLease lease)
    {
        var year = new FiscalYear { Id = Guid.NewGuid(), Label = "FY2026", StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2027, 6, 30) };
        var scorecard = new Scorecard { Id = Guid.NewGuid(), FiscalYearId = year.Id, Name = "Corporate 2026", Scope = ScorecardScope.Corporate };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Grow volume", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "Win mandates", Weight = 100 };

        lease.Context.FiscalYears.Add(year);
        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.SaveChanges();

        return (year, scorecard, goal, kra);
    }

    private static CreateKpiCommandHandler CreateHandler(TestDbContextLease lease, IKpiRepository? kpiRepository = null)
        => new(
            kpiRepository ?? new KpiRepository(lease.Context),
            new KraRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));

    private static CreateKpiCommandRequest CreateRequest(Guid scorecardId, Guid? kraId, string name, decimal weight)
        => new()
        {
            ScorecardId = scorecardId,
            KraId = kraId,
            Name = name,
            Weight = weight,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging,
            OptimizationType = KpiOptimizationType.Maximize
        };

    // ---------- Validators (shape rules) ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void CreateValidator_RejectsWeightOutsideZeroToHundred(decimal weight)
    {
        var validator = new CreateKpiValidator();
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "KPI", weight);
        validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.Weight);
    }

    [Fact]
    public void CreateValidator_AcceptsExactly100()
    {
        var validator = new CreateKpiValidator();
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "KPI", 100m);
        validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateValidator_AcceptsFractionalWeight()
    {
        var validator = new CreateKpiValidator();
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "KPI", 0.5m);
        validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateValidator_RejectsBothParents()
    {
        var validator = new CreateKpiValidator();
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "KPI", 50m);
        request.DivisionGoalId = Guid.NewGuid();
        Assert.False(validator.TestValidate(request).IsValid);
    }

    [Fact]
    public void CreateValidator_RejectsNoParent()
    {
        var validator = new CreateKpiValidator();
        var request = CreateRequest(Guid.NewGuid(), null, "KPI", 50m);
        Assert.False(validator.TestValidate(request).IsValid);
    }

    [Fact]
    public void UpdateValidator_RejectsNonPositiveWeight()
    {
        var validator = new UpdateKpiValidator();
        var request = new UpdateKpiCommandRequest { Id = Guid.NewGuid(), Weight = 0m };
        validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.Weight);
    }

    // ---------- Create handler (budget, duplicates, parents) ----------

    [Fact]
    public async Task Create_WeightExceeds100_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        await CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "First", 60m), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Second", 50m), CancellationToken.None));
        Assert.Contains("cannot exceed 100%", ex.Message);
    }

    [Fact]
    public async Task Create_FillsExactly100_Succeeds()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        await CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "First", 40m), CancellationToken.None);
        var response = await CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Second", 60m), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(60m, response.Data!.Weight);
    }

    [Fact]
    public async Task Create_FirstKpiAt100_SecondKpiRejected()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        await CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "First", 100m), CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Second", 0.5m), CancellationToken.None));
    }

    [Fact]
    public async Task Create_FirstKpiWithoutExplicitWeight_Defaults100()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);

        var handler = CreateHandler(lease);
        var request = CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Sole KPI", 0m);
        request.Weight = 100m;

        var response = await handler.Handle(request, CancellationToken.None);
        Assert.Equal(100m, response.Data!.Weight);
    }

    [Fact]
    public async Task Create_DuplicateNameUnderKra_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        await CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Revenue", 50m), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Revenue", 50m), CancellationToken.None));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task Create_DuplicateNameAllowedAcrossKras()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var kraB = new Kra { Id = Guid.NewGuid(), ScorecardId = hierarchy.Scorecard.Id, GoalId = hierarchy.Goal.Id, Name = "Lower churn", Weight = 25 };
        lease.Context.Kras.Add(kraB);
        lease.Context.SaveChanges();

        var first = await CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Revenue", 50m), CancellationToken.None);
        var second = await CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, kraB.Id, "Revenue", 50m), CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
    }

    [Fact]
    public async Task Create_BothParents_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var request = CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Ambiguous", 50m);
        request.DivisionGoalId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateHandler(lease).Handle(request, CancellationToken.None));
        Assert.Contains("exactly one parent", ex.Message);
    }

    [Fact]
    public async Task Create_SoftDeletedSiblingWeightFreedForReuse()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var first = await CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "First", 80m), CancellationToken.None);

        var saved = await lease.Context.Kpis.FindAsync(first.Data!.Id);
        saved!.IsDeleted = true;
        lease.Context.SaveChanges();

        var response = await CreateHandler(lease).Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Second", 80m), CancellationToken.None);
        Assert.True(response.Success);
    }

    // ---------- Update handler ----------

    [Fact]
    public async Task Update_WeightIntoBudgetGap_Succeeds()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var handler = CreateHandler(lease);
        var first = await handler.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "A", 40m), CancellationToken.None);
        var second = await handler.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "B", 30m), CancellationToken.None);

        var update = new UpdateKpiCommandHandler(
            new KpiRepository(lease.Context),
            new KraRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));
        var response = await update.Handle(new UpdateKpiCommandRequest { Id = second.Data!.Id, Weight = 60m }, CancellationToken.None);

        Assert.Equal(60m, response.Data!.Weight);
    }

    [Fact]
    public async Task Update_WeightExceeds100_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var handler = CreateHandler(lease);
        await handler.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "A", 60m), CancellationToken.None);
        var second = await handler.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "B", 30m), CancellationToken.None);

        var update = new UpdateKpiCommandHandler(
            new KpiRepository(lease.Context),
            new KraRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            update.Handle(new UpdateKpiCommandRequest { Id = second.Data!.Id, Weight = 45m }, CancellationToken.None));
    }

    [Fact]
    public async Task Update_UnspecifiedFields_Preserved()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var handler = CreateHandler(lease);
        var request = CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "A", 50m);
        request.Baseline = 10m;
        request.MinValue = 1m;
        request.MaxValue = 50m;
        request.IsCumulative = true;
        var created = await handler.Handle(request, CancellationToken.None);

        var update = new UpdateKpiCommandHandler(
            new KpiRepository(lease.Context),
            new KraRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));
        var response = await update.Handle(new UpdateKpiCommandRequest { Id = created.Data!.Id, Name = "A renamed" }, CancellationToken.None);

        Assert.Equal("A renamed", response.Data!.Name);
        Assert.Equal(10m, response.Data.Baseline);
        Assert.Equal(1m, response.Data.MinValue);
        Assert.Equal(50m, response.Data.MaxValue);
        Assert.True(response.Data.IsCumulative);
        Assert.Equal(hierarchy.Scorecard.Id, response.Data.ScorecardId);
        Assert.Equal(hierarchy.Kra.Id, response.Data.KraId);
    }

    [Fact]
    public async Task Update_RenameToDuplicate_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var handler = CreateHandler(lease);
        await handler.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "A", 40m), CancellationToken.None);
        var second = await handler.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "B", 40m), CancellationToken.None);

        var update = new UpdateKpiCommandHandler(
            new KpiRepository(lease.Context),
            new KraRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            update.Handle(new UpdateKpiCommandRequest { Id = second.Data!.Id, Name = "A" }, CancellationToken.None));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task Update_BothParentsInRequest_ThrowsBadRequest()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var handler = CreateHandler(lease);
        var created = await handler.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "A", 50m), CancellationToken.None);

        var update = new UpdateKpiCommandHandler(
            new KpiRepository(lease.Context),
            new KraRepository(lease.Context),
            new DivisionRepository(lease.Context),
            new SpmsUnitOfWork(lease.Context));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            update.Handle(new UpdateKpiCommandRequest { Id = created.Data!.Id, KraId = hierarchy.Kra.Id, DivisionGoalId = Guid.NewGuid() }, CancellationToken.None));
    }

    // ---------- Delete handler ----------

    [Fact]
    public async Task Delete_LeafKpi_SoftDeletedOnly()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var create = CreateHandler(lease);
        var kpi = await create.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Leaf", 25m), CancellationToken.None);

        var handler = new DeleteKpiCommandHandler(new KpiRepository(lease.Context), new SpmsUnitOfWork(lease.Context));
        await handler.Handle(new DeleteKpiCommandRequest { Id = kpi.Data!.Id }, CancellationToken.None);

        Assert.False(await lease.Context.Kpis.AnyAsync(candidate => candidate.Id == kpi.Data.Id));
        var raw = await lease.Context.Kpis.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == kpi.Data.Id);
        Assert.True(raw.IsDeleted);
    }

    [Fact]
    public async Task Delete_WithDescendants_DeletesWholeClosedSubtree()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var create = CreateHandler(lease);
        var parent = await create.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Parent", 25m), CancellationToken.None);
        var child = await create.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Child", 25m), CancellationToken.None);
        var childEntity = await lease.Context.Kpis.FindAsync(child.Data!.Id);
        childEntity!.ParentKpiId = parent.Data!.Id;
        lease.Context.SaveChanges();

        var handler = new DeleteKpiCommandHandler(new KpiRepository(lease.Context), new SpmsUnitOfWork(lease.Context));
        await handler.Handle(new DeleteKpiCommandRequest { Id = parent.Data.Id }, CancellationToken.None);

        var rawIds = await lease.Context.Kpis
            .IgnoreQueryFilters()
            .Where(candidate => candidate.Id == parent.Data.Id || candidate.Id == child.Data.Id)
            .ToListAsync();
        Assert.Equal(2, rawIds.Count);
        Assert.All(rawIds, candidate => Assert.True(candidate.IsDeleted));
    }

    [Fact]
    public async Task Delete_WithActualData_Blocked()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var fiscalPeriod = new FiscalPeriod
        {
            Id = Guid.NewGuid(),
            FiscalYearId = hierarchy.Year.Id,
            FiscalMonth = 1,
            Quarter = 1,
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2026, 7, 31)
        };
        lease.Context.FiscalPeriods.Add(fiscalPeriod);
        lease.Context.SaveChanges();

        var create = CreateHandler(lease);
        var kpi = await create.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Measured", 25m), CancellationToken.None);
        lease.Context.Set<KpiActualValue>().Add(new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Data!.Id,
            FiscalPeriodId = fiscalPeriod.Id,
            Value = 42m,
            DataSource = KpiDataSource.Manual,
            AsOfDate = DateTime.UtcNow
        });
        lease.Context.SaveChanges();

        var handler = new DeleteKpiCommandHandler(new KpiRepository(lease.Context), new SpmsUnitOfWork(lease.Context));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new DeleteKpiCommandRequest { Id = kpi.Data.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_WithZeroValuedActual_Blocked()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var fiscalPeriod = new FiscalPeriod
        {
            Id = Guid.NewGuid(),
            FiscalYearId = hierarchy.Year.Id,
            FiscalMonth = 1,
            Quarter = 1,
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2026, 7, 31)
        };
        lease.Context.FiscalPeriods.Add(fiscalPeriod);
        lease.Context.SaveChanges();

        var create = CreateHandler(lease);
        var kpi = await create.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Measured", 25m), CancellationToken.None);
        lease.Context.Set<KpiActualValue>().Add(new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Data!.Id,
            FiscalPeriodId = fiscalPeriod.Id,
            Value = 0m,
            DataSource = KpiDataSource.Manual,
            AsOfDate = DateTime.UtcNow
        });
        lease.Context.SaveChanges();

        var handler = new DeleteKpiCommandHandler(new KpiRepository(lease.Context), new SpmsUnitOfWork(lease.Context));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new DeleteKpiCommandRequest { Id = kpi.Data.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_DescendantWithActualData_BlocksParentDeletion()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var fiscalPeriod = new FiscalPeriod
        {
            Id = Guid.NewGuid(),
            FiscalYearId = hierarchy.Year.Id,
            FiscalMonth = 1,
            Quarter = 1,
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2026, 7, 31)
        };
        lease.Context.FiscalPeriods.Add(fiscalPeriod);

        var create = CreateHandler(lease);
        var parent = await create.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Parent", 25m), CancellationToken.None);
        var child = await create.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Child", 25m), CancellationToken.None);
        var childEntity = await lease.Context.Kpis.FindAsync(child.Data!.Id);
        childEntity!.ParentKpiId = parent.Data!.Id;
        lease.Context.SaveChanges();

        lease.Context.Set<KpiActualValue>().Add(new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = child.Data.Id,
            FiscalPeriodId = fiscalPeriod.Id,
            Value = 5m,
            DataSource = KpiDataSource.Manual,
            AsOfDate = DateTime.UtcNow
        });
        lease.Context.SaveChanges();

        var handler = new DeleteKpiCommandHandler(new KpiRepository(lease.Context), new SpmsUnitOfWork(lease.Context));
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new DeleteKpiCommandRequest { Id = parent.Data.Id }, CancellationToken.None));
        Assert.Contains("child KPIs", ex.Message);
    }

    [Fact]
    public async Task Delete_AlreadySoftDeletedChild_DoesNotBlockParentDeletion()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var hierarchy = SeedHierarchy(lease);
        var create = CreateHandler(lease);
        var parent = await create.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Parent", 25m), CancellationToken.None);
        var child = await create.Handle(CreateRequest(hierarchy.Scorecard.Id, hierarchy.Kra.Id, "Child", 25m), CancellationToken.None);
        var childEntity = await lease.Context.Kpis.FindAsync(child.Data!.Id);
        childEntity!.ParentKpiId = parent.Data!.Id;
        childEntity.IsDeleted = true;
        lease.Context.SaveChanges();

        var handler = new DeleteKpiCommandHandler(new KpiRepository(lease.Context), new SpmsUnitOfWork(lease.Context));
        await handler.Handle(new DeleteKpiCommandRequest { Id = parent.Data.Id }, CancellationToken.None);

        var rawParent = await lease.Context.Kpis.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == parent.Data!.Id);
        Assert.True(rawParent.IsDeleted);
    }
}
