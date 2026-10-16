using System;

namespace Application.Exceptions;

/// <summary>
/// Thrown when a create/update/delete on a fiscal year is disallowed because the year
/// is immutable (closed / archived / already ended) or because the change would affect
/// historical scorecards, targets or actual values. Maps to HTTP 409 Conflict.
/// </summary>
public class FiscalYearMutationException : Exception
{
    public Guid? FiscalYearId { get; }

    public FiscalYearMutationException(Guid? fiscalYearId, string message)
        : base(message)
    {
        FiscalYearId = fiscalYearId;
    }
}