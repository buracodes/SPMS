using Application.DTOS.AccessControl;
using Application.DTOS.Division;
using Application.DTOS.HR;
using Application.Interface.IRepository;
using Domain.Entities;

namespace Application.Tests.TestSupport;

/// <summary>
/// HR stub for access-control tests: the local user table is the only source of assignable
/// identities here, so every HR lookup misses and no shadow account is provisioned.
/// </summary>
internal sealed class TestHrInfoRepository : IHRInfoRepository
{
    public Task<EmployeeHRInfoDto?> FindActiveEmployeeByIdAsync(Guid empId, CancellationToken cancellationToken = default)
        => Task.FromResult<EmployeeHRInfoDto?>(null);

    public Task<EmployeeHRInfoDto?> FindActiveEmployeeByEmailAsync(string email)
        => Task.FromResult<EmployeeHRInfoDto?>(null);

    public Task<List<EmployeeHRInfoDto>> GetActiveEmployeesAsync(string? search, int? take, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<EmployeeHRInfoDto>());

    public Task<List<EmployeeHRInfoDto>> GetMultipleEmployeesInfo(List<Guid> empIds)
        => Task.FromResult(new List<EmployeeHRInfoDto>());

    public Task<List<HrDivisionDto>> GetDivisionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new List<HrDivisionDto>());

    public Task<List<EmpBranchInfo>> GetEmployeeInfo(Guid EmpId) => NotSupported<List<EmpBranchInfo>>();

    public Task<List<Employee>> GetEmployeeListById(List<Guid> emp) => NotSupported<List<Employee>>();

    public Task<List<Position>> GetPositionList() => NotSupported<List<Position>>();

    public Task<List<Employee>> GetEmployeeByPositionId(Guid PositionId) => NotSupported<List<Employee>>();

    public Task<List<Guid>> GetEmployeeBySuperVisoreId(List<Guid> emp, Guid SupId) => NotSupported<List<Guid>>();

    public Task<List<string>> GetEailByPositionId(Guid PositionId) => NotSupported<List<string>>();

    public Task<string> GetEmailByEmpId(Guid EmpId) => NotSupported<string>();

    public Task<List<string>> GetEailByEmpId(Guid EmpId) => NotSupported<List<string>>();

    public Task<string> getDepartment(Guid userId) => NotSupported<string>();

    public Task<string> getDivision(Guid userId) => NotSupported<string>();

    public Task<Employee> getEmployeDetail(Guid EmpId) => NotSupported<Employee>();

    public Task<List<Guid>> getEmployeDetailWithSupervisor(Guid Id) => NotSupported<List<Guid>>();

    private static Task<T> NotSupported<T>() => throw new NotSupportedException("The HR source is not used by this scenario.");
}
