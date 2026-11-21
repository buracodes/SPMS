using Application.DTOS.KpiActual;
using Application.Interface.IRepository;
using Application.Services;
using Domain.Entities;

namespace Application.CQRS.KpiActuals;

/// <summary>
/// Shared DTO projection for corporate and division history so both endpoints expose the
/// same fields, revision numbering and audit-reason rules.
/// </summary>
public static class ActualHistoryProjection
{
    public static async Task<ActualValueHistoryDto> BuildAsync(
        IAuditLogEntryRepository auditLogRepository,
        IUserRepository userRepository,
        Guid scorecardId,
        Domain.Entities.Kpi kpi,
        FiscalPeriod period,
        ActualRevisionChain chain,
        CancellationToken cancellationToken = default)
    {
        var reasons = await auditLogRepository.GetCreateReasonsAsync(
            nameof(KpiActualValue),
            chain.Revisions.Select(revision => revision.Value.Id),
            cancellationToken);

        // One query for every recorder in the result set; no per-revision user lookup.
        var names = ActualAttributionNames.Resolve(
            userRepository,
            chain.Revisions.Select(revision => revision.Value.RecordedBy));

        var fiscalYear = kpi.Scorecard?.FiscalYear;
        var periodLabel = $"Month {period.FiscalMonth} · Q{period.Quarter}";

        var result = new ActualValueHistoryDto
        {
            ScorecardId = scorecardId,
            KpiId = kpi.Id,
            KpiName = kpi.Name,
            FiscalPeriodId = period.Id,
            FiscalMonth = period.FiscalMonth,
            Quarter = period.Quarter,
            FiscalPeriodLabel = periodLabel,
            FiscalYearId = period.FiscalYearId,
            FiscalYearLabel = fiscalYear?.Label,
            HasChainIntegrityIssues = chain.HasIntegrityIssues
        };

        foreach (var revision in chain.Revisions)
        {
            var value = revision.Value;
            result.Revisions.Add(new ActualValueDto
            {
                Id = value.Id,
                KpiId = value.KpiId,
                KpiName = kpi.Name,
                FiscalPeriodId = value.FiscalPeriodId,
                FiscalMonth = period.FiscalMonth,
                Quarter = period.Quarter,
                FiscalPeriodLabel = periodLabel,
                FiscalYearId = period.FiscalYearId,
                FiscalYearLabel = fiscalYear?.Label,
                RevisionNumber = revision.RevisionNumber,
                Value = value.Value,
                DataSource = value.DataSource,
                AsOfDate = value.AsOfDate,
                IsCurrent = value.IsCurrent,
                SupersedesActualId = value.SupersedesActualId,
                Note = value.Note,
                RecordedBy = value.RecordedBy,
                EnteredByName = value.RecordedBy is Guid id && names.TryGetValue(id, out var name)
                    ? name
                    : null,
                CreatedDate = value.CreatedDate,

                // Only a replacement revision can carry a correction reason. The reason is
                // read from that revision's Create audit row, never from the predecessor's
                // Update row or from a timestamp-based guess.
                CorrectionReason = value.SupersedesActualId.HasValue
                    && reasons.TryGetValue(value.Id, out var reason)
                        ? reason
                        : null
            });
        }

        return result;
    }
}
