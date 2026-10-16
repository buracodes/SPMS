using Application.DTOS.Kpi;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Kpi.Requests.Commands;

public class UpdateKpiCommandRequest : IRequest<Response<KpiDto>>
{
    public Guid Id { get; set; }
    public Guid? KraId { get; set; }
    public Guid? DivisionGoalId { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? DivisionOwnerId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public decimal? Weight { get; set; }
    public decimal? ToleranceBandPercent { get; set; }
    public KpiOptimizationType? OptimizationType { get; set; }
    public KpiDataSource? DataSource { get; set; }
    public KpiType? KpiType { get; set; }
    public bool? IsCumulative { get; set; }
    public Guid? ParentKpiId { get; set; }
    public decimal? Baseline { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Formula { get; set; }
    public bool? IsActive { get; set; }
}
