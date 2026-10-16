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
    private readonly IFiscalCalendarService fiscalCalendarService;
    private readonly FiscalYearLifecycleGuard lifecycleGuard;
    private readonly IBusinessClock businessClock;
    private readonly IUnitOfWork unitOfWork;

    public FiscalYearController(
        IMediator _mediator,
        IFiscalYearRepository _repository,
        IFiscalCalendarService _fiscalCalendarService,
        FiscalYearLifecycleGuard _lifecycleGuard,
        IBusinessClock _businessClock,
        IUnitOfWork _unitOfWork)
    {
        mediator = _mediator;
        repository = _repository;
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
                Status = entity.FiscalYearStatus.ToString()
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

        entity.Label = dto.Label?.Trim() ?? entity.Label;
        if (dto.StartDate.HasValue || dto.EndDate.HasValue)
        {
            // Once the year has a period calendar the guard refuses any date change; this branch
            // therefore only runs before periods exist. The end date is always derived from the
            // start so it can never drift from the twelve-month rule.
            var newStartDate = (dto.StartDate ?? entity.StartDate).Date;
            entity.StartDate = newStartDate;
            entity.EndDate = fiscalCalendarService.ComputeEndDate(newStartDate);
        }

        await repository.Update(entity);

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
                Status = entity.FiscalYearStatus.ToString()
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
