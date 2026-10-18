using Application.CQRS.FiscalYear.Requests;
using Application.DTOS.FiscalYear;
using Application.Interface.IRepository;
using Application.Responses;
using AutoMapper;
using MediatR;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.FiscalYear.Handler
{
    public class GetFiscalYearHandler : IRequestHandler<GetFiscalYearRequest, Response<List<FiscalYearDto>>>
    {
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IMapper mapper;
        public GetFiscalYearHandler(IFiscalYearRepository _fiscalYearRepository, IScorecardRepository _scorecardRepository, IMapper _mapper)
        {
            fiscalYearRepository = _fiscalYearRepository;
            scorecardRepository = _scorecardRepository;
            mapper = _mapper;
        }

        public async Task<Response<List<FiscalYearDto>>> Handle(GetFiscalYearRequest request, CancellationToken cancellationToken)
        {
            var entity = await fiscalYearRepository.GetActiveAsync(cancellationToken);
            var dtos = mapper.Map<List<FiscalYearDto>>(entity);

            // Flag, in one query, which years already carry scorecards so the UI can lock their
            // dates. The entity has no such column, so AutoMapper leaves HasScorecards false and
            // it is filled in here.
            var yearsWithScorecards = await scorecardRepository.GetFiscalYearIdsWithScorecardsAsync(cancellationToken);
            foreach (var dto in dtos)
            {
                dto.HasScorecards = yearsWithScorecards.Contains(dto.Id);
            }

            return new Response<List<FiscalYearDto>>
            {
                Success = true,
                Message = "Fiscal years retrieved successfully.",
                Data = dtos
            };
        }
    }
}
