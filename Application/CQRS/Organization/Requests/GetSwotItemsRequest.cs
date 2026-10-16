using Application.DTOS.Organization;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Organization.Requests
{
    public class GetSwotItemsRequest : IRequest<Response<List<SwotItemDto>>>
    {

    }
}
