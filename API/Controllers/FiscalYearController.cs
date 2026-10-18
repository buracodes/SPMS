using Application.CQRS.FiscalYear.Requests;
using Application.DTOS.FiscalYear;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Application.Services;
using Asp.Versioning;
using Domain.Constants;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ApiVersion("1.0")]
public class FiscalYearController : ControllerBase
{
    private readonly IMediator mediator;
    private readonly IFiscalYearRepository repository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IScorecardRepository scorecardRepository;
    private readonly IFiscalCalendarService fiscalCalendarService;
    private readonly FiscalYearLifecycleGuard lifecycleGuard;
    private readonly IBusinessClock businessClock;
    private readonly IUnitOfWork unitOfWork;

    public FiscalYearController(
        IMediator _mediator,
        IFiscalYearRepository _repository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IScorecardRepository _scorecardRepository,
        IFiscalCalendarService _fiscalCalendarService,
        FiscalYearLifecycleGuard _lifecycleGuard,
        IBusinessClock _businessClock,
        IUnitOfWork _unitOfWork)
    {
        mediator = _mediator;
        repository = _repository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        scorecardRepository = _scorecardRepository;
        fiscalCalendarService = _fiscalCalendarService;
        lifecycleGuard = _lifecycleGuard;
        businessClock = _businessClock;
        unitOfWork = _unitOfWork;
    }

    /// <summary>
    /// The fiscal year list is a read-only dimension every role needs to filter planning
    /// grids, so it follows dashboard access. Creating, editing and deleting a year stays
    /// admin only, as do the periods below.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<ActionResult<Response<List<FiscalYearDto>>>> GetAll()
    {
        var result = await mediator.Send(new GetFiscalYearRequest());
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.AdminConfigManage)]
    public async Task<ActionResult<Response<FiscalYearDto>>> GetById(Guid id)
    {
        var entity = await repository.GetById(id);
        if (entity == null) return NotFound();

        return Ok(new Response<FiscalYearDto>
        {
            Success = true,
            Message = "Fiscal year retrieved successfully.",
            Data = new FiscalYearDto
            {
                Id = entity.Id,
                Label = entity.Label,
                StartDate = entity.StartDate,
                EndDate = entity.EndDate,
                Status = entity.FiscalYearStatus.ToString(),
                HasScorecards = await scorecardRepository.AnyForFiscalYearAsync(entity.Id)
            }
        });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.AdminConfigManage)]
    public async Task<ActionResult<Response<FiscalYearDto>>> Create([FromBody] CreateFiscalYearDto dto)
    {
        var endDate = await lifecycleGuard.EnsureCanCreateAsync(dto);

        var entity = new FiscalYear
        {
            Id = Guid.NewGuid(),
            Label = dto.Label.Trim(),
            StartDate = dto.StartDate.Date,
            EndDate = endDate,
            FiscalYearStatus = Domain.Enums.FiscalYearStatus.Open,
            CreatedBy = Guid.Empty,
            CreatedDate = DateTime.UtcNow,
        };

        // The year and its twelve monthly periods are staged together and committed in a single
        // SaveChanges, so a failure never leaves a year without a calendar (or vice versa).
        await repository.AddWithoutSave(entity);
        var periods = fiscalCalendarService.GeneratePeriods(entity);
        await unitOfWork.AddRangeAsync(periods);
        await unitOfWork.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, new Response<FiscalYearDto>
        {
            Success = true,
            Message = "Fiscal year created successfully.",
            Data = new FiscalYearDto
            {
                Id = entity.Id,
                Label = entity.Label,
                StartDate = entity.StartDate,
                EndDate = entity.EndDate,
                Status = entity.FiscalYearStatus.ToString()
            }
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.AdminConfigManage)]
    public async Task<ActionResult<Response<FiscalYearDto>>> Update(Guid id, [FromBody] UpdateFiscalYearDto dto)
    {
        var entity = await repository.GetById(id);
        if (entity == null) return NotFound();

        await lifecycleGuard.EnsureCanUpdateAsync(entity, dto, businessClock.BusinessDate);

        var datesChanged = dto.StartDate.HasValue && dto.StartDate.Value.Date != entity.StartDate.Date;

        // null means "leave the label unchanged". The guard has already rejected a
        // non-null blank label, but the fallback is kept explicit so a blank value can
        // never erase the existing label here.
        var newLabel = dto.Label?.Trim();
        if (!string.IsNullOrEmpty(newLabel))
        {
            entity.Label = newLabel;
        }
        if (datesChanged)
        {
            // The guard only lets the dates move while nothing depends on the calendar. The end
            // date is always derived from the new start so it can never drift from the
            // twelve-month rule.
            var newStartDate = dto.StartDate!.Value.Date;
            entity.StartDate = newStartDate;
            entity.EndDate = fiscalCalendarService.ComputeEndDate(newStartDate);
        }

        // Stage the year change, and when the dates moved replace the twelve monthly periods.
        // The old periods are guaranteed free of dependents (scorecards, targets, actuals) by the
        // guard, so they are safely superseded. The entity update and the period swap commit in a
        // single transaction through the unit of work.
        await repository.UpdateWithoutSave(entity);
        if (datesChanged)
        {
            var oldPeriods = await fiscalPeriodRepository.GetByFiscalYearAsync(entity.Id);
            await unitOfWork.RemoveRangeAsync(oldPeriods);
            var newPeriods = fiscalCalendarService.GeneratePeriods(entity);
            await unitOfWork.AddRangeAsync(newPeriods);
        }

        await unitOfWork.SaveChangesAsync();

        return Ok(new Response<FiscalYearDto>
        {
            Success = true,
            Message = "Fiscal year updated successfully.",
            Data = new FiscalYearDto
            {
                Id = entity.Id,
                Label = entity.Label,
                StartDate = entity.StartDate,
                EndDate = entity.EndDate,
                Status = entity.FiscalYearStatus.ToString(),
                HasScorecards = await scorecardRepository.AnyForFiscalYearAsync(entity.Id)
            }
        });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.AdminConfigManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var entity = await repository.GetById(id);
        if (entity == null) return NotFound();

        await lifecycleGuard.EnsureCanDeleteAsync(entity, businessClock.BusinessDate);

        await repository.Delete(entity);
        return NoContent();
    }
}
