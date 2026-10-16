namespace Application.DTOS.KpiActual
{
    public class GetActualValuesDto
    {
        public Guid KpiId { get; set; }
        public Guid FiscalYearId { get; set; }
        public List<ActualValueDto> Values { get; set; } = new();
    }
}
