using Application.DTOS.Initiative;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Commands;

/// <summary>
/// FRD lifecycle: puts an underway initiative on hold. The reason is a proposed business rule
/// (the FRD defines the On Hold state but not the reason); it is required as a deliberate-decision
/// record, mirroring how closure records its own information.
/// </summary>
public class PutInitiativeOnHoldCommandRequest : IRequest<Response<InitiativeDto>>
{
    public Guid InitiativeId { get; set; }

    public string Reason { get; set; } = string.Empty;
}

/// <summary>FRD lifecycle: resumes work on an initiative that was on hold.</summary>
public class ResumeInitiativeCommandRequest : IRequest<Response<InitiativeDto>>
{
    public Guid InitiativeId { get; set; }

    /// <summary>Optional note; resuming is the flip side of the hold that was already reasoned.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// FRD lifecycle: cancels an initiative. The reason is a proposed business rule; it is required so
/// the cancellation is auditable. Whether an owner may request instead of cancel directly is an
/// open decision - see InitiativeGuard.EnsureCanCancel.
/// </summary>
public class CancelInitiativeCommandRequest : IRequest<Response<InitiativeDto>>
{
    public Guid InitiativeId { get; set; }

    public string Reason { get; set; } = string.Empty;
}