using Application.CQRS.Organization.Requests;
using Application.DTOS.Organization;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using AutoMapper;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Application.CQRS.Organization.Handlers
{
    public class GetSwotItemsRequestHandler : IRequestHandler<GetSwotItemsRequest, Response<List<SwotItemDto>>>
    {
        private readonly IOrganizationProfileRepository profileRepository;
        private readonly IMapper mapper;
        public GetSwotItemsRequestHandler(IOrganizationProfileRepository profileRepository, IMapper mapper)
        {
            this.profileRepository = profileRepository;
            this.mapper = mapper;
        }
        public Task<Response<List<SwotItemDto>>> Handle(GetSwotItemsRequest request, CancellationToken cancellationToken)
        {
            var swotItems = profileRepository.GetSwotItemsAsync();
            if (swotItems == null)
            {
                throw new NotFoundException(nameof(swotItems));
            }

            var dto = mapper.Map<List<SwotItemDto>>(swotItems);
            return Task.FromResult(new Response<List<SwotItemDto>>
            {
                Success = true,
                Message = "Swot items retrieved successfully.",
                Data = dto
            });
        }
    }
}
