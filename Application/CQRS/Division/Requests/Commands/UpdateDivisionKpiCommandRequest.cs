using Application.DTOS.Division;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Division.Requests.Commands
{
    public class UpdateDivisionKpiCommandRequest : IRequest<Response<DivisionKpiDto>>
    {
        public Guid DivisionKpiId { get; set; }

        public Guid? UnitOfMeasureId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Unit { get; set; }
        public decimal? Weight { get; set; }
        public decimal? ToleranceBandPercent { get; set; }
        public KpiOptimizationType? OptimizationType { get; set; }
        public KpiDataSource? DataSource { get; set; }
        public KpiType? KpiType { get; set; }
        public bool? IsCumulative { get; set; }
        public decimal? Baseline { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public string? Formula { get; set; }
        public bool? IsActive { get; set; }

        /// <summary>
        /// FR-904: when supplied, replaces the KPI's monthly/quarterly/annual targets. Null leaves
        /// the existing targets untouched; an empty list clears them.
        /// </summary>
        public List<DivisionKpiTargetInputDto>? Targets { get; set; }
    }
}
