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
        private readonly IMapper mapper;
        public GetFiscalYearHandler(IFiscalYearRepository _fiscalYearRepository, IMapper _mapper)
        {
            fiscalYearRepository = _fiscalYearRepository;
            mapper = _mapper;
        }

        public async Task<Response<List<FiscalYearDto>>> Handle(GetFiscalYearRequest request, CancellationToken cancellationToken)
        {
            var entity = await fiscalYearRepository.GetActiveAsync(cancellationToken);
            var dtos = mapper.Map<List<FiscalYearDto>>(entity);
            return new Response<List<FiscalYearDto>>
            {
                Success = true,
                Message = "Fiscal years retrieved successfully.",
                Data = dtos
            };
        }
    }
}
