using Application.CQRS.Division.Requests.Commands;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;
using DivisionEntity = Domain.Entities.Division;

namespace Application.CQRS.Division.Handlers.Commands
{
    /// <summary>
    /// Mirrors HR's divisions into the local Division table. The HR OrgStrId becomes the local
    /// Division.Id, so an employee's HR division (and the division claim in the token) resolves to
    /// the same row SPMS plans against. HR is read-only here; only local divisions are written.
    /// Existing local rows with the same id are refreshed, never re-keyed, so plans and KPI owners
    /// that already reference them keep working.
    /// </summary>
    public class SyncDivisionsFromHrCommandHandler
        : IRequestHandler<SyncDivisionsFromHrCommandRequest, Response<DivisionSyncResultDto>>
    {
        private readonly IHRInfoRepository hrInfoRepository;
        private readonly IDivisionRepository divisionRepository;
        private readonly IUnitOfWork unitOfWork;

        public SyncDivisionsFromHrCommandHandler(
            IHRInfoRepository _hrInfoRepository,
            IDivisionRepository _divisionRepository,
            IUnitOfWork _unitOfWork)
        {
            hrInfoRepository = _hrInfoRepository;
            divisionRepository = _divisionRepository;
            unitOfWork = _unitOfWork;
        }

        public async Task<Response<DivisionSyncResultDto>> Handle(
            SyncDivisionsFromHrCommandRequest request,
            CancellationToken cancellationToken)
        {
            var hrDivisions = (await hrInfoRepository.GetDivisionsAsync(cancellationToken))
                .Where(division => division.Id != Guid.Empty && !string.IsNullOrWhiteSpace(division.Name))
                .GroupBy(division => division.Id)
                .Select(group => group.First())
                .ToList();

            var localDivisions = await divisionRepository.GetAll();
            var localById = localDivisions.ToDictionary(division => division.Id);
            var localByName = localDivisions
                .GroupBy(division => division.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var usedCodes = localDivisions
                .Select(division => division.Code)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var result = new DivisionSyncResultDto { TotalHrDivisions = hrDivisions.Count };
            var toCreate = new List<DivisionEntity>();
            var toUpdate = new List<DivisionEntity>();

            foreach (var hrDivision in hrDivisions)
            {
                var name = hrDivision.Name.Trim();
                var isActive = hrDivision.Status == 1;

                if (localById.TryGetValue(hrDivision.Id, out var local))
                {
                    if (localByName.TryGetValue(name, out var nameOwner) && nameOwner.Id != local.Id)
                    {
                        result.Conflicts.Add(new DivisionSyncConflictDto
                        {
                            HrDivisionId = hrDivision.Id,
                            Name = name,
                            Reason = $"The name is already used by local division '{nameOwner.Code}' with a different id."
                        });
                        continue;
                    }

                    var changed = false;
                    if (!string.Equals(local.Name, name, StringComparison.Ordinal))
                    {
                        localByName.Remove(local.Name.Trim());
                        local.Name = name;
                        localByName[name] = local;
                        changed = true;
                    }

                    if (local.IsActive != isActive)
                    {
                        local.IsActive = isActive;
                        changed = true;
                    }

                    if (changed)
                    {
                        toUpdate.Add(local);
                    }
                    else
                    {
                        result.Unchanged++;
                    }

                    continue;
                }

                if (localByName.TryGetValue(name, out var conflict))
                {
                    result.Conflicts.Add(new DivisionSyncConflictDto
                    {
                        HrDivisionId = hrDivision.Id,
                        Name = name,
                        Reason = $"A local division with this name already exists with a different id ('{conflict.Code}')."
                    });
                    continue;
                }

                var division = new DivisionEntity
                {
                    Id = hrDivision.Id,
                    Name = name,
                    Code = BuildCode(hrDivision.Id, usedCodes),
                    IsActive = isActive
                };

                toCreate.Add(division);
                localById[division.Id] = division;
                localByName[name] = division;
            }

            if (toCreate.Count > 0)
            {
                await unitOfWork.AddRangeAsync(toCreate, cancellationToken);
            }

            foreach (var division in toUpdate)
            {
                await divisionRepository.UpdateWithoutSave(division);
            }

            if (toCreate.Count > 0 || toUpdate.Count > 0)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            result.Created = toCreate.Count;
            result.Updated = toUpdate.Count;

            return new Response<DivisionSyncResultDto>
            {
                Success = true,
                Message = $"Division sync completed: {result.Created} created, {result.Updated} updated, " +
                          $"{result.Unchanged} unchanged, {result.Conflicts.Count} skipped.",
                Data = result
            };
        }

        /// <summary>
        /// HR has no division code, so a stable code is derived from the HR id. It never changes
        /// when HR renames the division, and the prefix search keeps it unique against manual codes.
        /// </summary>
        private static string BuildCode(Guid id, ISet<string> usedCodes)
        {
            var raw = id.ToString("N").ToUpperInvariant();
            for (var length = 8; length <= raw.Length; length += 4)
            {
                var candidate = $"HR-{raw[..length]}";
                if (usedCodes.Add(candidate))
                {
                    return candidate;
                }
            }

            throw new BadRequestException("Could not derive a unique division code from the HR division id.");
        }
    }
}
