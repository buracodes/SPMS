using Application.CQRS.Goals.Requests.Commands;
using Application.DTOS.Goal;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using AutoMapper;
using Domain.Entities;
using MediatR;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Application.CQRS.Goals.Handlers.Commands
{
    public class CreateGoalCommandHandler : IRequestHandler<CreateGoalCommandRequest, Response<CreateGoalDto>>
    {
        private readonly IMapper mapper;
        private readonly IGoalRepository goalRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IUnitOfWork unitOfWork;

        public CreateGoalCommandHandler(
            IGoalRepository _goalRepository,
            IScorecardRepository _scorecardRepository,
            IUnitOfWork _unitOfWork,
            IMapper _mapper)
        {
            goalRepository = _goalRepository;
            scorecardRepository = _scorecardRepository;
            unitOfWork = _unitOfWork;
            mapper = _mapper;
        }

        public async Task<Response<CreateGoalDto>> Handle(CreateGoalCommandRequest request, CancellationToken cancellationToken)
        {
            if (request.Dto == null)
            {
                throw new BadRequestException("Goal data is required.");
            }

            var goalName = request.Dto.Name.Trim();

            // Fail fast on a missing (or soft-deleted) scorecard: the FK would otherwise
            // surface as a raw SQL exception (HTTP 500) instead of a clear 404.
            if (!await scorecardRepository.Exists(request.Dto.ScorecardId))
            {
                throw new NotFoundException("Scorecard", request.Dto.ScorecardId);
            }

            var entity = mapper.Map<Goal>(request.Dto);
            // Auto-wiring the create DTO through ReverseMap would otherwise trust the
            // caller's Id; the database owns identity for new goals.
            entity.Id = Guid.NewGuid();
            entity.Name = goalName;

            // The weight budget is checked with the insert inside one serializable
            // transaction (ADR-009): the SQL SUM and the insert can no longer race,
            // so two concurrent creates cannot jointly break the 100% cap.
            var created = await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var currentWeightSum = await goalRepository.GetWeightsSumAsync(request.Dto.ScorecardId, cancellationToken: cancellationToken);

                if (currentWeightSum + entity.Weight > 100m)
                {
                    throw new BadRequestException($"Adding this goal would bring the total goal weight to {currentWeightSum + entity.Weight:F2}% which exceeds 100%. Adjust the weight or existing goals.");
                }

                // Existing create behavior: the name must stay unique inside the scorecard
                // (case-insensitive, soft-deleted rows excluded by the global query filter).
                var existingGoals = await goalRepository.GetByScorecardAsync(request.Dto.ScorecardId);
                if (existingGoals.Any(g => string.Equals(g.Name, goalName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new BadRequestException($"A goal named '{goalName}' already exists under this scorecard.");
                }

                await goalRepository.AddWithoutSave(entity);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return entity;
            });

            var dto = mapper.Map<CreateGoalDto>(created);

            return new Response<CreateGoalDto>
            {
                Success = true,
                Message = "Goal created successfully.",
                Data = dto
            };
        }

    }
}
