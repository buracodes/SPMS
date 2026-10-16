using Application.CQRS.Organization.Requests;
using Application.DTOS.Organization;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using AutoMapper;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Organization.Handlers;

public class GetOrganizationProfileRequestHandler : IRequestHandler<GetOrganizationProfileRequest, Response<OrganizationProfileDto>>
{
    private readonly IOrganizationProfileRepository _profileRepository;
    private readonly IMapper _mapper;

    public GetOrganizationProfileRequestHandler(IOrganizationProfileRepository profileRepository, IMapper mapper)
    {
        _profileRepository = profileRepository;
        _mapper = mapper;
    }

    public async Task<Response<OrganizationProfileDto>> Handle(GetOrganizationProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetFullProfileAsync();
        if (profile == null)
        {
            return new Response<OrganizationProfileDto>
            {
                Success = false,
                Message = "Organization profile not found.",
                Data = null
            };
        }

        var dto = _mapper.Map<OrganizationProfileDto>(profile);
        return new Response<OrganizationProfileDto>
        {
            Success = true,
            Message = "Organization profile retrieved successfully.",
            Data = dto
        };
    }
}

public class UpdateOrganizationIdentityCommandHandler : IRequestHandler<UpdateOrganizationIdentityCommand, Response<OrganizationProfileDto>>
{
    private readonly IOrganizationProfileRepository _profileRepository;
    private readonly IMapper _mapper;

    public UpdateOrganizationIdentityCommandHandler(IOrganizationProfileRepository profileRepository, IMapper mapper)
    {
        _profileRepository = profileRepository;
        _mapper = mapper;
    }

    public async Task<Response<OrganizationProfileDto>> Handle(UpdateOrganizationIdentityCommand request, CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetById(request.Dto.Id) ?? await _profileRepository.GetActiveProfileAsync();

        if (profile == null)
        {
            profile = new OrganizationProfile
            {
                Id = Guid.NewGuid(),
                Name = request.Dto.Name,
                Description = request.Dto.Description,
                Vision = request.Dto.Vision,
                Mission = request.Dto.Mission,
                CreatedDate = DateTime.UtcNow
            };
            await _profileRepository.Add(profile);
        }
        else
        {
            profile.Name = request.Dto.Name;
            profile.Description = request.Dto.Description;
            profile.Vision = request.Dto.Vision;
            profile.Mission = request.Dto.Mission;
            profile.UpdatedDate = DateTime.UtcNow;
            await _profileRepository.Update(profile);
        }

        var updatedProfile = await _profileRepository.GetFullProfileAsync();
        var dto = _mapper.Map<OrganizationProfileDto>(updatedProfile);

        return new Response<OrganizationProfileDto>
        {
            Success = true,
            Message = "Organization identity updated successfully.",
            Data = dto
        };
    }
}

// Core Value Handlers
public class CreateCoreValueCommandHandler : IRequestHandler<CreateCoreValueCommand, Response<CoreValueDto>>
{
    private readonly IOrganizationProfileRepository _profileRepository;
    private readonly IGenericRepository<CoreValue> _repository;
    private readonly IMapper _mapper;

    public CreateCoreValueCommandHandler(IOrganizationProfileRepository profileRepository, IGenericRepository<CoreValue> repository, IMapper mapper)
    {
        _profileRepository = profileRepository;
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Response<CoreValueDto>> Handle(CreateCoreValueCommand request, CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetActiveProfileAsync();
        if (profile == null)
        {
            throw new NotFoundException("Organization Profile", "Active");
        }

        var entity = _mapper.Map<CoreValue>(request.Dto);
        entity.Id = Guid.NewGuid();
        entity.OrganizationProfileId = profile.Id;
        entity.CreatedDate = DateTime.UtcNow;

        await _repository.Add(entity);

        var dto = _mapper.Map<CoreValueDto>(entity);
        return new Response<CoreValueDto> { Success = true, Message = "Core value created successfully.", Data = dto };
    }
}

public class UpdateCoreValueCommandHandler : IRequestHandler<UpdateCoreValueCommand, Response<CoreValueDto>>
{
    private readonly IGenericRepository<CoreValue> _repository;
    private readonly IMapper _mapper;

    public UpdateCoreValueCommandHandler(IGenericRepository<CoreValue> repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Response<CoreValueDto>> Handle(UpdateCoreValueCommand request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetById(request.Dto.Id);
        if (entity == null)
        {
            throw new NotFoundException("CoreValue", request.Dto.Id);
        }

        entity.Title = request.Dto.Title;
        entity.Description = request.Dto.Description;
        entity.UpdatedDate = DateTime.UtcNow;

        await _repository.Update(entity);

        var dto = _mapper.Map<CoreValueDto>(entity);
        return new Response<CoreValueDto> { Success = true, Message = "Core value updated successfully.", Data = dto };
    }
}

public class DeleteCoreValueCommandHandler : IRequestHandler<DeleteCoreValueCommand, Response<bool>>
{
    private readonly IGenericRepository<CoreValue> _repository;

    public DeleteCoreValueCommandHandler(IGenericRepository<CoreValue> repository)
    {
        _repository = repository;
    }

    public async Task<Response<bool>> Handle(DeleteCoreValueCommand request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetById(request.Id);
        if (entity == null)
        {
            throw new NotFoundException("CoreValue", request.Id);
        }

        await _repository.Delete(entity);
        return new Response<bool> { Success = true, Message = "Core value deleted successfully.", Data = true };
    }
}

// Strategic Theme Handlers
public class CreateStrategicThemeCommandHandler : IRequestHandler<CreateStrategicThemeCommand, Response<StrategicThemeDto>>
{
    private readonly IOrganizationProfileRepository _profileRepository;
    private readonly IGenericRepository<StrategicTheme> _repository;
    private readonly IMapper _mapper;

    public CreateStrategicThemeCommandHandler(IOrganizationProfileRepository profileRepository, IGenericRepository<StrategicTheme> repository, IMapper mapper)
    {
        _profileRepository = profileRepository;
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Response<StrategicThemeDto>> Handle(CreateStrategicThemeCommand request, CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetActiveProfileAsync();
        if (profile == null) throw new NotFoundException("Organization Profile", "Active");

        var entity = _mapper.Map<StrategicTheme>(request.Dto);
        entity.Id = Guid.NewGuid();
        entity.OrganizationProfileId = profile.Id;
        entity.CreatedDate = DateTime.UtcNow;

        await _repository.Add(entity);

        var dto = _mapper.Map<StrategicThemeDto>(entity);
        return new Response<StrategicThemeDto> { Success = true, Message = "Strategic theme created successfully.", Data = dto };
    }
}

public class UpdateStrategicThemeCommandHandler : IRequestHandler<UpdateStrategicThemeCommand, Response<StrategicThemeDto>>
{
    private readonly IGenericRepository<StrategicTheme> _repository;
    private readonly IMapper _mapper;

    public UpdateStrategicThemeCommandHandler(IGenericRepository<StrategicTheme> repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Response<StrategicThemeDto>> Handle(UpdateStrategicThemeCommand request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetById(request.Dto.Id);
        if (entity == null) throw new NotFoundException("StrategicTheme", request.Dto.Id);

        entity.Title = request.Dto.Title;
        entity.Code = request.Dto.Code;
        entity.Description = request.Dto.Description;
        entity.Color = request.Dto.Color;
        entity.UpdatedDate = DateTime.UtcNow;

        await _repository.Update(entity);

        var dto = _mapper.Map<StrategicThemeDto>(entity);
        return new Response<StrategicThemeDto> { Success = true, Message = "Strategic theme updated successfully.", Data = dto };
    }
}

public class DeleteStrategicThemeCommandHandler : IRequestHandler<DeleteStrategicThemeCommand, Response<bool>>
{
    private readonly IGenericRepository<StrategicTheme> _repository;

    public DeleteStrategicThemeCommandHandler(IGenericRepository<StrategicTheme> repository)
    {
        _repository = repository;
    }

    public async Task<Response<bool>> Handle(DeleteStrategicThemeCommand request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetById(request.Id);
        if (entity == null) throw new NotFoundException("StrategicTheme", request.Id);

        await _repository.Delete(entity);
        return new Response<bool> { Success = true, Message = "Strategic theme deleted successfully.", Data = true };
    }
}

// SWOT Handlers
public class CreateSwotItemCommandHandler : IRequestHandler<CreateSwotItemCommand, Response<SwotItemDto>>
{
    private readonly IOrganizationProfileRepository _profileRepository;
    private readonly IGenericRepository<SwotItem> _repository;
    private readonly IMapper _mapper;

    public CreateSwotItemCommandHandler(IOrganizationProfileRepository profileRepository, IGenericRepository<SwotItem> repository, IMapper mapper)
    {
        _profileRepository = profileRepository;
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Response<SwotItemDto>> Handle(CreateSwotItemCommand request, CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetActiveProfileAsync();
        if (profile == null) throw new NotFoundException("Organization Profile", "Active");

        var entity = _mapper.Map<SwotItem>(request.Dto);
        entity.Id = Guid.NewGuid();
        entity.OrganizationProfileId = profile.Id;
        entity.CreatedDate = DateTime.UtcNow;

        await _repository.Add(entity);

        var dto = _mapper.Map<SwotItemDto>(entity);
        return new Response<SwotItemDto> { Success = true, Message = "SWOT item created successfully.", Data = dto };
    }
}

public class UpdateSwotItemCommandHandler : IRequestHandler<UpdateSwotItemCommand, Response<SwotItemDto>>
{
    private readonly IGenericRepository<SwotItem> _repository;
    private readonly IMapper _mapper;

    public UpdateSwotItemCommandHandler(IGenericRepository<SwotItem> repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Response<SwotItemDto>> Handle(UpdateSwotItemCommand request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetById(request.Dto.Id);
        if (entity == null) throw new NotFoundException("SwotItem", request.Dto.Id);

        entity.Category = request.Dto.Category;
        entity.Title = request.Dto.Title;
        entity.Description = request.Dto.Description;
        entity.Impact = request.Dto.Impact;
        entity.UpdatedDate = DateTime.UtcNow;

        await _repository.Update(entity);

        var dto = _mapper.Map<SwotItemDto>(entity);
        return new Response<SwotItemDto> { Success = true, Message = "SWOT item updated successfully.", Data = dto };
    }
}

public class DeleteSwotItemCommandHandler : IRequestHandler<DeleteSwotItemCommand, Response<bool>>
{
    private readonly IGenericRepository<SwotItem> _repository;

    public DeleteSwotItemCommandHandler(IGenericRepository<SwotItem> repository)
    {
        _repository = repository;
    }

    public async Task<Response<bool>> Handle(DeleteSwotItemCommand request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetById(request.Id);
        if (entity == null) throw new NotFoundException("SwotItem", request.Id);

        await _repository.Delete(entity);
        return new Response<bool> { Success = true, Message = "SWOT item deleted successfully.", Data = true };
    }
}

// PESTEL Handlers
public class CreatePestelItemCommandHandler : IRequestHandler<CreatePestelItemCommand, Response<PestelItemDto>>
{
    private readonly IOrganizationProfileRepository _profileRepository;
    private readonly IGenericRepository<PestelItem> _repository;
    private readonly IMapper _mapper;

    public CreatePestelItemCommandHandler(IOrganizationProfileRepository profileRepository, IGenericRepository<PestelItem> repository, IMapper mapper)
    {
        _profileRepository = profileRepository;
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Response<PestelItemDto>> Handle(CreatePestelItemCommand request, CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetActiveProfileAsync();
        if (profile == null) throw new NotFoundException("Organization Profile", "Active");

        var entity = _mapper.Map<PestelItem>(request.Dto);
        entity.Id = Guid.NewGuid();
        entity.OrganizationProfileId = profile.Id;
        entity.CreatedDate = DateTime.UtcNow;

        await _repository.Add(entity);

        var dto = _mapper.Map<PestelItemDto>(entity);
        return new Response<PestelItemDto> { Success = true, Message = "PESTEL item created successfully.", Data = dto };
    }
}

public class UpdatePestelItemCommandHandler : IRequestHandler<UpdatePestelItemCommand, Response<PestelItemDto>>
{
    private readonly IGenericRepository<PestelItem> _repository;
    private readonly IMapper _mapper;

    public UpdatePestelItemCommandHandler(IGenericRepository<PestelItem> repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Response<PestelItemDto>> Handle(UpdatePestelItemCommand request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetById(request.Dto.Id);
        if (entity == null) throw new NotFoundException("PestelItem", request.Dto.Id);

        entity.Category = request.Dto.Category;
        entity.Title = request.Dto.Title;
        entity.Description = request.Dto.Description;
        entity.Impact = request.Dto.Impact;
        entity.UpdatedDate = DateTime.UtcNow;

        await _repository.Update(entity);

        var dto = _mapper.Map<PestelItemDto>(entity);
        return new Response<PestelItemDto> { Success = true, Message = "PESTEL item updated successfully.", Data = dto };
    }
}

public class DeletePestelItemCommandHandler : IRequestHandler<DeletePestelItemCommand, Response<bool>>
{
    private readonly IGenericRepository<PestelItem> _repository;

    public DeletePestelItemCommandHandler(IGenericRepository<PestelItem> repository)
    {
        _repository = repository;
    }

    public async Task<Response<bool>> Handle(DeletePestelItemCommand request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetById(request.Id);
        if (entity == null) throw new NotFoundException("PestelItem", request.Id);

        await _repository.Delete(entity);
        return new Response<bool> { Success = true, Message = "PESTEL item deleted successfully.", Data = true };
    }
}
