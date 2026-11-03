using Application.DTOS.Division;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Requests.Commands
{
    /// <summary>Imports every division HR organizes positions under into the local Division table.</summary>
    public class SyncDivisionsFromHrCommandRequest : IRequest<Response<DivisionSyncResultDto>>
    {
    }
}
