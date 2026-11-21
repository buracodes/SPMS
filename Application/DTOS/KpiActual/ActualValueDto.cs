using Domain.Enums;

namespace Application.DTOS.KpiActual
{
    public class ActualValueDto
    {
        public Guid Id { get; set; }
        public Guid KpiId { get; set; }

        /// <summary>KPI display name resolved once for the history response.</summary>
        public string? KpiName { get; set; }

        public Guid FiscalPeriodId { get; set; }
        public int FiscalMonth { get; set; }
        public int Quarter { get; set; }

        /// <summary>Human-readable period label derived from the fiscal period, not stored on the revision.</summary>
        public string? FiscalPeriodLabel { get; set; }

        public Guid FiscalYearId { get; set; }

        /// <summary>Fiscal year label from the scorecard's fiscal year.</summary>
        public string? FiscalYearLabel { get; set; }

        /// <summary>Position in the SupersedesActualId chain: oldest = 1, newest = chain length.</summary>
        public int RevisionNumber { get; set; }

        public decimal Value { get; set; }
        public KpiDataSource DataSource { get; set; }
        public DateTime AsOfDate { get; set; }
        public bool IsCurrent { get; set; }

        /// <summary>The previous revision superseded by this revision, when supplied.</summary>
        public Guid? SupersedesActualId { get; set; }

        public string? Note { get; set; }

        /// <summary>Attribution of the original entry, from the authenticated principal.</summary>
        public Guid? RecordedBy { get; set; }

        /// <summary>Display name resolved in a single batched lookup for the result set.</summary>
        public string? EnteredByName { get; set; }

        /// <summary>Timestamp the revision was entered (server clock).</summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Reason from the Create audit row for this replacement revision. The first revision
        /// and rows without a matching Create audit row deliberately return null.
        /// </summary>
        public string? CorrectionReason { get; set; }
    }
}
