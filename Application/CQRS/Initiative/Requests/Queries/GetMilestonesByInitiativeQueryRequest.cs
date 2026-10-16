using Application.DTOS.Initiative;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Queries;

public class GetMilestonesByInitiativeQueryRequest : IRequest<Response<List<MilestoneDto>>>
{
    public Guid InitiativeId { get; set; }
}
