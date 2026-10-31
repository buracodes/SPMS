using Application.DTOS.AccessControl;
using Application.DTOS.Division;
using Application.DTOS.HR;
using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Repositories
{
    public class HRInfoRepository : IHRInfoRepository
    {
        private readonly hrDbContext context;

        public HRInfoRepository(hrDbContext context)
        {
            this.context = context;
        }

        public async Task<List<EmpBranchInfo>> GetEmployeeInfo(Guid EmpId)
        {
            var sql = @"
                       select ad.Email,p.BranchId,b.name BranchName,e.EmpId,p.Id PositionId,pl.name PositionLevel,lp.Name PositionName from Position p
                       inner join EmployeePosition em on em.PositionId = p.Id
                       inner join Employee e on e.EmpId = em.EmpId
                       inner join Branch b on p.BranchId = b.Id
                       inner join ListOfPositions lp on lp.Id = p.ListOfPositionId
                       inner join PositionalLevel pl on pl.Id = p.PLevelId
                       inner join Address ad on ad.EmpId = e.EmpId

                       where em.CurrentStatus = 1 and e.EmpID = {0}";



            var rights = await context.Database
                            .SqlQueryRaw<EmpBranchInfo>(sql, EmpId)
                            .ToListAsync();


            //then using rights.supervisorId  then to positions.Id table then employeePosition.positionId then to employeePosition.empId then  find from Employee

            return rights;
        }
        public async Task<List<Employee>> GetEmployeeListById(List<Guid> emp)
        {

            if (emp == null || !emp.Any())
            {
                return new List<Employee>();
            }

            var paramNames = emp.Select((x, i) => $"{{{i}}}").ToList();
            var EmpId = emp.Cast<object>().ToArray();

            var sql = $@"
                   select e.*, ad.Email,p.BranchId,b.name BranchName,
                          p.Id PositionId,pl.name PositionLevel,lp.Name PositionName
                   from Position p
                   inner join EmployeePosition em on em.PositionId = p.Id
                   inner join Employee e on e.EmpId = em.EmpId
                   inner join Branch b on p.BranchId = b.Id
                   inner join ListOfPositions lp on lp.Id = p.ListOfPositionId
                   inner join PositionalLevel pl on pl.Id = p.PLevelId
                   inner join Address ad on ad.EmpId = e.EmpId
                   where em.CurrentStatus = 1
                   and e.EmpID IN ({string.Join(",", paramNames)})";

            var rights = await context.Database
                .SqlQueryRaw<Employee>(sql, EmpId)
                .ToListAsync();

            return rights;
        }

        public async Task<List<Guid>> GetEmployeeBySuperVisoreId(List<Guid> emp, Guid SupId)
        {
            //then using rights.supervisorId  then to positions.Id table then employeePosition.positionId then to employeePosition.empId then  find from Employee

            if (emp == null || !emp.Any())
            {
                return new List<Guid>();
            }

            var paramNames = emp.Select((x, i) => $"{{{i}}}").ToList();
            var EmpId = emp.Cast<object>().ToArray();

            var sql = $@"
                   select e.FirstName,e.EmpId, ad.Email,p.BranchId,b.name BranchName,
                          p.Id PositionId,pl.name PositionLevel,lp.Name PositionName,p.SupervisiorId
                   from Position p
                   inner join EmployeePosition em on em.PositionId = p.Id
                   inner join Employee e on e.EmpId = em.EmpId
                   inner join Branch b on p.BranchId = b.Id
                   inner join ListOfPositions lp on lp.Id = p.ListOfPositionId
                   inner join PositionalLevel pl on pl.Id = p.PLevelId
                   inner join Address ad on ad.EmpId = e.EmpId
                   where em.CurrentStatus = 1
                   and e.EmpID IN ({string.Join(",", paramNames)})";

            var rights = await context.Database
                .SqlQueryRaw<EmpBySuperVisor>(sql, EmpId)
                .ToListAsync();
            var positionIds = rights
    .Where(r => r.SupervisiorId.HasValue)
    .Select(r => new EmpPositionInfo
    {
        SupervisiorId = r.SupervisiorId.Value,
        BranchId = r.BranchId,
        EmpId = r.EmpId.Value
    })
    .Distinct()
    .ToList();
            List<EmpMappedWithSuperv> alld = new List<EmpMappedWithSuperv>();
            var byEmpPosition = @"SELECT  e.EmpId FROM EmployeePosition e WHERE PositionId={0}";
            foreach (var ids in positionIds)
            {

                var rightssup = context.Database
                   .SqlQueryRaw<Empids>(byEmpPosition, ids.SupervisiorId)
                   .FirstOrDefault();

                alld.Add(new EmpMappedWithSuperv
                {
                    EmpId = ids.EmpId,
                    SupervisiorEmpId = rightssup.EmpId
                });

            }
            var userfiltered = alld.Where(u => u.SupervisiorEmpId == SupId).ToList();

            //    var user = "6928c5e5-4022-4fc0-ac5a-29343b7ae8c8";
            //    var sss = @"SELECT ep.EmpId
            //FROM EmployeePosition ep
            //INNER JOIN Position p ON p.Id = ep.PositionId
            //WHERE p.SupervisiorId IN (
            //    SELECT PositionId 
            //    FROM EmployeePosition 
            //    WHERE EmpId = {0} AND CurrentStatus = 1
            //)
            //AND ep.CurrentStatus = 1";

            //    var undersup= context.Database
            //           .SqlQueryRaw<Empids>(sss, Guid.Parse(user))
            //           .ToList();
            //    var sqd = @"SELECT * FROM Employee WHERE EmpId={0}";
            //    List<Employee> empss = new List<Employee>();
            //    foreach (var id in undersup)
            //    {
            //        var ressq = context.Database.SqlQueryRaw<Employee>(sqd, id.EmpId).FirstOrDefault();
            //        empss.Add(ressq);
            //    }
            var addds = userfiltered.Select(s => s.EmpId).ToList();
            return addds;
        }


        public async Task<List<Position>> GetPositionList()
        {
            var sql = @"
                       select p.Id PositionId,pl.name PositionLevel ,lp.Name PositionName,b.name BranchName,b.Id BranchId from Position p
                           inner join PositionalLevel pl on pl.Id = p.PLevelId
                           inner join Branch b on b.Id = p.BranchId
                           inner join OrganizationalUnit ou on ou.Id = p.OrgUnitId
                           inner join ListOfPositions lp on p.ListOfPositionId = lp.Id
                           where pl.name in('Manager') and
                           p.Id in (SELECT SupervisiorId FROM Position)
                           order by pl.name";

            //var sqlEmpPosi = @"select e.*, ep.* from EmployeePosition ep INNER JOIN Employee e on ep.EmpId=e.EmpId
            //                   where PositionId={0}";

            //var Emp = await context.Database
            //                .SqlQueryRaw<Employee>(sqlEmpPosi)
            //                .ToListAsync();
            var rights = await context.Database
                            .SqlQueryRaw<Position>(sql)
                            .ToListAsync();

            return rights;
        }

        public async Task<List<Employee>> GetEmployeeByPositionId(Guid PositionId)
        {
            var sql = @"
                       select e.* from Employee e
                           inner join EmployeePosition ep on Ep.EmpId = E.EmpId
                           where ep.PositionId = {0} and ep.CurrentStatus in (1, 14) and ep.Status = 1";

            var rights = await context.Database
                            .SqlQueryRaw<Employee>(sql, PositionId)
                            .ToListAsync();

            return rights;
        }


        public async Task<List<string>> GetEailByPositionId(Guid PositionId)
        {
            var sql = @"
                       select a.Email from Address a
                               inner join EmployeePosition ep on Ep.EmpId = a.EmpId
                               where ep.PositionId = {0} and ep.CurrentStatus in (1, 14) and ep.Status = 1 ";

            var rights = await context.Database
                            .SqlQueryRaw<string>(sql, PositionId)
                            .ToListAsync();

            return rights;
        }

        public async Task<string> GetEmailByEmpId(Guid EmpId)
        {
            var sql = @"
                       select a.Email from Address a
                               inner join EmployeePosition ep on Ep.EmpId = a.EmpId
                               where a.EmpId = {0} and ep.CurrentStatus in (1, 14) and ep.Status = 1 ";

            var rights = await context.Database
                            .SqlQueryRaw<string>(sql, EmpId)
                            .FirstOrDefaultAsync();

            return rights;
        }

        public async Task<List<string>> GetEailByEmpId(Guid EmpId)
        {
            var sql = @"
                       select a.Email from Address a
                               where EmpId = {0}";

            var rights = await context.Database
                            .SqlQueryRaw<string>(sql, EmpId)
                            .ToListAsync();

            return rights;
        }
        public async Task<List<EmployeeHRInfoDto>> GetMultipleEmployeesInfo(List<Guid> empIds)
        {
            if (empIds == null || !empIds.Any())
                return new List<EmployeeHRInfoDto>();

            var parameters = empIds.Select((x, i) => $"{{{i}}}").ToList();
            var empIdParams = empIds.Cast<object>().ToArray();

            var sql = $@"
   SELECT 
       e.EmpId,
       e.FirstName,
       e.MiddleName,
       e.LastName,
       ad.Email,
       p.Id as PositionId,
       lp.Name as PositionName,
       pl.name as PositionLevel,
       os.Id as DivisionId,
       os.Name as DivisionName,
       parentOs.Id as DepartmentId,
       parentOs.Name as DepartmentName,
       b.Id as BranchId,
       b.name as BranchName
   FROM Employee e
   OUTER APPLY (SELECT TOP 1 a.Email FROM Address a
                WHERE a.EmpId = e.EmpId AND a.Status = 1
                ORDER BY a.PId DESC) ad
   INNER JOIN EmployeePosition ep ON ep.EmpId = e.EmpId
   INNER JOIN Position p ON p.Id = ep.PositionId
   INNER JOIN ListOfPositions lp ON lp.Id = p.ListOfPositionId
   INNER JOIN PositionalLevel pl ON pl.Id = p.PLevelId
   INNER JOIN Branch b ON b.Id = p.BranchId
   INNER JOIN OrganizationalStracture os ON os.Id = p.OrgStrId
   LEFT JOIN OrganizationalStracture parentOs ON parentOs.Id = os.ImmediateSuppervisor
   WHERE ep.CurrentStatus = 1 
       AND e.EmpId IN ({string.Join(",", parameters)})";

            var res = await context.Database
                .SqlQueryRaw<EmployeeHRInfoDto>(sql, empIdParams)
                .ToListAsync();
            return res;
        }

        // Active employees are the only assignable population; e.Status = 1 matches the legacy
        // HR picker and ep.CurrentStatus = 1 mirrors the login lookup. An employee can hold more
        // than one active position, so the caller receives one row per employee.
        public async Task<List<EmployeeHRInfoDto>> GetActiveEmployeesAsync(
            string? search,
            int? take,
            CancellationToken cancellationToken = default)
        {
            var top = take is > 0 ? $"TOP ({take.Value})" : string.Empty;
            var sql = $@"
   SELECT {top}
       e.EmpId,
       e.IdCode,
       e.FirstName,
       e.MiddleName,
       e.LastName,
       ad.Email,
       p.Id as PositionId,
       lp.Name as PositionName,
       pl.name as PositionLevel,
       os.Id as DivisionId,
       os.Name as DivisionName,
       parentOs.Id as DepartmentId,
       parentOs.Name as DepartmentName,
       b.Id as BranchId,
       b.name as BranchName
   FROM Employee e
   OUTER APPLY (SELECT TOP 1 a.Email FROM Address a
                WHERE a.EmpId = e.EmpId AND a.Status = 1
                ORDER BY a.PId DESC) ad
   INNER JOIN EmployeePosition ep ON ep.EmpId = e.EmpId
   INNER JOIN Position p ON p.Id = ep.PositionId
   INNER JOIN ListOfPositions lp ON lp.Id = p.ListOfPositionId
   INNER JOIN PositionalLevel pl ON pl.Id = p.PLevelId
   INNER JOIN Branch b ON b.Id = p.BranchId
   INNER JOIN OrganizationalStracture os ON os.Id = p.OrgStrId
   LEFT JOIN OrganizationalStracture parentOs ON parentOs.Id = os.ImmediateSuppervisor
   WHERE e.Status = 1
       AND ep.CurrentStatus = 1
       AND ep.Status = 1
       AND ({{0}} IS NULL
            OR e.FirstName LIKE '%' + {{0}} + '%'
            OR e.MiddleName LIKE '%' + {{0}} + '%'
            OR e.LastName LIKE '%' + {{0}} + '%'
            OR e.IdCode LIKE '%' + {{0}} + '%'
            OR ad.Email LIKE '%' + {{0}} + '%')
   ORDER BY e.FirstName, e.MiddleName, e.LastName";

            var rows = await context.Database
                .SqlQueryRaw<EmployeeHRInfoDto>(sql, search?.Trim() ?? (object)DBNull.Value)
                .ToListAsync(cancellationToken);

            return rows
                .Where(row => row.EmpId.HasValue)
                .GroupBy(row => row.EmpId!.Value)
                .Select(group => group.First())
                .ToList();
        }

        // The AD mail address is the only reliable AD-to-HR link (Employee.AdId holds a
        // placeholder). The active EmployeePosition row is what the HR screens use to decide
        // where an employee currently sits. This returns null for a missing or duplicated
        // match so the login path can fail closed rather than bind to the wrong employee.
        public async Task<EmployeeHRInfoDto?> FindActiveEmployeeByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            var sql = @"
   SELECT 
       e.EmpId,
       e.FirstName,
       e.MiddleName,
       e.LastName,
       ad.Email,
       p.Id as PositionId,
       lp.Name as PositionName,
       pl.name as PositionLevel,
       os.Id as DivisionId,
       os.Name as DivisionName,
       parentOs.Id as DepartmentId,
       parentOs.Name as DepartmentName,
       b.Id as BranchId,
       b.name as BranchName
   FROM Employee e
   OUTER APPLY (SELECT TOP 1 a.Email FROM Address a
                WHERE a.EmpId = e.EmpId AND a.Status = 1
                ORDER BY a.PId DESC) ad
   INNER JOIN EmployeePosition ep ON ep.EmpId = e.EmpId
   INNER JOIN Position p ON p.Id = ep.PositionId
   INNER JOIN ListOfPositions lp ON lp.Id = p.ListOfPositionId
   INNER JOIN PositionalLevel pl ON pl.Id = p.PLevelId
   INNER JOIN Branch b ON b.Id = p.BranchId
   INNER JOIN OrganizationalStracture os ON os.Id = p.OrgStrId
   LEFT JOIN OrganizationalStracture parentOs ON parentOs.Id = os.ImmediateSuppervisor
   WHERE ep.CurrentStatus = 1
       AND ad.Email = {0}";

            var rows = await context.Database
                .SqlQueryRaw<EmployeeHRInfoDto>(sql, email.Trim())
                .ToListAsync();

            var distinctEmployees = rows
                .GroupBy(row => row.EmpId)
                .ToList();

            return distinctEmployees.Count == 1 ? distinctEmployees[0].First() : null;
        }

        // Used when an administrator grants access to a directory employee before their first
        // sign-in. The same active-position rules as the directory listing apply, so an employee
        // who left HR cannot be used to create a new SPMS account.
        public async Task<EmployeeHRInfoDto?> FindActiveEmployeeByIdAsync(
            Guid empId,
            CancellationToken cancellationToken = default)
        {
            var sql = @"
   SELECT 
       e.EmpId,
       e.IdCode,
       e.FirstName,
       e.MiddleName,
       e.LastName,
       ad.Email,
       p.Id as PositionId,
       lp.Name as PositionName,
       pl.name as PositionLevel,
       os.Id as DivisionId,
       os.Name as DivisionName,
       parentOs.Id as DepartmentId,
       parentOs.Name as DepartmentName,
       b.Id as BranchId,
       b.name as BranchName
   FROM Employee e
   OUTER APPLY (SELECT TOP 1 a.Email FROM Address a
                WHERE a.EmpId = e.EmpId AND a.Status = 1
                ORDER BY a.PId DESC) ad
   INNER JOIN EmployeePosition ep ON ep.EmpId = e.EmpId
   INNER JOIN Position p ON p.Id = ep.PositionId
   INNER JOIN ListOfPositions lp ON lp.Id = p.ListOfPositionId
   INNER JOIN PositionalLevel pl ON pl.Id = p.PLevelId
   INNER JOIN Branch b ON b.Id = p.BranchId
   INNER JOIN OrganizationalStracture os ON os.Id = p.OrgStrId
   LEFT JOIN OrganizationalStracture parentOs ON parentOs.Id = os.ImmediateSuppervisor
   WHERE e.EmpId = {0}
       AND e.Status = 1
       AND ep.CurrentStatus = 1
       AND ep.Status = 1";

            var rows = await context.Database
                .SqlQueryRaw<EmployeeHRInfoDto>(sql, empId)
                .ToListAsync(cancellationToken);

            return rows.FirstOrDefault(row => row.EmpId == empId);
        }

        // A position's OrgStrId is what the employee queries report as the division, so the
        // organizational nodes positions point at are the divisions HR actually organizes work
        // under. Reading Position (not EmployeePosition) also finds a division HR has created
        // before its first employee is posted there.
        public async Task<List<HrDivisionDto>> GetDivisionsAsync(CancellationToken cancellationToken = default)
        {
            var sql = @"
   SELECT DISTINCT
       os.Id AS Id,
       os.Name AS Name
   FROM OrganizationalStracture os
   INNER JOIN Position p ON p.OrgStrId = os.Id
   WHERE os.Name IS NOT NULL
       AND LTRIM(RTRIM(os.Name)) <> ''
   ORDER BY os.Name";

            var rows = await context.Database
                .SqlQueryRaw<HrDivisionDto>(sql)
                .ToListAsync(cancellationToken);

            return rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Name))
                .ToList();
        }

        public async Task<string> getDepartment(Guid userId)
        {
            var sqlDi = @"SELECT DISTINCT parentOs.Name AS Value 
                           FROM EmployeePosition ep
                           INNER JOIN Position p ON p.Id = ep.PositionId
                           INNER JOIN OrganizationalStracture os ON os.Id = p.OrgStrId
                           LEFT JOIN OrganizationalStracture parentOs 
                               ON parentOs.Id = os.ImmediateSuppervisor
                           WHERE ep.CurrentStatus = 1 AND ep.EmpId={0}";





            var department = await context.Database.SqlQueryRaw<string>(sqlDi, userId).FirstOrDefaultAsync();
            //var division = _contexthr.Database.SqlQueryRaw<string>(sqlDiv, item.RequestedBy).FirstOrDefault();
            return department;

        }
        public async Task<string> getDivision(Guid userId)
        {
            var sqlDiv = @"SELECT DISTINCT os.Name  AS Value 
   FROM EmployeePosition ep
   INNER JOIN Position p ON p.Id = ep.PositionId
   INNER JOIN OrganizationalStracture os ON os.Id = p.OrgStrId
   WHERE ep.CurrentStatus = 1 AND ep.EmpId={0}";

            var division = await context.Database.SqlQueryRaw<string>(sqlDiv, userId).FirstOrDefaultAsync();
            return division;
        }

        public async Task<Employee> getEmployeDetail(Guid EmpId)
        {


            var sql = @"SELECT e.* FROM Employee e WHERE EmpId ={0}";

            var res = await context.Database.SqlQueryRaw<Employee>(sql, EmpId).FirstOrDefaultAsync();
            return res;
        }


        public async Task<List<Guid>> getEmployeDetailWithSupervisor(Guid Id)
        {

            var sql = @"
       SELECT ep.EmpId
       FROM EmployeePosition ep
       INNER JOIN Position p ON p.Id = ep.PositionId
       WHERE p.SupervisiorId IN (
           SELECT PositionId 
           FROM EmployeePosition 
           WHERE EmpId = {0} AND CurrentStatus = 1
       )
       AND ep.CurrentStatus = 1";




            var res = await context.Database.SqlQueryRaw<Guid>(sql, Id).ToListAsync();
            return res;
        }



    }
}
