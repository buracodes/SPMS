using Application.DTOS.AccessControl;
using Application.DTOS.Division;
using Application.DTOS.HR;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Interface.IRepository
{
    public interface IHRInfoRepository
    {

        Task<List<EmpBranchInfo>> GetEmployeeInfo(Guid EmpId);
        Task<List<Employee>> GetEmployeeListById(List<Guid> emp);
        Task<List<Position>> GetPositionList();
        Task<List<Employee>> GetEmployeeByPositionId(Guid PositionId);
        Task<List<Guid>> GetEmployeeBySuperVisoreId(List<Guid> emp, Guid SupId);
        Task<List<string>> GetEailByPositionId(Guid PositionId);
        Task<string> GetEmailByEmpId(Guid EmpId);
        Task<List<string>> GetEailByEmpId(Guid EmpId);
        Task<string> getDepartment(Guid userId);
        Task<string> getDivision(Guid userId);
        Task<List<EmployeeHRInfoDto>> GetMultipleEmployeesInfo(List<Guid> empIds);
        Task<List<EmployeeHRInfoDto>> GetActiveEmployeesAsync(string? search, int? take, CancellationToken cancellationToken = default);
        Task<EmployeeHRInfoDto?> FindActiveEmployeeByEmailAsync(string email);
        Task<EmployeeHRInfoDto?> FindActiveEmployeeByIdAsync(Guid empId, CancellationToken cancellationToken = default);
        Task<List<HrDivisionDto>> GetDivisionsAsync(CancellationToken cancellationToken = default);
        Task<Employee> getEmployeDetail(Guid EmpId);
        Task<List<Guid>> getEmployeDetailWithSupervisor(Guid Id);


    }
}
