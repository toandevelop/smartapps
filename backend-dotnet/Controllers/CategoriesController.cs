using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Models;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly MetadataService _metadata;

    public CategoriesController(MetadataService metadata)
    {
        _metadata = metadata;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories()
    {
        var items = await _metadata.GetCategoriesAsync();
        return Ok(new { success = true, data = items });
    }

    [HttpPost]
    public async Task<IActionResult> UpsertCategory([FromBody] UpsertCategoryRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CategoryCode) || string.IsNullOrWhiteSpace(req.CategoryName))
            return BadRequest(new { success = false, message = "CategoryCode và CategoryName là bắt buộc." });

        var item = await _metadata.UpsertCategoryAsync(req);
        return Ok(new { success = true, data = item });
    }

    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] string? categoryCode)
    {
        var items = await _metadata.GetCategoryItemsAsync(categoryCode);
        return Ok(new { success = true, data = items });
    }

    [HttpPost("items")]
    public async Task<IActionResult> UpsertItem([FromBody] UpsertCategoryItemRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CategoryCode) || string.IsNullOrWhiteSpace(req.ItemName))
            return BadRequest(new { success = false, message = "Thiếu thông tin danh mục hoặc tên mục chọn." });

        var item = await _metadata.UpsertCategoryItemAsync(req);
        return Ok(new { success = true, data = item });
    }

    public class BulkCategoryItemsRequest
    {
        public string CategoryCode { get; set; } = string.Empty;
        public List<UpsertCategoryItemRequest> Items { get; set; } = new();
    }

    [HttpPost("items/bulk")]
    public async Task<IActionResult> BulkUpsertItems([FromBody] BulkCategoryItemsRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CategoryCode) || req.Items.Count == 0)
            return BadRequest(new { success = false, message = "Thiếu mã danh mục hoặc danh sách mục cần nhập." });

        var cat = req.CategoryCode.Trim().ToUpperInvariant();
        var imported = new List<SysCategoryItemDto>();
        foreach (var row in req.Items)
        {
            if (string.IsNullOrWhiteSpace(row.ItemName)) continue;
            row.CategoryCode = cat;
            imported.Add(await _metadata.UpsertCategoryItemAsync(row));
        }

        return Ok(new { success = true, data = imported, count = imported.Count });
    }

    [HttpDelete("{categoryCode}")]
    public async Task<IActionResult> DeleteCategory(string categoryCode)
    {
        var ok = await _metadata.DeleteCategoryAsync(categoryCode);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false, message = "Không tìm thấy danh mục." });
    }

    [HttpDelete("items/{id}")]
    public async Task<IActionResult> DeleteItem(string id)
    {
        var ok = await _metadata.DeleteCategoryItemAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false, message = "Không tìm thấy mục danh mục." });
    }
}
