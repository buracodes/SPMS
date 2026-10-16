using Application.CQRS.FiscalYear.Requests;
using Application.DTOS.FiscalYear;
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
    private readonly IFiscalCalendarService fiscalCalendarService;
    private readonly FiscalYearLifecycleGuard lifecycleGuard;
    private readonly IBusinessClock businessClock;

    public FiscalYearController(
        IMediator _mediator,
        IFiscalYearRepository _repository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IFiscalCalendarService _fiscalCalendarService,
        FiscalYearLifecycleGuard _lifecycleGuard,
        IBusinessClock _businessClock)
    {
        mediator = _mediator;
        repository = _repository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        fiscalCalendarService = _fiscalCalendarService;
        lifecycleGuard = _lifecycleGuard;
        businessClock = _businessClock;
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
        await lifecycleGuard.EnsureCanCreateAsync(dto);

        var entity = new FiscalYear
        {
            Label = dto.Label,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            FiscalYearStatus = Domain.Enums.FiscalYearStatus.Open,
            CreatedBy = Guid.Empty,
            CreatedDate = DateTime.UtcNow,
        };
        await repository.Add(entity);

        var periods = fiscalCalendarService.GeneratePeriods(entity);
        await fiscalPeriodRepository.AddRange(periods);

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

        entity.Label = dto.Label ?? entity.Label;
        entity.StartDate = dto.StartDate ?? entity.StartDate;
        entity.EndDate = dto.EndDate ?? entity.EndDate;

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
