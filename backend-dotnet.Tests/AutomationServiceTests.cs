using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class AutomationServiceTests
{
    [Fact]
    public void ResolveTemplate_HandlesEmailPrefixSlugDateAddAndRecordFields()
    {
        var rec = new AppRecordDto
        {
            Id = "rec_test",
            AppId = "app_hrm",
            ModuleId = "mod_hrm",
            TableId = "tbl_hrm_nhanvien",
            RecordKey = "NV901",
            CreatedBy = "hr_admin",
            Data = new Dictionary<string, object?>
            {
                ["ma_nv"] = "NV901",
                ["ho_ten"] = "Đặng Quốc Bảo",
                ["email"] = "bao.dang@company.vn",
                ["phong_ban"] = "Kỹ thuật"
            }
        };

        Assert.Equal("bao.dang", AutomationService.ResolveTemplate("{{EMAIL_PREFIX(email)}}", rec));
        Assert.Equal("dangquocbao", AutomationService.ResolveTemplate("{{SLUG(ho_ten)}}", rec));
        Assert.Equal("Onboarding Đặng Quốc Bảo (NV901) - Kỹ thuật",
            AutomationService.ResolveTemplate("Onboarding {{ho_ten}} ({{ma_nv}}) - {{phong_ban}}", rec));

        var expectedPlus3 = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd");
        Assert.Equal(expectedPlus3, AutomationService.ResolveTemplate("{{DATE_ADD(3)}}", rec));
    }

    [Fact]
    public void DeriveUsername_UsesFallbackChainAndNormalizesVietnamese()
    {
        Assert.Equal("bao.dang",
            AutomationService.DeriveUsername(null, "bao.dang@company.vn", "NV901", "Đặng Quốc Bảo"));

        Assert.Equal("dang.bao",
            AutomationService.DeriveUsername("Đặng.Bảo ", "bao.dang@company.vn", "NV901", "Đặng Quốc Bảo"));

        Assert.Equal("nv901",
            AutomationService.DeriveUsername(null, null, "NV901", "Đặng Quốc Bảo"));

        Assert.Equal("tranthithuy",
            AutomationService.DeriveUsername(null, null, null, "Trần Thị Thủy"));
    }
}
