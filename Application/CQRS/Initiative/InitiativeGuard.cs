using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Calculation;
using Domain.Entities;

namespace Application.CQRS.Initiative;

/// <summary>
/// Shared business rules for initiatives (FR-401, FR-402 and the FR-405 closure lock), so
/// create, update, progress and milestone handlers enforce them identically instead of each
/// re-implementing them.
/// </summary>
public sealed class InitiativeGuard
{
    private readonly IGoalRepository goalRepository;
    private readonly IKraRepository kraRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IUserRepository userRepository;
    private readonly ICurrentUserService currentUserService;

    public InitiativeGuard(
        IGoalRepository goalRepository,
        IKraRepository kraRepository,
        IKpiRepository kpiRepository,
        IUserRepository userRepository,
        ICurrentUserService currentUserService)
    {
        this.goalRepository = goalRepository;
        this.kraRepository = kraRepository;
        this.kpiRepository = kpiRepository;
        this.userRepository = userRepository;
        this.currentUserService = currentUserService;
    }

    /// <summary>
    /// FR-401: strategic links are optional, but when supplied they must be internally
    /// consistent — a KRA must belong to the selected Goal, and a KPI must belong to the
    /// selected KRA. Nothing else is invented: the existing Scorecard/Goal/KRA/KPI hierarchy
    /// is the only thing validated.
    /// </summary>
    public async Task ValidateStrategicLinksAsync(
        Guid? goalId,
        Guid? kraId,
        Guid? kpiId,
        CancellationToken cancellationToken)
    {
        Goal? goal = null;

        if (goalId.HasValue)
        {
            goal = await goalRepository.GetById(goalId.Value);
            if (goal == null)
            {
                throw new BadRequestException($"The selected goal '{goalId.Value}' does not exist.");
            }
        }

        if (kraId.HasValue)
        {
            var kra = await kraRepository.GetById(kraId.Value);
            if (kra == null)
            {
                throw new BadRequestException($"The selected KRA '{kraId.Value}' does not exist.");
            }

            if (goalId.HasValue && kra.GoalId != goalId.Value)
            {
                throw new BadRequestException("The selected KRA does not belong to the selected goal.");
            }

            // A KPI with no corporate KRA parent (e.g. a division KPI) can never be a valid
            // KRA child, so reject that combination rather than silently ignoring it.
            if (!goalId.HasValue && kra.ScorecardId == Guid.Empty)
            {
                throw new BadRequestException("The selected KRA is not attached to a scorecard.");
            }
        }

        if (kpiId.HasValue)
        {
            var kpi = await kpiRepository.GetById(kpiId.Value);
            if (kpi == null)
            {
                throw new BadRequestException($"The selected KPI '{kpiId.Value}' does not exist.");
            }

            if (kraId.HasValue && kpi.KraId != kraId.Value)
            {
                throw new BadRequestException("The selected KPI does not belong to the selected KRA.");
            }

            if (!kraId.HasValue && kpi.DivisionGoalId.HasValue)
            {
                throw new BadRequestException(
                    "The selected KPI is a division KPI and cannot be linked without a corporate KRA.");
            }
        }
    }

    /// <summary>FR-401: budget figures may never be negative.</summary>
    public static void ValidateBudget(decimal plannedBudget, decimal actualExpenditure)
    {
        if (plannedBudget < 0m)
        {
            throw new BadRequestException("Planned budget cannot be negative.");
        }

        if (actualExpenditure < 0m)
        {
            throw new BadRequestException("Actual expenditure cannot be negative.");
        }
    }

    /// <summary>FR-401: an end date cannot precede the start date.</summary>
    public static void ValidateDateRange(DateTime? startDate, DateTime? endDate)
    {
        if (startDate.HasValue && endDate.HasValue && endDate.Value.Date < startDate.Value.Date)
        {
            throw new BadRequestException("Initiative end date cannot be before start date.");
        }
    }

    /// <summary>
    /// FR-401 no-backdating rule: a newly scheduled initiative may not start in the past. Today is
    /// allowed, earlier dates are refused. Applied when an initiative is created and when its start
    /// date is explicitly changed — never to an unchanged historical start date.
    /// </summary>
    /// <remarks>
    /// "Today" is the server's business date (<see cref="DateTime.Today"/>), the same clock the
    /// closure validation uses, so the API and the Angular picker agree on the boundary.
    /// </remarks>
    public static void ValidateStartDate(DateTime startDate)
    {
        if (startDate.Date < DateTime.Today)
        {
            throw new BadRequestException("Initiative start date cannot be before today's date.");
        }
    }

    /// <summary>FR-402: milestone weights and completion are bounded percentages.</summary>
    public static void ValidateMilestoneRanges(decimal weight, int completionPercentage)
    {
        if (weight < 0m)
        {
            throw new BadRequestException("Milestone weight cannot be negative.");
        }

        if (weight > 100m)
        {
            throw new BadRequestException("Milestone weight cannot exceed 100.");
        }

        if (completionPercentage < 0 || completionPercentage > 100)
        {
            throw new BadRequestException("Milestone completion must be between 0% and 100%.");
        }
    }

    /// <summary>
    /// FR-402: milestone weights share a single 100% budget, so adding a weight to the ones already
    /// recorded must not push the plan past 100%. <paramref name="existingWeight"/> is the sum of the
    /// other milestones on the initiative, i.e. the milestone being edited is excluded.
    /// </summary>
    /// <remarks>
    /// Callers read the current total and then save without a transaction, so two simultaneous
    /// writes can each pass this check and exceed the cap together. <c>RowVersion</c> does not close
    /// that window: the race is between reading the aggregate and inserting a new child row.
    /// Deferred deliberately — see <c>docs/decisions/ADR-009-weight-budget-concurrency.md</c>.
    /// </remarks>
    public static void ValidateMilestoneWeightBudget(decimal existingWeight, decimal weight)
    {
        // Mirrors the WeightTolerance the progress calculator already applies, so a plan that it
        // would consider configured is not rejected here. Without it, three weights of 33.3333
        // would be refused even though the plan totals 99.9999.
        const decimal tolerance = 0.0001m;
        var budget = InitiativeProgressCalculator.RequiredTotalWeight;
        var total = existingWeight + weight;

        if (total > budget + tolerance)
        {
            var overflow = total - budget;

            throw new BadRequestException(
                $"Milestone weight would take the initiative total to {total:N2}%, which exceeds the {budget:N0}% available across all milestones. Reduce this milestone by {overflow:N2}%, or lower another milestone.");
        }

        // A weightless milestone can never move the initiative's progress, because FR-606 sums
        // weight x completion. Allowing one while the other milestones already hold the whole
        // budget would add a row that is silently ignored and hides the real problem: the plan is
        // fully allocated and this milestone has no share of it. Refused rather than warned,
        // because the alternative is data that looks complete but is not.
        if (weight <= 0m && existingWeight >= budget - tolerance)
        {
            throw new BadRequestException(
                $"The existing milestones already account for all {budget:N0}% of the weight, so a new milestone cannot be added at 0% — it would never contribute to initiative progress. Give this milestone a share and lower another milestone, or lower an existing one first to free up weight.");
        }
    }

    /// <summary>
    /// FR-402: milestone due dates should sit inside the initiative's window.
    /// </summary>
    /// <remarks>
    /// Both bounds are inclusive. The date is compared on its calendar day, which keeps the rule
    /// stable regardless of the time component the API happened to store — a due date of the start
    /// day is inside the window, not an hour outside it.
    /// </remarks>
    public static void ValidateMilestoneDueDate(Domain.Entities.Initiative initiative, DateTime dueDate)
    {
        if (dueDate.Date < initiative.StartDate.Date)
        {
            throw new BadRequestException(
                $"Milestone due date {dueDate:yyyy-MM-dd} is before the initiative start date {initiative.StartDate:yyyy-MM-dd}. Milestones must fall inside the initiative window.");
        }

        if (dueDate.Date > initiative.EndDate.Date)
        {
            throw new BadRequestException(
                $"Milestone due date {dueDate:yyyy-MM-dd} is after the initiative end date {initiative.EndDate:yyyy-MM-dd}. Milestones must fall inside the initiative window.");
        }
    }

    /// <summary>
    /// FR-405: the actual end date recorded at closure must sit inside the initiative window —
    /// strictly after the start date and no later than the planned end date.
    /// </summary>
    /// <remarks>
    /// The lower bound is exclusive (an initiative cannot be delivered on the day it starts) while
    /// the upper bound is inclusive, matching FR-402's inclusive milestone window. Both bounds are
    /// compared on the calendar day, so the rule is stable regardless of any stored time component.
    /// </remarks>
    public static void ValidateActualEndDate(Domain.Entities.Initiative initiative, DateTime actualEndDate)
    {
        if (actualEndDate.Date <= initiative.StartDate.Date)
        {
            throw new BadRequestException("Actual End Date must be strictly later than the initiative's Start Date.");
        }

        if (actualEndDate.Date > initiative.EndDate.Date)
        {
            throw new BadRequestException(
                $"Actual End Date {actualEndDate:yyyy-MM-dd} must be on or before the initiative's planned End Date {initiative.EndDate:yyyy-MM-dd}.");
        }
    }

    /// <summary>
    /// FR-405: once closure information is recorded the initiative is read-only. Admin and Power
    /// User accounts retain correction rights (they hold <c>initiatives:edit</c>); everyone else,
    /// including the original owner, is locked out. Enforced server-side so hiding buttons in
    /// Angular is never the only protection.
    /// </summary>
    public void EnsureMutable(Domain.Entities.Initiative initiative)
    {
        if (!initiative.IsClosed)
        {
            return;
        }

        if (HasPermission(Domain.Constants.Permissions.InitiativesEdit)
            || HasPermission(Domain.Constants.Permissions.InitiativesClose))
        {
            return;
        }

        throw new BadRequestException(
            "This initiative has been closed and is read-only. Ask an administrator to record a correction.");
    }

    /// <summary>
    /// FR-403: progress may be reported by the assigned owner or by a user holding the
    /// progress-update permission. A division account that is not the owner cannot report on
    /// somebody else's initiative.
    /// </summary>
    public void EnsureCanUpdateProgress(Domain.Entities.Initiative initiative)
    {
        if (HasPermission(Domain.Constants.Permissions.InitiativesUpdateProgress)
            || HasPermission(Domain.Constants.Permissions.InitiativesManageMilestones))
        {
            return;
        }

        if (currentUserService.UserId.HasValue && currentUserService.UserId.Value == initiative.OwnerId)
        {
            return;
        }

        throw new ForbiddenException(
            "Only the assigned initiative owner or a user with initiative progress permission can update this initiative.");
    }

    /// <summary>
    /// FR-403 milestone delivery state. The three FRD states map onto completion directly: 100% is
    /// <c>Completed</c> automatically, 0% is <c>NotStarted</c>, and anything in between is
    /// <c>InProgress</c>. The caller's requested status is honoured only where it agrees with the
    /// recorded completion, so a milestone can never claim to be finished at 40% or untouched at 60%.
    /// </summary>
    public static Domain.Enums.MilestoneStatus ResolveMilestoneStatus(
        int completionPercentage,
        Domain.Enums.MilestoneStatus requested)
    {
        if (completionPercentage >= 100)
        {
            return Domain.Enums.MilestoneStatus.Completed;
        }

        if (completionPercentage <= 0)
        {
            return Domain.Enums.MilestoneStatus.NotStarted;
        }

        // Between 1% and 99% the milestone is under way. A requested NotStarted is overridden here
        // because recorded progress and "not started" contradict each other.
        return Domain.Enums.MilestoneStatus.InProgress;
    }

    /// <summary>
    /// FRD lifecycle: the first recorded progress starts the initiative. A <c>NotStarted</c>
    /// initiative that reports any completion above 0 moves to <c>InProgress</c> — the owner's or
    /// authorised power user's progress report is the "work has begun" signal.
    /// <para>
    /// Only <c>NotStarted → InProgress</c> is ever performed here. OnHold/Cancelled decisions,
    /// an explicitly set status and an already-<c>Completed</c> or <c>InProgress</c> initiative are
    /// left alone, exactly like <see cref="ResolveMilestoneStatus"/> never undoes a deliberate
    /// state, and reporting 0% does not start anything.
    /// </para>
    /// </summary>
    public static bool StartExecution(Domain.Entities.Initiative initiative, int milestoneCompletionPercentage)
    {
        if (initiative.Status != Domain.Enums.InitiativeStatus.NotStarted)
        {
            return false;
        }

        if (milestoneCompletionPercentage <= 0)
        {
            return false;
        }

        initiative.Status = Domain.Enums.InitiativeStatus.InProgress;
        return true;
    }

/// <summary>
    /// FRD lifecycle: initiative progress and initiative status are independent. Weighted progress
    /// never writes <see cref="Domain.Enums.InitiativeStatus.Completed"/>, and a deliberate
    /// OnHold/Cancelled decision is never undone by a progress edit.
    /// <para>
    /// Completion is a recorded, authorised business decision (formal closure), not a side effect of
    /// a number reaching 100. The readiness test below is what a progress edit may influence.
    /// </para>
    /// </summary>
    public static bool IsReadyForCompletion(Domain.Calculation.InitiativeProgressResult progress) =>
        progress.HasMilestones && progress.WeightsAreValid && progress.ProgressPercentage >= 100m;

    /// <summary>
    /// FRD closure gate: explains, in the caller's words, why an initiative cannot be closed yet.
    /// Returns null when closure may proceed.
    /// </summary>
    public static string? DescribeWhyNotReadyForCompletion(
        Domain.Calculation.InitiativeProgressResult progress)
    {
        if (!progress.HasMilestones)
        {
            return "This initiative has no milestones, so there is no delivery progress to complete. Add milestones before closing it.";
        }

        if (!progress.WeightsAreValid)
        {
            return $"Milestone weights must total 100% before this initiative can be closed. They currently total {progress.TotalWeight:N2}%.";
        }

        if (progress.ProgressPercentage < 100m)
        {
            return $"An initiative cannot be closed before its milestones reach 100%. Current progress is {progress.ProgressPercentage:N2}%.";
        }

        return null;
    }

    /// <summary>
    /// FRD budget authorization: actual expenditure to date is financial data, so it is only
    /// writable by a user holding <c>initiatives:expenditure.update</c> (or the wider Power User
    /// corrections permissions <c>initiatives:edit</c>/<c>initiatives:close</c>) or by the
    /// initiative's own owner. The assigned owner reaches this through the narrow expenditure
    /// endpoint only, so posting spend never grants the detail-editing rights that
    /// <c>InitiativesEdit</c> protects. Mirrors <see cref="EnsureCanUpdateProgress"/> — a
    /// division account that is not the owner cannot post spend against somebody else's initiative.
    /// </summary>
    public void EnsureCanUpdateExpenditure(Domain.Entities.Initiative initiative)
    {
        if (HasPermission(Domain.Constants.Permissions.InitiativesUpdateExpenditure)
            || HasPermission(Domain.Constants.Permissions.InitiativesEdit)
            || HasPermission(Domain.Constants.Permissions.InitiativesClose))
        {
            return;
        }

        if (currentUserService.UserId.HasValue && currentUserService.UserId.Value == initiative.OwnerId)
        {
            return;
        }

        throw new ForbiddenException(
            "Only the assigned initiative owner or a user with expenditure permission can update this initiative's actual expenditure.");
    }

    /// <summary>
    /// FRD lifecycle: the status transitions that a hold/resume/cancel action may perform. Status is
    /// the initiative's execution state, so it is only ever moved through the dedicated lifecycle
    /// actions - never through a bare detail edit. Closure moves a finished initiative to
    /// <c>Completed</c>, and reopening moves a closed one back to <c>InProgress</c>; neither is
    /// reachable here.
    /// <list type="bullet">
    /// <item><c>OnHold</c>: planned or underway work suspended, so from Not Started or In Progress.</item>
    /// <item><c>Cancelled</c>: stopped for good, from any pre-closure state except Completed.</item>
    /// <item><c>InProgress</c>: resuming work that was on hold, so from On Hold only.</item>
    /// </list>
    /// </summary>
    public static bool IsAllowedTransition(
        Domain.Enums.InitiativeStatus current,
        Domain.Enums.InitiativeStatus target) =>
        target switch
        {
            Domain.Enums.InitiativeStatus.OnHold =>
                current is Domain.Enums.InitiativeStatus.NotStarted
                    or Domain.Enums.InitiativeStatus.InProgress,
            Domain.Enums.InitiativeStatus.Cancelled =>
                current is Domain.Enums.InitiativeStatus.NotStarted
                    or Domain.Enums.InitiativeStatus.InProgress
                    or Domain.Enums.InitiativeStatus.OnHold,
            Domain.Enums.InitiativeStatus.InProgress =>
                current is Domain.Enums.InitiativeStatus.OnHold,
            _ => false
        };

    /// <summary>
    /// Centralized lifecycle gate: refuses a status transition that <see cref="IsAllowedTransition"/>
    /// would reject, so every lifecycle handler (hold, resume, cancel) enforces the same rule.
    /// </summary>
    public static void EnsureCanTransition(
        Domain.Entities.Initiative initiative,
        Domain.Enums.InitiativeStatus target)
    {
        if (IsAllowedTransition(initiative.Status, target))
        {
            return;
        }

        throw new BadRequestException(
            $"An initiative in state {initiative.Status} cannot be moved to {target}. "
            + "On Hold can follow Not Started or In Progress, a cancelled initiative can only come from "
            + "Not Started, In Progress or On Hold, and resuming only applies to an initiative on hold. "
            + "Completion and reopening are separate workflow actions.");
    }

    /// <summary>FRD lifecycle: a deliberate decision (hold or cancel) needs a recorded reason.</summary>
    public static string RequireLifecycleReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BadRequestException("A reason is required to change an initiative's status.");
        }

        return reason.Trim();
    }

    /// <summary>
    /// FRD lifecycle: holding or resuming work is a day-to-day execution decision, so the assigned
    /// owner may make it, as may a user with the edit or close permissions.
    /// </summary>
    public void EnsureCanManageLifecycle(Domain.Entities.Initiative initiative)
    {
        if (HasPermission(Domain.Constants.Permissions.InitiativesEdit)
            || HasPermission(Domain.Constants.Permissions.InitiativesClose))
        {
            return;
        }

        if (currentUserService.UserId.HasValue && currentUserService.UserId.Value == initiative.OwnerId)
        {
            return;
        }

        throw new ForbiddenException(
            "Only the assigned initiative owner or a user with initiative edit permission can hold or resume this initiative.");
    }

    /// <summary>
    /// FRD lifecycle: cancelling is a structural decision, so it is reserved for users holding the
    /// edit or close permissions.
    /// <para>
    /// Open decision (tracked separately): whether an assigned owner may cancel their own initiative
    /// directly or must request cancellation from a Power User. The conservative default below keeps
    /// cancellation with the edit/close holders and hides the action from owners; revisit when the
    /// owner-request workflow is designed.
    /// </para>
    /// </summary>
    public void EnsureCanCancel(Domain.Entities.Initiative initiative)
    {
        if (HasPermission(Domain.Constants.Permissions.InitiativesEdit)
            || HasPermission(Domain.Constants.Permissions.InitiativesClose))
        {
            return;
        }

        throw new ForbiddenException(
            "Only a user with initiative edit or close permission can cancel this initiative.");
    }

    /// <summary>
    /// FRD closure authorization: formal closure is reserved for a Power User holding
    /// <c>initiatives:close</c>, or the assigned initiative owner confirming completion of their own
    /// initiative. Anyone else is refused even if they can view the initiative.
    /// </summary>
    public void EnsureCanClose(Domain.Entities.Initiative initiative)
    {
        if (HasPermission(Domain.Constants.Permissions.InitiativesClose))
        {
            return;
        }

        if (currentUserService.UserId.HasValue && currentUserService.UserId.Value == initiative.OwnerId)
        {
            return;
        }

        throw new ForbiddenException(
            "Only the assigned initiative owner or a power user can confirm completion of this initiative.");
    }

    /// <summary>Owner-or-power-user check for structural milestone changes (FR-402).</summary>
    public void EnsureCanManageMilestones(Domain.Entities.Initiative initiative)
    {
        if (HasPermission(Domain.Constants.Permissions.InitiativesManageMilestones)
            || HasPermission(Domain.Constants.Permissions.InitiativesEdit))
        {
            return;
        }

        if (currentUserService.UserId.HasValue && currentUserService.UserId.Value == initiative.OwnerId)
        {
            return;
        }

        throw new ForbiddenException(
            "Only the assigned initiative owner or a user with milestone management permission can change milestones.");
    }

    /// <summary>Owner-or-power-user check for dependency graph changes.</summary>
    public void EnsureCanManageDependencies(Domain.Entities.Initiative initiative)
    {
        if (HasPermission(Domain.Constants.Permissions.InitiativesManageDependencies)
            || HasPermission(Domain.Constants.Permissions.InitiativesEdit))
        {
            return;
        }

        if (currentUserService.UserId.HasValue && currentUserService.UserId.Value == initiative.OwnerId)
        {
            return;
        }

        throw new ForbiddenException(
            "Only the assigned initiative owner or a user with dependency management permission can change initiative dependencies.");
    }

    private bool HasPermission(string permission) =>
        currentUserService.Permissions is not null
        && currentUserService.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
