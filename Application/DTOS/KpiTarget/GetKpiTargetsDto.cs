namespace Application.DTOS.KpiTarget
{
    public class GetKpiTargetsDto
    {
        public Guid KpiId { get; set; }
        public Guid FiscalYearId { get; set; }
        public List<KpiTargetValueDto> Targets { get; set; } = new();
    }
}
