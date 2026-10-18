using Application.CQRS.Initiative.Requests.Commands;
using Application.DTOS.Initiative;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Commands;

/// <summary>
/// Links an initiative to a prerequisite initiative. Self-references, duplicates and any edge
/// that would create a cycle are rejected.
/// </summary>
public class AddInitiativeDependencyCommandHandler
    : IRequestHandler<AddInitiativeDependencyCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeDependencyRepository dependencyRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeDtoFactory dtoFactory;
    private readonly InitiativeGuard guard;

    public AddInitiativeDependencyCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeDependencyRepository _dependencyRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        IUnitOfWork _unitOfWork,
        InitiativeDtoFactory _dtoFactory,
        InitiativeGuard _guard)
    {
        initiativeRepository = _initiativeRepository;
        dependencyRepository = _dependencyRepository;
        updateLogRepository = _updateLogRepository;
        unitOfWork = _unitOfWork;
        dtoFactory = _dtoFactory;
        guard = _guard;
    }

    public async Task<Response<InitiativeDto>> Handle(
        AddInitiativeDependencyCommandRequest request,
        CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetById(request.InitiativeId)
            ?? throw new NotFoundException("Initiative", request.InitiativeId);

        guard.EnsureMutable(initiative);

        guard.EnsureCanManageDependencies(initiative);

        if (request.InitiativeId == request.DependsOnInitiativeId)
        {
            throw new BadRequestException("An initiative cannot depend on itself.");
        }

        var prerequisite = await initiativeRepository.GetById(request.DependsOnInitiativeId)
            ?? throw new BadRequestException("The prerequisite initiative does not exist.");

        if (await dependencyRepository.ExistsAsync(request.InitiativeId, request.DependsOnInitiativeId))
        {
            throw new BadRequestException(
                $"'{initiative.Name}' already depends on '{prerequisite.Name}'.");
        }

        // A -> B is illegal when B can already reach A, because adding the edge closes a loop.
        var edges = await dependencyRepository.GetAllEdgesAsync();
        if (WouldCreateCycle(edges, request.InitiativeId, request.DependsOnInitiativeId))
        {
            throw new BadRequestException("This dependency would create a circular dependency.");
        }

        await dependencyRepository.AddAsync(new InitiativeDependency
        {
            Id = Guid.NewGuid(),
            InitiativeId = request.InitiativeId,
            DependsOnInitiativeId = request.DependsOnInitiativeId
        });

        await updateLogRepository.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            ChangeType = "DependencyAdded",
            Details = $"'{initiative.Name}' now depends on '{prerequisite.Name}'.",
            NewValue = prerequisite.Name
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var detail = await initiativeRepository.GetDetailAsync(initiative.Id);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = $"Dependency added: '{initiative.Name}' depends on '{prerequisite.Name}'.",
            Data = await dtoFactory.BuildDetailAsync(detail!, cancellationToken)
        };
    }

/// <summary>
    /// True when the prerequisite already depends (directly or transitively) on the dependent,
    /// i.e. the new edge would close a loop. Public so the rule can be unit tested directly
    /// instead of only through the handler.
    /// </summary>
    public static bool WouldCreateCycle(
        IReadOnlyList<(Guid InitiativeId, Guid DependsOnInitiativeId)> edges,
        Guid initiativeId,
        Guid dependsOnInitiativeId)
    {
        var direct = new Dictionary<Guid, List<Guid>>();
        foreach (var (from, to) in edges)
        {
            if (!direct.TryGetValue(from, out var list))
            {
                list = new List<Guid>();
                direct[from] = list;
            }

            list.Add(to);
        }

        // Walk the prerequisites of the proposed prerequisite; if we reach the dependent,
        // adding the edge closes a loop.
        var visited = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(dependsOnInitiativeId);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current == initiativeId)
            {
                return true;
            }

            if (direct.TryGetValue(current, out var next))
            {
                foreach (var item in next)
                {
                    pending.Push(item);
                }
            }
        }

        return false;
    }
}

/// <summary>Removes an initiative-to-initiative prerequisite link.</summary>
public class RemoveInitiativeDependencyCommandHandler
    : IRequestHandler<RemoveInitiativeDependencyCommandRequest, Response<InitiativeDto>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeDependencyRepository dependencyRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly InitiativeDtoFactory dtoFactory;
    private readonly InitiativeGuard guard;

    public RemoveInitiativeDependencyCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeDependencyRepository _dependencyRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        IUnitOfWork _unitOfWork,
        InitiativeDtoFactory _dtoFactory,
        InitiativeGuard _guard)
    {
        initiativeRepository = _initiativeRepository;
        dependencyRepository = _dependencyRepository;
        updateLogRepository = _updateLogRepository;
        unitOfWork = _unitOfWork;
        dtoFactory = _dtoFactory;
        guard = _guard;
    }

    public async Task<Response<InitiativeDto>> Handle(
        RemoveInitiativeDependencyCommandRequest request,
        CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetById(request.InitiativeId)
            ?? throw new NotFoundException("Initiative", request.InitiativeId);

        guard.EnsureMutable(initiative);

        guard.EnsureCanManageDependencies(initiative);

        var dependency = await dependencyRepository.GetAsync(request.InitiativeId, request.DependsOnInitiativeId)
            ?? throw new NotFoundException("Initiative dependency");

        var prerequisite = await initiativeRepository.GetById(request.DependsOnInitiativeId);

        dependencyRepository.Remove(dependency);

        await updateLogRepository.Add(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            ChangeType = "DependencyRemoved",
            Details = $"'{initiative.Name}' no longer depends on '{prerequisite?.Name ?? request.DependsOnInitiativeId.ToString()}'.",
            PreviousValue = prerequisite?.Name
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var detail = await initiativeRepository.GetDetailAsync(initiative.Id);

        return new Response<InitiativeDto>
        {
            Success = true,
            Message = "Dependency removed.",
            Data = await dtoFactory.BuildDetailAsync(detail!, cancellationToken)
        };
    }
}
