using Application.CQRS.Goals.Requests.Commands;
using Application.DTOS.Goal;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using AutoMapper;
using Domain.Entities;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.CQRS.Goals.Handlers.Commands
{
    public class CreateGoalCommandHandler : IRequestHandler<CreateGoalCommandRequest, Response<CreateGoalDto>>
    {
        private readonly IMapper mapper;
        private readonly IGoalRepository goalRepository;
        public CreateGoalCommandHandler(IGoalRepository _goalRepository, IMapper _mapper)
        {
            goalRepository = _goalRepository;
            mapper = _mapper;
        }

        public async Task<Response<CreateGoalDto>> Handle(CreateGoalCommandRequest request, CancellationToken cancellationToken)
        {
            if (request.Dto == null)
            {
                throw new BadRequestException("Goal data is required.");
            }

            // Validate total weight doesn't exceed 100%
            var existingGoals = await goalRepository.GetByScorecardAsync(request.Dto.ScorecardId);
            var currentWeightSum = existingGoals.Sum(g => g.Weight);
            var newWeight = request.Dto.Weight;

            if (currentWeightSum + newWeight > 100m)
            {
                throw new BadRequestException($"Adding this goal would bring the total goal weight to {currentWeightSum + newWeight:F2}% which exceeds 100%. Adjust the weight or existing goals.");
            }

            var goalName = request.Dto.Name.Trim();
            if (existingGoals.Any(g => !g.IsDeleted && string.Equals(g.Name, goalName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BadRequestException($"A goal named '{goalName}' already exists under this scorecard.");
            }

            var entity = mapper.Map<Goal>(request.Dto);
            entity.Name = goalName;
            await goalRepository.Add(entity);
            var dto = mapper.Map<CreateGoalDto>(entity);

            return new Response<CreateGoalDto>
            {
                Success = true,
                Message = "Goal created successfully.",
                Data = dto
            };
        }

    }
}
