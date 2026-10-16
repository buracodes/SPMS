using Application.DTOS.FiscalYear;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.FiscalYear.Requests
{
    public class GetFiscalYearRequest : IRequest<Response<List<FiscalYearDto>>>
    {
    }
}
