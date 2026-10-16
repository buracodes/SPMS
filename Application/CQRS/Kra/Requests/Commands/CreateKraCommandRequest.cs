using Application.DTOS.Kra;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Kra.Requests.Commands;

public class CreateKraCommandRequest : IRequest<Response<KraDto>>
{
    public Guid GoalId { get; set; }
    public Guid ScorecardId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public Guid? StrategicThemeId { get; set; }
    public string? UnitMeasure { get; set; }
    public decimal Weight { get; set; }
}
