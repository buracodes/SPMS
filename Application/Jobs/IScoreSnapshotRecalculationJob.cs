using System;
using System.Threading.Tasks;

namespace Application.Jobs
{
    public interface IScoreSnapshotRecalculationJob : ISpmsJob
    {
        Task RecalculateKpiCascadeAsync(Guid kpiId, Guid fiscalPeriodId);
    }
}
