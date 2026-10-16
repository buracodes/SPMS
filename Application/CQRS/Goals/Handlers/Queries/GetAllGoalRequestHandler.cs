using Application.CQRS.Goals.Requests.Queries;
using Application.DTOS.Goal;
using Application.Interface.IRepository;
using Application.Responses;
using AutoMapper;
using Domain.Entities;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Goals.Handlers.Queries
{
    public class GetAllGoalRequestHandler : IRequestHandler<GetAllGoalRequest, Response<List<GetAllGoalDto>>>
    {
        private readonly IGoalRepository goalRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IMapper mapper;
        public GetAllGoalRequestHandler(
            IGoalRepository _goalRepository,
            IScorecardRepository _scorecardRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IMapper _mapper)
        {
            goalRepository = _goalRepository;
            scorecardRepository = _scorecardRepository;
            fiscalYearRepository = _fiscalYearRepository;
            mapper = _mapper;
        }

        public async Task<Response<List<GetAllGoalDto>>> Handle(GetAllGoalRequest request, CancellationToken cancellationToken)
        {
            var entities = await goalRepository.GetAll();
            var scorecards = await scorecardRepository.GetAll();
            var fiscalYearLabels = (await fiscalYearRepository.GetAll()).ToDictionary(f => f.Id, f => f.Label);

            var scorecardById = scorecards.ToDictionary(s => s.Id);

            IEnumerable<Goal> filtered = entities;

            if (request.ScorecardId.HasValue)
            {
                filtered = filtered.Where(g => g.ScorecardId == request.ScorecardId.Value);
            }

            if (request.FiscalYearId.HasValue)
            {
                filtered = filtered.Where(g =>
                    scorecardById.TryGetValue(g.ScorecardId, out var scorecard) &&
                    scorecard.FiscalYearId == request.FiscalYearId.Value);
            }

            var dtos = mapper.Map<List<GetAllGoalDto>>(filtered.ToList());

            foreach (var dto in dtos)
            {
                if (!scorecardById.TryGetValue(dto.ScorecardId, out var scorecard))
                {
                    continue;
                }

                dto.ScorecardName = scorecard.Name;
                dto.FiscalYearName = fiscalYearLabels.GetValueOrDefault(scorecard.FiscalYearId);
            }

            return new Response<List<GetAllGoalDto>>
            {
                Success = true,
                Message = "Goals retrieved successfully.",
                Data = dtos
            };
        }
    }
}
