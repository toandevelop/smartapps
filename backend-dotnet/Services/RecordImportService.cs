using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public sealed class ImportRejectedException(string message, int statusCode = 400, ImportPreviewDto? preview = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public ImportPreviewDto? Preview { get; } = preview;
}

public partial class RecordService
{
    public const int MaxImportRows = 2000;
    private sealed record ImportWrite(ImportRowPreviewDto Row, Dictionary<string, object?> Data, AppRecordDto? Existing);
    private sealed record ImportPlan(ImportPreviewDto Preview, List<ImportWrite> Writes, SysTableDto Table, Dictionary<(string ColumnCode, string ScopeKey), long> MaxAutoNumbers);

    public async Task<ImportPreviewDto> PreviewImportAsync(BulkImportRequest request, SysUserDto user)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        return (await BuildImportPlanAsync(conn, request, user, lockRows: false)).Preview;
    }

    public async Task<(int Inserted, int Updated, int ErrorRows)> BulkImportAsync(BulkImportRequest request, SysUserDto user)
    {
        if (string.IsNullOrWhiteSpace(request.PreviewFingerprint))
            throw new ImportRejectedException("Vui lòng xem trước và kiểm tra dữ liệu trước khi nhập.");
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable);
        // Serialize imports of the same logical table. Existing targets are locked as well.
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@tableId, 0))", conn, tx))
        {
            gate.Parameters.AddWithValue("tableId", request.TableId);
            await gate.ExecuteNonQueryAsync();
        }
        var plan = await BuildImportPlanAsync(conn, request, user, lockRows: true);
        if (!plan.Preview.CanImport)
            throw new ImportRejectedException("Dữ liệu có lỗi. Chưa nhập dòng nào; hãy sửa và kiểm tra lại.", 400, plan.Preview);
        if (!string.Equals(request.PreviewFingerprint, plan.Preview.Fingerprint, StringComparison.Ordinal))
            throw new ImportRejectedException("Dữ liệu, quyền hoặc cấu hình đã thay đổi sau khi xem trước. Vui lòng kiểm tra lại.", 409);

        foreach (var write in plan.Writes)
        {
            var id = write.Existing?.Id ?? $"rec_{Guid.NewGuid():N}";
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO app_records(id, app_id, module_id, table_id, record_key, data, status, validation_errors, created_by, updated_by)
                VALUES (@id,@app,@mod,@tbl,@key,@data::jsonb,'ACTIVE','[]'::jsonb,@actor,@actor)
                ON CONFLICT(id) DO UPDATE SET data=EXCLUDED.data, record_key=EXCLUDED.record_key,
                    status='ACTIVE', version=COALESCE(app_records.version, 1) + 1,
                    validation_errors='[]'::jsonb, updated_by=@actor, updated_at=CURRENT_TIMESTAMP
                """, conn, tx);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("app", plan.Table.AppId);
            cmd.Parameters.AddWithValue("mod", plan.Table.ModuleId);
            cmd.Parameters.AddWithValue("tbl", plan.Table.Id);
            cmd.Parameters.AddWithValue("key", (object?)write.Row.RecordKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("data", JsonSerializer.Serialize(write.Data));
            cmd.Parameters.AddWithValue("actor", user.Username);
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new ImportRejectedException("Mã định danh đã tồn tại do thay đổi đồng thời. Vui lòng kiểm tra lại.", 409);
            }
            await WriteAuditAsync(conn, plan.Table.AppId, plan.Table.Id, id,
                write.Existing == null ? "CREATE" : "UPDATE", user.Username, write.Existing?.Data, write.Data,
                $"Nhập Excel dòng {write.Row.SourceRow}");
        }
        foreach (var ((colCode, scopeKey), maxSeq) in plan.MaxAutoNumbers)
            await AdvanceAutoNumberCounterAsync(conn, tx, plan.Table.Id, colCode, scopeKey, maxSeq);
        await tx.CommitAsync();
        _realtime.Publish("record_changed", null, plan.Table.Id, null, "*", new { action = "IMPORT" });
        return (plan.Preview.Inserted, plan.Preview.Updated, 0);
    }

    private async Task<ImportPlan> BuildImportPlanAsync(NpgsqlConnection conn, BulkImportRequest request, SysUserDto user, bool lockRows)
    {
        if (request.Rows == null || request.Rows.Count is < 1 or > MaxImportRows)
            throw new ImportRejectedException($"Mỗi lần nhập cần từ 1 đến {MaxImportRows} dòng dữ liệu.");
        if (request.Rows.Any(r => r == null)) throw new ImportRejectedException("Dòng dữ liệu không hợp lệ.");
        if (request.RowNumbers == null || (request.RowNumbers.Count != 0 && (request.RowNumbers.Count != request.Rows.Count
            || request.RowNumbers.Any(n => n < 1) || request.RowNumbers.Distinct().Count() != request.RowNumbers.Count)))
            throw new ImportRejectedException("Số dòng nguồn không hợp lệ.");
        if (request.NumberFormat is not ("invariant" or "vi-VN")
            || request.DateFormat is not ("yyyy-MM-dd" or "dd/MM/yyyy" or "MM/dd/yyyy"))
            throw new ImportRejectedException("Định dạng số hoặc ngày không được hỗ trợ.");

        var tables = await _metadata.GetTablesAsync(null, null);
        var table = tables.FirstOrDefault(t => t.Id == request.TableId);
        if (table == null || !table.IsActive) throw new ImportRejectedException("Không tìm thấy bảng dữ liệu.", 404);
        if (table.AppId != request.AppId || table.ModuleId != request.ModuleId)
            throw new ImportRejectedException("App, phân hệ và bảng dữ liệu không khớp.");
        var app = await _metadata.GetAppByCodeOrIdAsync(table.AppId);
        if (app == null || !AuthService.CanAccessApp(user, app.Code)
            || !AuthService.CanAccessTable(user, app.Code, table.Id, "create")
            || !AuthService.CanAccessTable(user, app.Code, table.Id, "view"))
            throw new ImportRejectedException("Không có quyền nhập dữ liệu vào bảng này.", 403);
        var admin = AuthService.IsAdminUser(user);
        var roles = user.RoleCodes.Append(user.RoleCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rowScope = AuthService.ResolveRowScope(user, app.Code, table.Id, table.RowScope);
        var columns = await _metadata.GetColumnsAsync(table.Id);
        FormRuleService.ValidateDefinition(table.FormRules, columns);
        var columnMap = columns.ToDictionary(c => c.ColumnCode, StringComparer.Ordinal);
        var categories = await LoadCategorySetsAsync(columns);
        var identity = columns.FirstOrDefault(c => c.IsIdentityKey);
        string? SuppliedKey(Dictionary<string, object?> row)
        {
            if (identity == null) return null;
            try { return Convert.ToString(ImportValueNormalizer.Normalize(row.GetValueOrDefault(identity.ColumnCode), identity.DataType,
                request.NumberFormat, request.DateFormat), CultureInfo.InvariantCulture)?.Trim().ToLowerInvariant(); }
            catch (Exception ex) when (ex is FormatException or OverflowException or JsonException or InvalidOperationException) { return null; }
        }
        var suppliedKeys = identity == null ? Array.Empty<string>() : request.Rows
            .Select(SuppliedKey)
            .Where(k => !string.IsNullOrEmpty(k)).Cast<string>().Distinct().ToArray();
        var existing = new List<AppRecordDto>();
        if (suppliedKeys.Length > 0)
        {
            await using var existingCmd = new NpgsqlCommand($"""
                SELECT {SelectColumns} FROM app_records
                WHERE table_id=@tbl AND deleted_at IS NULL AND LOWER(record_key)=ANY(@keys)
                ORDER BY id { (lockRows ? "FOR UPDATE" : "") }
                """, conn);
            existingCmd.Parameters.AddWithValue("tbl", table.Id);
            existingCmd.Parameters.AddWithValue("keys", suppliedKeys);
            await using var reader = await existingCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) existing.Add(ReadRecord(reader));
        }
        var reservedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (columns.Any(c => c.DataType.Equals("AUTO_NUMBER", StringComparison.OrdinalIgnoreCase)))
        {
            await using var keyCmd = new NpgsqlCommand("SELECT record_key FROM app_records WHERE table_id=@tbl AND record_key IS NOT NULL", conn);
            keyCmd.Parameters.AddWithValue("tbl", table.Id);
            await using var reader = await keyCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) reservedKeys.Add(reader.GetString(0));
        }

        var preview = new ImportPreviewDto();
        var writes = new List<ImportWrite>();
        var lookupCache = new Dictionary<(string, string), bool>();
        var autoCounters = new Dictionary<(string ColumnCode, string ScopeKey), long>();
        var maxAutoNumbers = new Dictionary<(string ColumnCode, string ScopeKey), long>();
        for (var index = 0; index < request.Rows.Count; index++)
        {
            var input = request.Rows[index];
            if (input == null) throw new ImportRejectedException("Dòng dữ liệu không hợp lệ.");
            var row = new ImportRowPreviewDto { SourceRow = request.RowNumbers.Count == 0 ? index + 2 : request.RowNumbers[index] };
            var data = new Dictionary<string, object?>();
            foreach (var (field, value) in input)
            {
                if (!columnMap.TryGetValue(field, out var column)) { AddImportError(row, field, "Cột không có trong cấu trúc bảng."); continue; }
                if ((!admin && (column.HiddenForRoles.Any(roles.Contains) || column.ReadOnlyForRoles.Any(roles.Contains)))
                    || !string.IsNullOrWhiteSpace(column.Formula))
                { AddImportError(row, field, "Không được nhập cột bị ẩn, chỉ đọc hoặc tính tự động."); continue; }
                try { data[field] = ImportValueNormalizer.Normalize(value, column.DataType, request.NumberFormat, request.DateFormat); }
                catch (Exception ex) when (ex is FormatException or OverflowException or JsonException or InvalidOperationException)
                { AddImportError(row, field, ex.Message); }
            }
            if (input.Count == 0) AddImportError(row, "", "Dòng không có cột được ánh xạ.");
            string? key = identity == null ? null : Convert.ToString(data.GetValueOrDefault(identity.ColumnCode), CultureInfo.InvariantCulture)?.Trim();
            var matches = string.IsNullOrWhiteSpace(key) ? new List<AppRecordDto>()
                : existing.Where(r => string.Equals(r.RecordKey, key, StringComparison.OrdinalIgnoreCase)).ToList();
            var old = matches.FirstOrDefault();
            if (matches.Count > 1) AddImportError(row, identity!.ColumnCode, "Mã định danh đang trùng trong hệ thống; cần xử lý trước khi nhập.");
            row.Operation = old == null ? "CREATE" : "UPDATE";
            var canUpdate = old == null || (request.UpsertByIdentityKey
                && AuthService.CanAccessTable(user, app.Code, table.Id, "edit")
                && (rowScope == "ALL" || await IsRecordOwnedByUserAsync(old.Id, user, rowScope)));
            if (!canUpdate) AddImportError(row, identity!.ColumnCode, "Mã đã tồn tại và không được phép cập nhật theo lựa chọn hoặc quyền hiện tại.");
            if (old != null && old.ApprovalStatus == "APPROVED" && !admin)
                AddImportError(row, identity?.ColumnCode ?? "", "Bản ghi đã phê duyệt và bị khóa chỉnh sửa.");
            if (old != null && canUpdate)
                data = old.Data.Concat(data).GroupBy(k => k.Key).ToDictionary(g => g.Key, g => g.Last().Value);
            if (old == null)
            {
                foreach (var column in columns)
                {
                    if (!string.IsNullOrWhiteSpace(column.Formula)) continue;
                    if (column.DataType.Equals("AUTO_NUMBER", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(data.GetValueOrDefault(column.ColumnCode)?.ToString())) continue;
                        var prefix = ResolveAutoNumberPrefix(column.DefaultValue);
                        var counterKey = (column.ColumnCode, prefix);
                        if (!autoCounters.TryGetValue(counterKey, out var next))
                        {
                            await using var cCmd = new NpgsqlCommand(
                                "SELECT current_value FROM sys_auto_number_counters WHERE table_id=@tbl AND column_code=@col AND scope_key=@scope", conn);
                            cCmd.Parameters.AddWithValue("tbl", table.Id);
                            cCmd.Parameters.AddWithValue("col", column.ColumnCode);
                            cCmd.Parameters.AddWithValue("scope", prefix);
                            var persisted = await cCmd.ExecuteScalarAsync();
                            var persistedVal = persisted != null && persisted != DBNull.Value ? Convert.ToInt64(persisted) : 0L;
                            var maxExisting = reservedKeys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                .Select(k => long.TryParse(k[prefix.Length..], out var suffix) ? suffix : 0).DefaultIfEmpty(0).Max();
                            next = Math.Max(persistedVal, maxExisting) + 1;
                        }
                        while (reservedKeys.Contains($"{prefix}{next:0000}")) next++;
                        data[column.ColumnCode] = $"{prefix}{next:0000}";
                        reservedKeys.Add($"{prefix}{next:0000}");
                        maxAutoNumbers[counterKey] = next;
                        autoCounters[counterKey] = next + 1;
                    }
                    else if (!data.ContainsKey(column.ColumnCode) && column.DataType != "SUB_TABLE" && !string.IsNullOrWhiteSpace(column.DefaultValue))
                    {
                        try { data[column.ColumnCode] = ImportValueNormalizer.Normalize(column.DefaultValue, column.DataType, "invariant", "yyyy-MM-dd"); }
                        catch (Exception ex) when (ex is FormatException or OverflowException or JsonException)
                        { AddImportError(row, column.ColumnCode, "Giá trị mặc định của cột không hợp lệ."); }
                    }
                }
                if (!admin && rowScope == "OWNER_ONLY" && !string.IsNullOrWhiteSpace(user.EmployeeCode))
                    foreach (var column in columns.Where(c => c.RefTableId == "tbl_hrm_nhanvien" || c.ColumnCode is "ho_ten_nv" or "nhan_vien" or "nguoi_tao" or "nguoi_nhan"))
                    {
                        if (data.TryGetValue(column.ColumnCode, out var assigned) && assigned != null && assigned.ToString() != user.EmployeeCode)
                            AddImportError(row, column.ColumnCode, "Chỉ được tạo dữ liệu cho mã nhân viên của chính mình.");
                        data[column.ColumnCode] = user.EmployeeCode;
                    }
            }
            // Validate the merged row, so unmapped fields of an update are preserved.
            foreach (var column in columns.Where(c => !string.IsNullOrWhiteSpace(c.RefCategoryCode)))
            {
                var raw = data.GetValueOrDefault(column.ColumnCode)?.ToString();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!categories.TryGetValue(column.RefCategoryCode!, out var allowed) || !allowed.Contains(raw))
                    AddImportError(row, column.ColumnCode, "Giá trị phải là mã có trong danh mục.");
                else data[column.ColumnCode] = allowed.First(v => v.Equals(raw, StringComparison.OrdinalIgnoreCase));
            }
            foreach (var column in columns.Where(c => !string.IsNullOrWhiteSpace(c.RefTableId)))
            {
                var raw = data.GetValueOrDefault(column.ColumnCode)?.ToString();
                if (string.IsNullOrWhiteSpace(raw) || (!input.ContainsKey(column.ColumnCode) && old != null)) continue;
                var cacheKey = (column.Id, raw);
                if (!lookupCache.TryGetValue(cacheKey, out var allowed))
                {
                    allowed = await IsImportLookupAllowedAsync(conn, column, raw, user, tables, roles);
                    lookupCache[cacheKey] = allowed;
                }
                if (!allowed) AddImportError(row, column.ColumnCode, "Mã liên kết không tồn tại hoặc ngoài phạm vi được phép.");
            }
            var computed = _compute.EvaluateAndValidate(columns, categories, [("preview_" + index, data)], table.FormRules).Single();
            data = computed.EvaluatedData;
            key = identity == null ? null : Convert.ToString(data.GetValueOrDefault(identity.ColumnCode), CultureInfo.InvariantCulture)?.Trim();
            if (old == null && !string.IsNullOrWhiteSpace(key) && !suppliedKeys.Contains(key.ToLowerInvariant()))
            {
                await using var collision = new NpgsqlCommand("SELECT 1 FROM app_records WHERE table_id=@tbl AND deleted_at IS NULL AND LOWER(record_key)=@key LIMIT 1", conn);
                collision.Parameters.AddWithValue("tbl", table.Id);
                collision.Parameters.AddWithValue("key", key.ToLowerInvariant());
                if (await collision.ExecuteScalarAsync() != null)
                    AddImportError(row, identity!.ColumnCode, "Mã định danh từ giá trị mặc định hoặc tự sinh đã tồn tại.");
            }
            await EvaluateCrossAppFormulasAsync(conn, columns, data, key);
            try { FormRuleService.EnforceWrite(columns, table.FormRules, data, old?.Data, NormalizeDict(input), categories); }
            catch (FormRuleValidationException ex) { row.Errors.AddRange(ex.Errors); }
            row.Errors.AddRange(computed.Errors.Select(issue => !admin && columnMap.TryGetValue(issue.Field, out var field)
                && field.HiddenForRoles.Any(roles.Contains)
                ? new ValidationErrorItem { Field = "", Severity = issue.Severity, Message = "Trường bị ẩn có dữ liệu không hợp lệ; cần quản trị viên kiểm tra." }
                : issue));
            row.RecordKey = key;
            row.Data = data.Where(kv => columnMap.TryGetValue(kv.Key, out var c) && (admin || !c.HiddenForRoles.Any(roles.Contains)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            // Never return existing data from a row the caller cannot edit.
            if (!canUpdate) row.Data = input.Where(kv => columnMap.TryGetValue(kv.Key, out var c) && (admin || !c.HiddenForRoles.Any(roles.Contains)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            preview.Rows.Add(row);
            writes.Add(new ImportWrite(row, data, old));
        }
        foreach (var duplicate in preview.Rows.Where(r => !string.IsNullOrWhiteSpace(r.RecordKey)).GroupBy(r => r.RecordKey!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var row in duplicate) AddImportError(row, identity?.ColumnCode ?? "", "Mã định danh trùng với dòng khác trong file.");

        // This is an optimistic preflight fingerprint, not an authorization token.
        // Commit always rebuilds the plan under a transaction and rechecks rights and validation.
        preview.Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            request.AppId, request.ModuleId, request.TableId, request.UpsertByIdentityKey, request.Rows, request.RowNumbers,
            request.NumberFormat, request.DateFormat,
            User = new
            {
                user.Id,
                user.Username,
                Roles = roles.Order(StringComparer.Ordinal).ToArray(),
                Admin = admin,
                RowScope = rowScope,
                CanEdit = AuthService.CanAccessTable(user, app.Code, table.Id, "edit"),
                user.Department,
                user.OrgUnitId,
                user.EmployeeCode
            },
            Table = table, Columns = columns, Categories = categories,
            Targets = existing.Select(r => new { r.Id, r.UpdatedAt, r.ApprovalStatus, r.Data }), PreviewRows = preview.Rows
        })));
        return new ImportPlan(preview, writes, table, maxAutoNumbers);
    }

    private async Task<bool> IsImportLookupAllowedAsync(NpgsqlConnection conn, SysColumnDto column, string value,
        SysUserDto user, List<SysTableDto> tables, HashSet<string> roles)
    {
        var target = tables.FirstOrDefault(t => t.Id == column.RefTableId && t.IsActive);
        if (target == null) return false;
        var app = await _metadata.GetAppByCodeOrIdAsync(target.AppId);
        if (app == null || !AuthService.CanAccessApp(user, app.Code) || !AuthService.CanAccessTable(user, app.Code, target.Id, "view")) return false;
        var columns = await _metadata.GetColumnsAsync(target.Id);
        var visible = columns.Where(c => AuthService.IsAdminUser(user) || !c.HiddenForRoles.Any(roles.Contains)).ToList();
        var valueField = column.RefValueField ?? visible.FirstOrDefault(c => c.IsIdentityKey)?.ColumnCode ?? visible.FirstOrDefault()?.ColumnCode ?? "id";
        if (valueField != "id" && !visible.Any(c => c.ColumnCode == valueField)) return false;
        var scope = AuthService.ResolveRowScope(user, app.Code, target.Id, target.RowScope);
        await using var cmd = new NpgsqlCommand($"""
            SELECT 1 FROM app_records WHERE table_id=@tbl AND deleted_at IS NULL
              AND COALESCE(data ->> @field, id)=@value AND {OwnerFilterSqlClause} LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tbl", target.Id);
        cmd.Parameters.AddWithValue("field", valueField);
        cmd.Parameters.AddWithValue("value", value);
        AddOwnerFilterParams(cmd, scope == "ALL" ? null : user.Username, user.Email, user.EmployeeCode,
            scope == "DEPARTMENT" ? (string.IsNullOrWhiteSpace(user.Department) ? "__UNASSIGNED__" : user.Department) : null);
        return await cmd.ExecuteScalarAsync() != null;
    }

    private static void AddImportError(ImportRowPreviewDto row, string field, string message) =>
        row.Errors.Add(new ValidationErrorItem { Severity = "ERROR", Field = field, Message = message });
}
