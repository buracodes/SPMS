namespace Infrastructure.Jobs
{
    public static class RecurringJobIds
    {
        public const string RefreshTokenCleanup = "system.refresh-token-cleanup";
        public const string ScoreSnapshotReconciliation = "system.score-snapshot-reconciliation";
        public const string FiscalYearClosure = "system.fiscal-year-closure";
    }
}
