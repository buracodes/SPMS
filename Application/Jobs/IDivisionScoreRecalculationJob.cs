using System.Threading.Tasks;

namespace Application.Jobs;

public interface IDivisionScoreRecalculationJob : ISpmsJob
{
    Task RecalculateDivisionGoalAsync(Guid divisionGoalId, Guid fiscalPeriodId);
}
