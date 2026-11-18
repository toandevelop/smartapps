using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[FormRuleExceptionFilter]
[Route("api/public/forms")]
public class PublicFormsController : ControllerBase
{
    private readonly MetadataService _metadata;
    private readonly RecordService _records;
    private readonly NotificationService _notifications;

    public PublicFormsController(
        MetadataService metadata,
        RecordService records,
        NotificationService notifications)
    {
        _metadata = metadata;
        _records = records;
        _notifications = notifications;
    }

    [HttpGet("{tableId}")]
    public async Task<IActionResult> GetFormDefinition(string tableId)
    {
        var tables = await _metadata.GetTablesAsync(null, null);
        var table = tables.FirstOrDefault(t => t.Id == tableId || t.TableName.Equals(tableId, StringComparison.OrdinalIgnoreCase));
        if (table == null || !table.PublicFormEnabled)
        {
            return NotFound(new { success = false, message = "Biểu mẫu công khai không tồn tại hoặc chưa được kích hoạt." });
        }

        var app = await _metadata.GetAppByCodeOrIdAsync(table.AppId);
        var cols = (await _metadata.GetColumnsAsync(table.Id))
            .Where(c => c.ShowInForm && !c.HiddenForRoles.Contains("STAFF", StringComparer.OrdinalIgnoreCase))
            .ToList();

        var categories = new Dictionary<string, List<SysCategoryItemDto>>();
        foreach (var code in cols.Where(c => !string.IsNullOrWhiteSpace(c.RefCategoryCode)).Select(c => c.RefCategoryCode!).Distinct())
        {
            categories[code] = await _metadata.GetCategoryItemsAsync(code);
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                app = app == null ? null : new { app.Id, app.Code, app.Name, app.Color, app.Department },
                table = new { table.Id, table.DisplayName, table.Description, table.AppId, table.ModuleId, table.FormRules },
                columns = cols,
                categories
            }
        });
    }

    [HttpPost("{tableId}/submit")]
    public async Task<IActionResult> SubmitPublicForm(string tableId, [FromBody] Dictionary<string, object?> data)
    {
        var tables = await _metadata.GetTablesAsync(null, null);
        var table = tables.FirstOrDefault(t => t.Id == tableId || t.TableName.Equals(tableId, StringComparison.OrdinalIgnoreCase));
        if (table == null || !table.PublicFormEnabled)
        {
            return NotFound(new { success = false, message = "Biểu mẫu công khai chưa được kích hoạt." });
        }

        try
        {
            var allowed = (await _metadata.GetColumnsAsync(table.Id))
                .Where(c => c.ShowInForm && !c.HiddenForRoles.Contains("STAFF", StringComparer.OrdinalIgnoreCase)
                    && !c.ReadOnlyForRoles.Contains("STAFF", StringComparer.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(c.Formula) && c.DataType != "AUTO_NUMBER")
                .Select(c => c.ColumnCode).ToHashSet(StringComparer.Ordinal);
            if (data.Keys.Any(key => !allowed.Contains(key)))
                return BadRequest(new { success = false, message = "Dữ liệu chứa trường không được phép nhập qua biểu mẫu công khai." });
            var (saved, _) = await _records.UpsertRecordAsync(new UpsertRecordRequest
            {
                AppId = table.AppId,
                ModuleId = table.ModuleId,
                TableId = table.Id,
                Data = data ?? new Dictionary<string, object?>()
            }, "public_form", false);

            var app = await _metadata.GetAppByCodeOrIdAsync(table.AppId);
            await _notifications.CreateAsync(
                $"Biểu mẫu mới ({app?.Code ?? "FORM"})",
                $"Khách/Ứng viên vừa gửi phiếu '{saved.RecordKey ?? saved.Id}' qua Public Form '{table.DisplayName}'.",
                app?.Code,
                saved.Id,
                "*");

            return Ok(new { success = true, data = new { id = saved.Id, recordKey = saved.RecordKey } });
        }
        catch (DuplicateIdentityKeyException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
    }
}
