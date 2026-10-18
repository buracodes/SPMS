using Application.DTOS.FiscalYear;
using Application.Exceptions;
using Application.Services;
using Application.Tests.TestSupport;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;

namespace Application.Tests.FiscalYears;

public class FiscalYearLifecycleGuardTests
{
    [Fact]
    public async Task Create_RejectsInvertedDates()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            guard.EnsureCanCreateAsync(new CreateFiscalYearDto
            {
                Label = "Invalid",
                StartDate = new DateTime(2099, 7, 1),
                EndDate = new DateTime(2099, 6, 30)
            }));
    }

    [Fact]
    public async Task Create_AllowsValidDates()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var guard = CreateGuard(lease);

        await guard.EnsureCanCreateAsync(new CreateFiscalYearDto
        {
            Label = "2099/00",
            StartDate = new DateTime(2099, 7, 1),
            EndDate = new DateTime(2100, 6, 30)
        });
    }

    [Fact]
    public async Task Create_RejectsStartDateThatIsNotFirstOfMonth()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            guard.EnsureCanCreateAsync(new CreateFiscalYearDto
            {
                Label = "Sep/26",
                StartDate = new DateTime(2099, 9, 15)
            }));
    }

    [Fact]
    public async Task Create_RejectsEndDateThatIsNotTwelveMonths()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            guard.EnsureCanCreateAsync(new CreateFiscalYearDto
            {
                Label = "2099/00",
                StartDate = new DateTime(2099, 7, 1),
                EndDate = new DateTime(2099, 12, 31)
            }));
    }

    [Fact]
    public async Task Create_DerivesTwelveMonthEndDateWhenOmitted()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var guard = CreateGuard(lease);

        var endDate = await guard.EnsureCanCreateAsync(new CreateFiscalYearDto
        {
            Label = "Sep/26",
            StartDate = new DateTime(2099, 9, 1)
        });

        Assert.Equal(new DateTime(2100, 8, 31), endDate);
    }

    [Fact]
    public async Task Create_AllowsNonJulyStartMonth()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var guard = CreateGuard(lease);

        await guard.EnsureCanCreateAsync(new CreateFiscalYearDto
        {
            Label = "Sep/26",
            StartDate = new DateTime(2099, 9, 1),
            EndDate = new DateTime(2100, 8, 31)
        });
    }

    [Fact]
    public async Task Create_RejectsDuplicateLabel()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedYearAsync(lease);
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            guard.EnsureCanCreateAsync(new CreateFiscalYearDto
            {
                Label = "2099/00",
                StartDate = new DateTime(2100, 7, 1),
                EndDate = new DateTime(2101, 6, 30)
            }));
    }

    [Fact]
    public async Task Create_RejectsOverlappingCalendar()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        await SeedYearAsync(lease);
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            guard.EnsureCanCreateAsync(new CreateFiscalYearDto
            {
                Label = "Sep/26",
                StartDate = new DateTime(2099, 9, 1),
                EndDate = new DateTime(2100, 8, 31)
            }));
    }

    [Fact]
    public async Task Update_RejectsClosedYear()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, closed: true);
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<FiscalYearMutationException>(() =>
            guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto { Label = "Renamed" }, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Update_RejectsYearEndedByBusinessDate()
    {
        // The year's end date has passed in ECX business time even though its status is
        // still Open (the closure job has not run). It must be immutable.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<FiscalYearMutationException>(() =>
            guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto { Label = "Renamed" }, BusinessDateAfterEnd));
    }

    [Fact]
    public async Task Update_Dates_AllowsWhenPeriodsExistButNoScorecardsOrValues()
    {
        // Periods alone no longer freeze the calendar: a correction is allowed as long as no
        // scorecard, target or actual value depends on them (they are safely regenerated).
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, withPeriods: true);
        var guard = CreateGuard(lease);

        await guard.EnsureCanUpdateAsync(
            year,
            new UpdateFiscalYearDto { StartDate = new DateTime(2099, 9, 1) },
            BusinessDateBeforeEnd);
    }

    [Fact]
    public async Task Update_Dates_RejectsWhenScorecardExists()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, withPeriods: true);
        lease.Context.Scorecards.Add(new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Scorecard 2099",
            Scope = ScorecardScope.Corporate
        });
        await lease.Context.SaveChangesAsync();
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<FiscalYearMutationException>(() =>
            guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto { StartDate = new DateTime(2099, 9, 1) }, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Update_Dates_RejectsWhenTargetsExist()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, withPeriods: true);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Scorecard 2099",
            Scope = ScorecardScope.Corporate
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "KRA", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "KPI",
            Unit = "%",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
        var period = year.Periods.First();
        var target = new KpiTarget
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            TargetValue = 100,
            Granularity = TargetGranularity.Annual
        };

        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.KpiTargets.Add(target);
        await lease.Context.SaveChangesAsync();
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<FiscalYearMutationException>(() =>
            guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto { StartDate = new DateTime(2099, 9, 1) }, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Update_Dates_RejectsWhenActualsExist()
    {
        // Any actual value recorded against a period depends on that period's calendar, so a
        // calendar change must be rejected (defensive counterpart to the target gate).
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, withPeriods: true);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Scorecard 2099",
            Scope = ScorecardScope.Corporate
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "KRA", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "KPI",
            Unit = "%",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
        var period = year.Periods.First();
        var actual = new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            Value = 42,
            DataSource = KpiDataSource.Manual,
            AsOfDate = DateTime.UtcNow
        };

        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.KpiActualValues.Add(actual);
        await lease.Context.SaveChangesAsync();
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<FiscalYearMutationException>(() =>
            guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto { StartDate = new DateTime(2099, 9, 1) }, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Update_Dates_RejectsStartThatIsNotFirstOfMonth()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, withPeriods: true);
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto { StartDate = new DateTime(2099, 9, 15) }, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Update_Dates_RejectsOverlappingCalendar()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease); // 2099-07-01 .. 2100-06-30
        var neighbour = new FiscalCalendarService().CreateFiscalYear(2098); // 2098-07-01 .. 2099-06-30
        lease.Context.FiscalYears.Add(neighbour);
        await lease.Context.SaveChangesAsync();
        var guard = CreateGuard(lease);

        // Moving the 2099 year back to June makes its span overlap the 2098 year.
        await Assert.ThrowsAsync<BadRequestException>(() =>
            guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto { StartDate = new DateTime(2099, 6, 1) }, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Update_Dates_AllowsUnchangedStart_EvenWhenScorecardsExist()
    {
        // Re-submitting the same start date is not a calendar change, so it is not blocked by the
        // scorecard gate (a label-only edit must still succeed).
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, withPeriods: true);
        lease.Context.Scorecards.Add(new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Scorecard 2099",
            Scope = ScorecardScope.Corporate
        });
        await lease.Context.SaveChangesAsync();
        var guard = CreateGuard(lease);

        await guard.EnsureCanUpdateAsync(
            year,
            new UpdateFiscalYearDto { StartDate = year.StartDate },
            BusinessDateBeforeEnd);
    }

    [Fact]
    public async Task Update_Label_AllowsOpenYear_WhenDatesUnchanged()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, withPeriods: true);
        var guard = CreateGuard(lease);

        await guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto { Label = "Renamed" }, BusinessDateBeforeEnd);
    }

    [Fact]
    public async Task Update_Label_Allows_WhenScorecardsExist()
    {
        // Confirmed rule: a year carrying scorecards cannot have its DATES edited, but it may still
        // be relabelled because a label has no dependent periods, targets or snapshots.
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, withPeriods: true);
        lease.Context.Scorecards.Add(new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Scorecard 2099",
            Scope = ScorecardScope.Corporate
        });
        await lease.Context.SaveChangesAsync();
        var guard = CreateGuard(lease);

        await guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto { Label = "Renamed 2099" }, BusinessDateBeforeEnd);
    }

    [Fact]
    public async Task Update_RejectsBlankLabel()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto { Label = "   " }, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Update_NullLabel_LeavesLabelUnchanged()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var guard = CreateGuard(lease);

        await guard.EnsureCanUpdateAsync(year, new UpdateFiscalYearDto(), BusinessDateBeforeEnd);

        Assert.Equal("2099/00", year.Label);
    }

    [Fact]
    public async Task Update_RejectsDuplicateLabel_IgnoringCase()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var calendar = new FiscalCalendarService();
        var first = calendar.CreateFiscalYear(2099);
        first.Label = "FY2099";
        var second = calendar.CreateFiscalYear(2100);
        second.Label = "FY2100";
        lease.Context.FiscalYears.AddRange(first, second);
        await lease.Context.SaveChangesAsync();
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            guard.EnsureCanUpdateAsync(second, new UpdateFiscalYearDto { Label = "fy2099" }, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Delete_RejectsClosedYear()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, closed: true);
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<FiscalYearMutationException>(() =>
            guard.EnsureCanDeleteAsync(year, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Delete_RejectsEndedYear()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var guard = CreateGuard(lease);

        await Assert.ThrowsAsync<FiscalYearMutationException>(() =>
            guard.EnsureCanDeleteAsync(year, BusinessDateAfterEnd));
    }

    [Fact]
    public async Task Delete_RejectsYearWithScorecards()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, withPeriods: true);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Historical 2099",
            Scope = ScorecardScope.Corporate
        };
        lease.Context.Scorecards.Add(scorecard);
        await lease.Context.SaveChangesAsync();

        var guard = CreateGuard(lease);
        await Assert.ThrowsAsync<FiscalYearMutationException>(() =>
            guard.EnsureCanDeleteAsync(year, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Delete_RejectsYearWithTargets()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease, withPeriods: true);
        var scorecard = new Scorecard
        {
            Id = Guid.NewGuid(),
            FiscalYearId = year.Id,
            Name = "Scorecard 2099",
            Scope = ScorecardScope.Corporate
        };
        var goal = new Goal { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, Name = "Goal", Weight = 100 };
        var kra = new Kra { Id = Guid.NewGuid(), ScorecardId = scorecard.Id, GoalId = goal.Id, Name = "KRA", Weight = 100 };
        var kpi = new Kpi
        {
            Id = Guid.NewGuid(),
            ScorecardId = scorecard.Id,
            KraId = kra.Id,
            Name = "KPI",
            Unit = "%",
            Weight = 100,
            DataSource = KpiDataSource.Manual,
            KpiType = KpiType.Lagging
        };
        var period = year.Periods.First();
        var target = new KpiTarget
        {
            Id = Guid.NewGuid(),
            KpiId = kpi.Id,
            FiscalPeriodId = period.Id,
            TargetValue = 100,
            Granularity = TargetGranularity.Annual
        };

        lease.Context.Scorecards.Add(scorecard);
        lease.Context.Goals.Add(goal);
        lease.Context.Kras.Add(kra);
        lease.Context.Kpis.Add(kpi);
        lease.Context.KpiTargets.Add(target);
        await lease.Context.SaveChangesAsync();

        var guard = CreateGuard(lease);
        await Assert.ThrowsAsync<FiscalYearMutationException>(() =>
            guard.EnsureCanDeleteAsync(year, BusinessDateBeforeEnd));
    }

    [Fact]
    public async Task Delete_AllowsEmptyOpenYear()
    {
        await using var lease = await TestDbContextFactory.CreateAsync();
        var year = await SeedYearAsync(lease);
        var guard = CreateGuard(lease);

        await guard.EnsureCanDeleteAsync(year, BusinessDateBeforeEnd);
    }

    private static readonly DateTime BusinessDateBeforeEnd = new(2100, 6, 30);
    private static readonly DateTime BusinessDateAfterEnd = new(2100, 7, 1);

    private static FiscalYearLifecycleGuard CreateGuard(TestDbContextLease lease)
    {
        return new FiscalYearLifecycleGuard(
            new FiscalPeriodRepository(lease.Context),
            new ScorecardRepository(lease.Context),
            new KpiTargetRepository(lease.Context),
            new KpiActualValueRepository(lease.Context),
            new FiscalYearRepository(lease.Context),
            new FiscalCalendarService());
    }

    private static async Task<FiscalYear> SeedYearAsync(TestDbContextLease lease, bool closed = false, bool withPeriods = false)
    {
        var calendar = new FiscalCalendarService();
        var year = calendar.CreateFiscalYear(2099);
        if (closed)
        {
            year.FiscalYearStatus = FiscalYearStatus.Closed;
        }

        lease.Context.FiscalYears.Add(year);
        if (withPeriods)
        {
            year.Periods = calendar.GeneratePeriods(year).ToList();
            lease.Context.FiscalPeriods.AddRange(year.Periods);
        }

        await lease.Context.SaveChangesAsync();
        return year;
    }
}