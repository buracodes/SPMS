using Application.Interface.IService;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Seed
{
    public static class SpmsDbSeeder
    {
        public static async Task SeedAsync(
            SpmsDbContext context,
            IPasswordHasher passwordHasher,
            DefaultAdminOptions defaultAdmin)
        {
            await SeedPermissionsAsync(context);
            await SeedRolesAsync(context);
            await SeedRolePermissionsAsync(context);
            await SeedDefaultAdminAsync(context, passwordHasher, defaultAdmin);
            await SeedRoleUsersAsync(context, passwordHasher);
            await SeedOrganizationProfileAsync(context);
            await SeedDivisionAccountsAsync(context, passwordHasher);
            await SeedRagThresholdsAsync(context);
        }

        private static async Task SeedPermissionsAsync(SpmsDbContext context)
        {
            var existingNames = await context.Permissions.Select(p => p.Name).ToHashSetAsync();

            var permissions = Permissions.All
                .Where(p => !existingNames.Contains(p.Name))
                .Select(p => new Permission
                {
                    Id = Guid.NewGuid(),
                    Name = p.Name,
                    Module = p.Module,
                    Description = p.Description
                })
                .ToList();

            if (permissions.Count == 0)
            {
                return;
            }

            await context.Permissions.AddRangeAsync(permissions);
            await context.SaveChangesAsync();
        }

        private static async Task SeedRolesAsync(SpmsDbContext context)
        {
            var existingNames = await context.Roles.Select(r => r.Name).ToListAsync();

            var roles = Roles.All
                .Where(name => !existingNames.Contains(name))
                .Select(name => new Role
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Description = $"Default {name} role."
                })
                .ToList();

            if (roles.Count == 0)
            {
                return;
            }

            await context.Roles.AddRangeAsync(roles);
            await context.SaveChangesAsync();
        }

        private static async Task SeedRolePermissionsAsync(SpmsDbContext context)
        {
            var roles = await context.Roles
                .Include(r => r.RolePermissions)
                .ToListAsync();
            var permissions = await context.Permissions.ToListAsync();

            var roleByName = roles.ToDictionary(r => r.Name);
            var permissionByName = permissions.ToDictionary(p => p.Name);

            var toAdd = new List<RolePermission>();
            foreach (var mapping in RolePermissionMap)
            {
                if (!roleByName.TryGetValue(mapping.Key, out var role))
                {
                    continue;
                }

                var existingPermissionIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
                foreach (var permissionName in mapping.Value)
                {
                    if (!permissionByName.TryGetValue(permissionName, out var permission))
                    {
                        continue;
                    }

                    if (!existingPermissionIds.Contains(permission.Id))
                    {
                        toAdd.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
                    }
                }
            }

            if (toAdd.Count > 0)
            {
                await context.RolePermissions.AddRangeAsync(toAdd);
                await context.SaveChangesAsync();
            }
        }

        private static async Task SeedRoleUsersAsync(SpmsDbContext context, IPasswordHasher passwordHasher)
        {
            var roles = await context.Roles.ToListAsync();
            var existingUsernames = await context.Users.Select(u => u.Username).ToListAsync();

            var roleUsers = new[]
            {
                new { RoleName = Roles.PowerUser, Username = "poweruser", FullName = "Power User", Email = "poweruser@ecx.com.et" },
                new { RoleName = Roles.Executive, Username = "executive", FullName = "Executive", Email = "executive@ecx.com.et" },
                new { RoleName = Roles.DataInputUser, Username = "datainput", FullName = "Data Input User", Email = "datainput@ecx.com.et" },
                new { RoleName = Roles.ViewOnly, Username = "viewonly", FullName = "View Only", Email = "viewonly@ecx.com.et" }
            };

            var usersToAdd = new List<User>();
            foreach (var roleUser in roleUsers)
            {
                if (existingUsernames.Contains(roleUser.Username))
                {
                    continue;
                }

                var role = roles.FirstOrDefault(r => r.Name == roleUser.RoleName);
                if (role == null)
                {
                    continue;
                }

                var userId = Guid.NewGuid();
                usersToAdd.Add(new User
                {
                    Id = userId,
                    Username = roleUser.Username,
                    FullName = roleUser.FullName,
                    Email = roleUser.Email,
                    PasswordHash = passwordHasher.HashPassword(roleUser.Username),
                    IsActive = true,
                    UserRoles = new List<UserRole>
                    {
                        new UserRole { UserId = userId, RoleId = role.Id }
                    }
                });

                existingUsernames.Add(roleUser.Username);
            }

            if (usersToAdd.Count > 0)
            {
                await context.Users.AddRangeAsync(usersToAdd);
                await context.SaveChangesAsync();
            }
        }

        private static async Task SeedDefaultAdminAsync(
            SpmsDbContext context,
            IPasswordHasher passwordHasher,
            DefaultAdminOptions defaultAdmin)
        {
            if (await context.Users.AnyAsync())
            {
                return;
            }

            var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == Roles.Admin);

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = defaultAdmin.Username,
                FullName = defaultAdmin.FullName,
                Email = defaultAdmin.Email,
                PasswordHash = passwordHasher.HashPassword(defaultAdmin.Password),
                IsActive = true,
                UserRoles = new List<UserRole>()
            };

            if (adminRole != null)
            {
                user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = adminRole.Id });
            }

            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();
        }

        private static async Task SeedOrganizationProfileAsync(SpmsDbContext context)
        {
            if (await context.OrganizationProfiles.AnyAsync())
            {
                return;
            }

            var profile = new OrganizationProfile
            {
                Id = Guid.NewGuid(),
                Name = "Ethiopian Commodity Exchange (ECX)",
                Description = "A state-of-the-art marketing system that ensures market integrity, efficiency, and transparency for agricultural commodities.",
                Vision = "To be a world-class exchange of choice in Africa.",
                Mission = "To provide a modern, efficient, transparent, and accessible commodity market platform to serve all market actors.",
                CreatedDate = DateTime.UtcNow,
                CoreValues = new List<CoreValue>
                {
                    new CoreValue { Id = Guid.NewGuid(), Title = "Integrity", Description = "Upholding complete transparency and trust in commodity trade." },
                    new CoreValue { Id = Guid.NewGuid(), Title = "Efficiency", Description = "Maximizing speed and accuracy in grading, storage, and clearing." },
                    new CoreValue { Id = Guid.NewGuid(), Title = "Accessibility", Description = "Empowering smallholder farmers and market participants across Ethiopia." },
                    new CoreValue { Id = Guid.NewGuid(), Title = "Innovation", Description = "Pioneering technology-driven trading platforms and market data dissemination." }
                },
                StrategicThemes = new List<StrategicTheme>
                {
                    new StrategicTheme { Id = Guid.NewGuid(), Title = "Operational Excellence", Code = "ST-1", Description = "Streamlining warehouse management, grading accuracy, and trade settlement.", Color = "#10B981" },
                    new StrategicTheme { Id = Guid.NewGuid(), Title = "Market Expansion & Volume Growth", Code = "ST-2", Description = "Introducing new commodities, contracts, and electronic trading channels.", Color = "#3B82F6" },
                    new StrategicTheme { Id = Guid.NewGuid(), Title = "Digital Transformation", Code = "ST-3", Description = "Modernizing IT infrastructure, automated integrations, and real-time analytics.", Color = "#8B5CF6" },
                    new StrategicTheme { Id = Guid.NewGuid(), Title = "Stakeholder Empowerment", Code = "ST-4", Description = "Enhancing capacity building, fair pricing, and market transparency.", Color = "#F59E0B" }
                },
                SwotItems = new List<SwotItem>
                {
                    new SwotItem { Id = Guid.NewGuid(), Category = Domain.Enums.SwotCategory.Strength, Title = "Robust Physical Warehouse Network", Description = "Over 20 regional warehouse sites with standardized grading and storage.", Impact = Domain.Enums.ImpactLevel.High },
                    new SwotItem { Id = Guid.NewGuid(), Category = Domain.Enums.SwotCategory.Strength, Title = "T+1 Clearing & Settlement Integrity", Description = "Zero default rate in financial settlement since inception.", Impact = Domain.Enums.ImpactLevel.High },
                    new SwotItem { Id = Guid.NewGuid(), Category = Domain.Enums.SwotCategory.Weakness, Title = "Manual Paperwork Bottlenecks", Description = "Legacy manual entry during peak harvest season delays trade reporting.", Impact = Domain.Enums.ImpactLevel.Medium },
                    new SwotItem { Id = Guid.NewGuid(), Category = Domain.Enums.SwotCategory.Opportunity, Title = "Traceability & Eco-Certification", Description = "Growing demand for specialty coffee and traceable agricultural exports.", Impact = Domain.Enums.ImpactLevel.High },
                    new SwotItem { Id = Guid.NewGuid(), Category = Domain.Enums.SwotCategory.Threat, Title = "Macroeconomic & Price Volatility", Description = "Fluctuations in global commodity markets and foreign exchange availability.", Impact = Domain.Enums.ImpactLevel.High }
                },
                PestelItems = new List<PestelItem>
                {
                    new PestelItem { Id = Guid.NewGuid(), Category = Domain.Enums.PestelCategory.Political, Title = "National Agricultural Growth Policy", Description = "Strong government backing for agricultural commercialization.", Impact = Domain.Enums.ImpactLevel.High },
                    new PestelItem { Id = Guid.NewGuid(), Category = Domain.Enums.PestelCategory.Economic, Title = "FX & Export Revenue Mandates", Description = "Exchange regulations governing agricultural commodity export proceeds.", Impact = Domain.Enums.ImpactLevel.High },
                    new PestelItem { Id = Guid.NewGuid(), Category = Domain.Enums.PestelCategory.Social, Title = "Smallholder Inclusion & Youth Employment", Description = "Increasing participation of youth cooperatives in supply chains.", Impact = Domain.Enums.ImpactLevel.Medium },
                    new PestelItem { Id = Guid.NewGuid(), Category = Domain.Enums.PestelCategory.Technological, Title = "Mobile Market Information Dissemination", Description = "SMS and mobile app price ticker rollout to farmers nationwide.", Impact = Domain.Enums.ImpactLevel.High },
                    new PestelItem { Id = Guid.NewGuid(), Category = Domain.Enums.PestelCategory.Environmental, Title = "Climate Variations & Harvest Cycles", Description = "Unpredictable rainfall affecting crop yields and warehouse inflows.", Impact = Domain.Enums.ImpactLevel.High },
                    new PestelItem { Id = Guid.NewGuid(), Category = Domain.Enums.PestelCategory.Legal, Title = "Commodity Exchange Proclamation Compliance", Description = "Regulatory framework enforced by the Ethiopian Commodity Exchange Authority.", Impact = Domain.Enums.ImpactLevel.High }
                }
            };

            await context.OrganizationProfiles.AddAsync(profile);
            await context.SaveChangesAsync();
        }

        public static async Task SeedDivisionAccountsAsync(SpmsDbContext context, IPasswordHasher passwordHasher)
        {
            var dataInputRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == Roles.DataInputUser);
            if (dataInputRole == null)
            {
                return;
            }

            var divisions = await context.Divisions.ToListAsync();
            if (divisions.Count == 0)
            {
                return;
            }

            var existingUsernames = (await context.Users.Select(u => u.Username).ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var divisionIdsWithAccounts = (await context.Users
                .Where(u => u.DivisionId != null)
                .Select(u => u.DivisionId!.Value)
                .ToListAsync())
                .ToHashSet();

            var accountIds = new List<Guid>();

            foreach (var division in divisions)
            {
                if (divisionIdsWithAccounts.Contains(division.Id) || existingUsernames.Contains(division.Code))
                {
                    continue;
                }

                var userId = Guid.NewGuid();
                context.Users.Add(new User
                {
                    Id = userId,
                    Username = division.Code,
                    FullName = $"{division.Name} Division Account",
                    PasswordHash = passwordHasher.HashPassword(division.Code),
                    IsActive = true,
                    DivisionId = division.Id,
                    UserRoles = new List<UserRole>
                    {
                        new UserRole { UserId = userId, RoleId = dataInputRole.Id }
                    }
                });

                accountIds.Add(userId);
                existingUsernames.Add(division.Code);
            }

            if (accountIds.Count > 0)
            {
                await context.SaveChangesAsync();
            }
        }

        private static readonly IReadOnlyDictionary<string, string[]> RolePermissionMap = new Dictionary<string, string[]>
        {
            [Roles.Admin] = Permissions.All.Select(p => p.Name).ToArray(),

            [Roles.PowerUser] = Permissions.All
                .Select(p => p.Name)
                .Except(new[] { Permissions.AdminUsersManage, Permissions.AdminRolesManage, Permissions.AdminConfigManage })
                .ToArray(),

            [Roles.Executive] = new[]
            {
                Permissions.DashboardView, Permissions.DashboardConfigure, Permissions.ReportsView, Permissions.ReportsExport,
                Permissions.ScorecardView, Permissions.GoalsView, Permissions.KraView, Permissions.KpiView,
                Permissions.ActualsView, Permissions.TargetsView, Permissions.InitiativesView,
                Permissions.DivisionView, Permissions.DivisionReportsView,
                Permissions.OrganizationView
            },

            [Roles.DataInputUser] = new[]
            {
                Permissions.ScorecardView, Permissions.DashboardView, Permissions.GoalsView, Permissions.KraView, Permissions.KpiView,
                Permissions.ActualsView, Permissions.ActualsEnter, Permissions.DataUpload, Permissions.DataValidate,
                Permissions.TargetsView, Permissions.ReportsView, Permissions.InitiativesView,
                Permissions.DivisionView, Permissions.DivisionActualsEnter, Permissions.DivisionReportsView,
                Permissions.OrganizationView
            },

            [Roles.ViewOnly] = new[]
            {
                Permissions.DashboardView, Permissions.ScorecardView, Permissions.GoalsView, Permissions.KraView,
                Permissions.KpiView, Permissions.ActualsView, Permissions.TargetsView, Permissions.InitiativesView,
                Permissions.DivisionView, Permissions.ReportsView,
                Permissions.OrganizationView
            }
        };

        /// <summary>
        /// FR-607 default RAG thresholds (Green >= 95%, Yellow 70-94%, Red &lt; 70%). Seeded once so the
        /// administrator screen starts from the FRD bands instead of an empty configuration; the rows
        /// can then be changed through PUT /api/RagThreshold.
        /// </summary>
        private static async Task SeedRagThresholdsAsync(SpmsDbContext context)
        {
            var scopeKey = RagThresholdDefaults.ScopeKey;
            var alreadySeeded = await context.RagThresholdConfigs.AnyAsync(x => x.ScopeKey == scopeKey);
            if (alreadySeeded)
            {
                return;
            }

            context.RagThresholdConfigs.AddRange(
                new RagThresholdConfig
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = scopeKey,
                    Band = RagBand.Green,
                    MinScore = RagThresholdDefaults.GreenThreshold,
                    MaxScore = null,
                    Color = RagThresholdDefaults.GreenColor
                },
                new RagThresholdConfig
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = scopeKey,
                    Band = RagBand.Yellow,
                    MinScore = RagThresholdDefaults.YellowThreshold,
                    MaxScore = RagThresholdDefaults.GreenThreshold,
                    Color = RagThresholdDefaults.YellowColor
                },
                new RagThresholdConfig
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = scopeKey,
                    Band = RagBand.Red,
                    MinScore = null,
                    MaxScore = RagThresholdDefaults.YellowThreshold,
                    Color = RagThresholdDefaults.RedColor
                },
                new RagThresholdConfig
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = scopeKey,
                    Band = RagBand.Gray,
                    MinScore = null,
                    MaxScore = null,
                    Color = RagThresholdDefaults.GrayColor
                });

            await context.SaveChangesAsync();
        }
    }
}