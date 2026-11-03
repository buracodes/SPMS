using Application.Interface.IService;

namespace Application.Tests.TestSupport;

/// <summary>
/// Deterministic HR access snapshot for tests. Defaults to "not an active HR employee", so
/// baseline grants stay off unless a test opts in.
/// </summary>
public sealed class TestEmployeeAccessResolver : IEmployeeAccessResolver
{
    public EmployeeAccessSnapshot Snapshot { get; set; } = EmployeeAccessSnapshot.None;

    public Task<EmployeeAccessSnapshot> ResolveAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Snapshot);

    public static TestEmployeeAccessResolver ActiveInDivision(Guid divisionId) =>
        new() { Snapshot = new EmployeeAccessSnapshot(true, divisionId) };

    public static TestEmployeeAccessResolver ActiveInDivisions(Guid primaryDivisionId, params Guid[] additionalDivisionIds) =>
        new() { Snapshot = new EmployeeAccessSnapshot(true, primaryDivisionId, additionalDivisionIds) };
}
