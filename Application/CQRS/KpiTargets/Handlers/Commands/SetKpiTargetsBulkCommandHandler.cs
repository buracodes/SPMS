using Application.CQRS.KpiTargets.Requests.Commands;
using Application.DTOS.KpiTarget;
using Application.Exceptions;
using Application.Responses;
using MediatR;

namespace Application.CQRS.KpiTargets.Handlers.Commands
{
    /// <summary>
    /// Saves every KPI of a bulk request through the single-KPI command, so validation,
    /// period generation and snapshot recalculation stay in one place. A KPI that fails is
    /// reported on its own row and the rest still save.
    /// </summary>
    public class SetKpiTargetsBulkCommandHandler
        : IRequestHandler<SetKpiTargetsBulkCommandRequest, Response<SetKpiTargetsBulkResultDto>>
    {
        private const int MaxKpisPerRequest = 500;

        private readonly IMediator mediator;

        public SetKpiTargetsBulkCommandHandler(IMediator _mediator)
        {
            mediator = _mediator;
        }

        public async Task<Response<SetKpiTargetsBulkResultDto>> Handle(
            SetKpiTargetsBulkCommandRequest request,
            CancellationToken cancellationToken)
        {
            var dto = request.Dto;
            if (dto == null)
            {
                throw new BadRequestException("Target data is required.");
            }

            if (dto.Targets == null || dto.Targets.Count == 0)
            {
                throw new BadRequestException("At least one KPI target is required.");
            }

            if (dto.Targets.Count > MaxKpisPerRequest)
            {
                throw new BadRequestException($"A maximum of {MaxKpisPerRequest} KPIs can be saved in one request.");
            }

            var result = new SetKpiTargetsBulkResultDto();

            foreach (var item in dto.Targets)
            {
                if (item.KpiId == Guid.Empty)
                {
                    result.Results.Add(new KpiTargetSaveResultDto
                    {
                        KpiId = item.KpiId,
                        Success = false,
                        Message = "A KPI id is missing."
                    });
                    continue;
                }

                try
                {
                    await mediator.Send(new SetKpiTargetsCommandRequest
                    {
                        Dto = new SetKpiTargetsDto
                        {
                            KpiId = item.KpiId,
                            FiscalYearId = dto.FiscalYearId,
                            MonthlyTargets = item.MonthlyTargets ?? new List<decimal>(),
                            QuarterlyTargets = item.QuarterlyTargets,
                            AnnualTarget = item.AnnualTarget
                        }
                    }, cancellationToken);

                    result.SavedCount++;
                    result.Results.Add(new KpiTargetSaveResultDto
                    {
                        KpiId = item.KpiId,
                        Success = true,
                        Message = "Saved."
                    });
                }
                catch (ValidationExceptions ex)
                {
                    result.Results.Add(new KpiTargetSaveResultDto
                    {
                        KpiId = item.KpiId,
                        Success = false,
                        Message = Describe(ex)
                    });
                }
                catch (Exception ex)
                {
                    result.Results.Add(new KpiTargetSaveResultDto
                    {
                        KpiId = item.KpiId,
                        Success = false,
                        Message = ex.Message
                    });
                }
            }

            result.FailedCount = result.Results.Count(r => !r.Success);

            var succeeded = result.FailedCount == 0;
            var message = succeeded
                ? $"{result.SavedCount} KPI target(s) saved successfully."
                : $"{result.SavedCount} saved, {result.FailedCount} failed.";

            return new Response<SetKpiTargetsBulkResultDto>
            {
                Success = succeeded,
                Message = message,
                Data = result
            };
        }

        /// <summary>Joins this KPI's validation messages so the row explains what to fix.</summary>
        private static string Describe(ValidationExceptions ex)
        {
            var messages = ex.Errors.Select(e => e.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct();
            var joined = string.Join(" ", messages);
            return string.IsNullOrWhiteSpace(joined) ? ex.Message : joined;
        }
    }
}
