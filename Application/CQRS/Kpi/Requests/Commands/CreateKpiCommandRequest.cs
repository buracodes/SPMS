using Application.DTOS.Kpi;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Kpi.Requests.Commands;

public class CreateKpiCommandRequest : IRequest<Response<KpiDto>>
{
    public Guid ScorecardId { get; set; }
    public Guid? KraId { get; set; }
    public Guid? DivisionGoalId { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? DivisionOwnerId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public decimal Weight { get; set; }
    public KpiOptimizationType OptimizationType { get; set; } = KpiOptimizationType.Maximize;
    public KpiDataSource DataSource { get; set; } = KpiDataSource.Manual;
    public KpiType KpiType { get; set; } = KpiType.Lagging;
    public bool IsCumulative { get; set; }
    public Guid? ParentKpiId { get; set; }
    public decimal? Baseline { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Formula { get; set; }
}
