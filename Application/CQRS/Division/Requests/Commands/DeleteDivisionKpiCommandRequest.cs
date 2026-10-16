using MediatR;

namespace Application.CQRS.Division.Requests.Commands
{
    public class DeleteDivisionKpiCommandRequest : IRequest<bool>
    {
        public Guid DivisionKpiId { get; set; }
    }
}
