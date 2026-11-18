using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class AuthServiceTests
{
    private static AuthService CreateService()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=smartapps_db;Username=postgres;Password=postgres",
                ["Auth:Secret"] = "Unit_Test_Secret_Key_2026"
            })
            .Build();
        return new AuthService(cfg);
    }

    [Fact]
    public void HashPassword_UsesUniquePerUserSaltedPbkdf2()
    {
        var h1 = AuthService.HashPassword("123456");
        var h2 = AuthService.HashPassword("123456");

        Assert.StartsWith("pbkdf2$100000$", h1);
        Assert.NotEqual(h1, h2);

        Assert.True(AuthService.VerifyPassword("123456", h1, out var upgrade1));
        Assert.False(upgrade1);
        Assert.False(AuthService.VerifyPassword("wrong", h1, out _));
    }

    [Fact]
    public void VerifyPassword_FlagsLegacySha256ForTransparentUpgrade()
    {
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("smartapps::123456"))).ToLowerInvariant();
        Assert.True(AuthService.VerifyPassword("123456", legacy, out var needsUpgrade));
        Assert.True(needsUpgrade);
    }

    [Fact]
    public void CreateToken_AndRoleChecksWorkCorrectly()
    {
        var svc = CreateService();
        var user = new SysUserDto
        {
            Id = "usr_1",
            Username = "hr_manager",
            RoleCode = "HR_MANAGER",
            AllowedApps = ["HRM", "OPS"],
            Permissions = ["record.view", "record.edit"]
        };

        var token = svc.CreateToken(user);
        Assert.Contains(".", token);
        Assert.True(AuthService.HasPermission(user, "record.edit"));
        Assert.False(AuthService.HasPermission(user, "studio.manage"));
        Assert.True(AuthService.CanAccessApp(user, "hrm"));
        Assert.False(AuthService.CanAccessApp(user, "asset"));
    }

    [Fact]
    public void PerAppRolePermissions_EnforcesSeparatePermissionsPerApp()
    {
        var user = new SysUserDto
        {
            Id = "usr_custom",
            Username = "ke_toan",
            RoleCode = "KE_TOAN",
            RoleCodes = ["KE_TOAN", "STAFF"],
            AllowedApps = ["FINANCE", "OPS"],
            SystemPermissions = [],
            AppPermissions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["FINANCE"] = ["record.view", "record.view_all", "record.create", "record.edit", "record.approve"],
                ["OPS"] = ["record.view", "record.create"]
            },
            AppRowScopes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FINANCE"] = "ALL",
                ["OPS"] = "OWNER_ONLY"
            }
        };

        Assert.True(AuthService.CanAccessApp(user, "finance"));
        Assert.True(AuthService.CanAccessApp(user, "ops"));
        Assert.False(AuthService.CanAccessApp(user, "hrm"));

        Assert.True(AuthService.HasAppPermission(user, "FINANCE", "record.edit"));
        Assert.True(AuthService.HasAppPermission(user, "FINANCE", "record.approve"));
        Assert.False(AuthService.HasAppPermission(user, "OPS", "record.edit"));
        Assert.False(AuthService.HasAppPermission(user, "OPS", "record.approve"));
        Assert.True(AuthService.HasAppPermission(user, "OPS", "record.create"));

        Assert.True(AuthService.HasAnyRole(user, ["ADMIN", "KE_TOAN"]));
        Assert.False(AuthService.HasAnyRole(user, ["HR_MANAGER"]));

        // Row-scope checks: FINANCE overrides to ALL; OPS overrides to OWNER_ONLY; HRM table OWNER_ONLY without view_all
        Assert.False(AuthService.IsOwnerScopedForApp(user, "FINANCE", "OWNER_ONLY"));
        Assert.True(AuthService.IsOwnerScopedForApp(user, "OPS", "ALL"));
        Assert.True(AuthService.IsOwnerScopedForApp(user, "HRM", "OWNER_ONLY"));
    }

    [Fact]
    public void PermissionCatalog_ContainsSystemAndAppPermissions()
    {
        var catalog = AuthService.GetPermissionCatalog();
        Assert.Contains(catalog.SystemPermissions, p => p.Code == "role.manage");
        Assert.Contains(catalog.SystemPermissions, p => p.Code == "user.manage");
        Assert.Contains(catalog.AppPermissions, p => p.Code == "record.view_all");
        Assert.Contains(catalog.AppPermissions, p => p.Code == "record.approve");
        Assert.Contains(catalog.AppPermissions, p => p.Code == "app.admin");
    }

    [Fact]
    public void TableLevelPermissions_DifferentiateAdminAndEndUserInSameApp()
    {
        var staff = new SysUserDto
        {
            Id = "usr_staff",
            Username = "staff",
            EmployeeCode = "NV003",
            RoleCode = "STAFF",
            RoleCodes = ["STAFF"],
            AllowedApps = ["HRM"],
            AppPermissions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["HRM"] = ["record.view", "record.create"]
            },
            TablePermissions = new Dictionary<string, EffectiveTablePermissionDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["tbl_hrm_nhanvien"] = new() { CanView = true, CanCreate = false, CanEdit = false, CanDelete = false, RowScope = "OWNER_ONLY" },
                ["tbl_hrm_nghiphep"] = new() { CanView = true, CanCreate = true, CanEdit = false, CanDelete = false, RowScope = "OWNER_ONLY" },
                ["tbl_hrm_congtac"] = new() { CanView = true, CanCreate = false, CanEdit = false, CanDelete = false, RowScope = "OWNER_ONLY" }
            }
        };

        var hrMgr = new SysUserDto
        {
            Id = "usr_hr",
            Username = "hr_manager",
            EmployeeCode = "NV002",
            RoleCode = "HR_MANAGER",
            RoleCodes = ["HR_MANAGER"],
            AllowedApps = ["HRM", "OPS"],
            AppPermissions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["HRM"] = ["app.admin", "record.view", "record.view_all", "record.create", "record.edit", "record.delete", "record.approve"],
                ["OPS"] = ["record.view", "record.view_all", "record.create"]
            }
        };

        // Staff on HRM: view own profile & business trips, create own leave, cannot add/edit employees
        Assert.True(AuthService.CanAccessTable(staff, "HRM", "tbl_hrm_nhanvien", "view"));
        Assert.False(AuthService.CanAccessTable(staff, "HRM", "tbl_hrm_nhanvien", "create"));
        Assert.True(AuthService.CanAccessTable(staff, "HRM", "tbl_hrm_nghiphep", "create"));
        Assert.True(AuthService.CanAccessTable(staff, "HRM", "tbl_hrm_congtac", "view"));
        Assert.False(AuthService.CanAccessTable(staff, "HRM", "tbl_hrm_congtac", "create"));

        Assert.True(AuthService.IsOwnerScopedForTable(staff, "HRM", "tbl_hrm_nhanvien", "OWNER_ONLY", false));

        // HR Manager on HRM: App Admin sees all unless toggled to personalMode=true
        Assert.True(AuthService.CanAccessTable(hrMgr, "HRM", "tbl_hrm_nhanvien", "create"));
        Assert.False(AuthService.IsOwnerScopedForTable(hrMgr, "HRM", "tbl_hrm_nhanvien", "OWNER_ONLY", false));
        Assert.True(AuthService.IsOwnerScopedForTable(hrMgr, "HRM", "tbl_hrm_nhanvien", "OWNER_ONLY", true));
    }

    [Fact]
    public void TasksAppPermissions_ScopeManagerAndStaffRoles()
    {
        var staff = new SysUserDto
        {
            Username = "staff",
            RoleCode = "STAFF",
            RoleCodes = ["STAFF"],
            AllowedApps = ["TASKS"],
            AppPermissions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["TASKS"] = ["record.view", "record.create", "record.edit"]
            },
            TablePermissions = new Dictionary<string, EffectiveTablePermissionDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["tbl_tasks_congviec"] = new() { CanView = true, CanCreate = true, CanEdit = true, CanDelete = false, RowScope = "OWNER_ONLY" }
            }
        };

        var manager = new SysUserDto
        {
            Username = "hr_manager",
            RoleCode = "HR_MANAGER",
            RoleCodes = ["HR_MANAGER"],
            AllowedApps = ["TASKS"],
            AppPermissions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["TASKS"] = ["record.view", "record.view_all", "record.create", "record.edit", "record.delete"]
            },
            TablePermissions = new Dictionary<string, EffectiveTablePermissionDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["tbl_tasks_congviec"] = new() { CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, RowScope = "ALL" }
            }
        };

        Assert.True(AuthService.CanAccessApp(staff, "TASKS"));
        Assert.True(AuthService.CanAccessTable(staff, "TASKS", "tbl_tasks_congviec", "edit"));
        Assert.False(AuthService.CanAccessTable(staff, "TASKS", "tbl_tasks_congviec", "delete"));
        Assert.True(AuthService.IsOwnerScopedForTable(staff, "TASKS", "tbl_tasks_congviec", "OWNER_ONLY", false));

        Assert.True(AuthService.CanAccessTable(manager, "TASKS", "tbl_tasks_congviec", "delete"));
        Assert.False(AuthService.IsOwnerScopedForTable(manager, "TASKS", "tbl_tasks_congviec", "OWNER_ONLY", false));
        Assert.True(AuthService.IsOwnerScopedForTable(manager, "TASKS", "tbl_tasks_congviec", "OWNER_ONLY", true));
    }

    [Fact]
    public void ResolveRowScope_SupportsAllDepartmentAndOwnerOnlyScopes()
    {
        var deptLead = new SysUserDto
        {
            Username = "tech_lead",
            RoleCode = "STAFF",
            Department = "TECH",
            AppPermissions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["HRM"] = ["record.view"]
            },
            AppRowScopes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HRM"] = "DEPARTMENT"
            },
            TablePermissions = new Dictionary<string, EffectiveTablePermissionDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["tbl_hrm_nghiphep"] = new() { CanView = true, RowScope = "OWNER_ONLY" },
                ["tbl_hrm_phucap"] = new() { CanView = true, RowScope = "INHERIT" }
            }
        };

        Assert.Equal("DEPARTMENT", AuthService.ResolveRowScope(deptLead, "HRM", "tbl_hrm_nhanvien", "OWNER_ONLY", false));
        Assert.Equal("OWNER_ONLY", AuthService.ResolveRowScope(deptLead, "HRM", "tbl_hrm_nghiphep", "ALL", false));
        Assert.Equal("DEPARTMENT", AuthService.ResolveRowScope(deptLead, "HRM", "tbl_hrm_phucap", "OWNER_ONLY", false));
        Assert.Equal("OWNER_ONLY", AuthService.ResolveRowScope(deptLead, "HRM", "tbl_hrm_nhanvien", "ALL", true));
    }

    [Fact]
    public void OrgUnitSupport_PreservesOrgUnitIdOnUserModelsAndSubtreeFilterClause()
    {
        var req = new UpsertUserRequest
        {
            Username = "sales_rep",
            FullName = "Sales Rep",
            Department = "Phòng Kinh doanh 1",
            OrgUnitId = "org_kd_1"
        };
        var user = new SysUserDto
        {
            Username = req.Username,
            FullName = req.FullName,
            Department = req.Department,
            OrgUnitId = req.OrgUnitId
        };

        Assert.Equal("org_kd_1", user.OrgUnitId);
        Assert.Contains("sys_org_units", RecordService.OwnerFilterSqlClause);
        Assert.Contains("org_tree", RecordService.OwnerFilterSqlClause);
        Assert.Contains("su.org_unit_id = t.id", RecordService.OwnerFilterSqlClause);
    }
}
