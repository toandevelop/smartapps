using Npgsql;
using NpgsqlTypes;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public sealed class OrgUnitService
{
    private readonly string _connStr;

    public OrgUnitService(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
    }

    public async Task<List<SysOrgUnitDto>> GetOrgUnitsAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, code, name, parent_id, manager_username, order_index, is_active, created_at, updated_at
            FROM sys_org_units
            WHERE is_active = TRUE
            ORDER BY order_index ASC, name ASC
            """, conn);
        var list = new List<SysOrgUnitDto>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new SysOrgUnitDto
            {
                Id = r.GetString(0),
                Code = r.GetString(1),
                Name = r.GetString(2),
                ParentId = r.IsDBNull(3) ? null : r.GetString(3),
                ManagerUsername = r.IsDBNull(4) ? null : r.GetString(4),
                OrderIndex = r.GetInt32(5),
                IsActive = r.GetBoolean(6),
                CreatedAt = r.GetDateTime(7),
                UpdatedAt = r.GetDateTime(8)
            });
        }
        return list;
    }

    public async Task<SysOrgUnitDto> UpsertOrgUnitAsync(UpsertOrgUnitRequest req)
    {
        var code = req.Code.Trim().ToUpperInvariant();
        var id = string.IsNullOrWhiteSpace(req.Id) ? $"org_{code.ToLowerInvariant()}_{Guid.NewGuid().ToString("N")[..4]}" : req.Id.Trim();
        var parentId = string.IsNullOrWhiteSpace(req.ParentId) ? null : req.ParentId.Trim();
        if (parentId == id)
            throw new InvalidOperationException("Đơn vị không thể trực thuộc chính nó.");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        // Cycle check when setting parentId
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            var all = await GetOrgUnitsAsync();
            var map = all.ToDictionary(u => u.Id, StringComparer.Ordinal);
            var cursor = parentId;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (cursor != null)
            {
                if (cursor == id)
                    throw new InvalidOperationException("Phát hiện vòng lặp quan hệ cha–con trong cơ cấu tổ chức.");
                if (!visited.Add(cursor)) break;
                cursor = map.TryGetValue(cursor, out var p) ? p.ParentId : null;
            }
        }

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_org_units (id, code, name, parent_id, manager_username, order_index, is_active, updated_at)
            VALUES (@id, @code, @name, @pid, @mgr, @ord, @act, CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO UPDATE SET
                code = EXCLUDED.code,
                name = EXCLUDED.name,
                parent_id = EXCLUDED.parent_id,
                manager_username = EXCLUDED.manager_username,
                order_index = EXCLUDED.order_index,
                is_active = EXCLUDED.is_active,
                updated_at = CURRENT_TIMESTAMP
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", req.Name.Trim());
        cmd.Parameters.Add(new NpgsqlParameter("pid", NpgsqlDbType.Varchar) { Value = (object?)parentId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("mgr", NpgsqlDbType.Varchar) { Value = string.IsNullOrWhiteSpace(req.ManagerUsername) ? DBNull.Value : req.ManagerUsername.Trim() });
        cmd.Parameters.AddWithValue("ord", req.OrderIndex);
        cmd.Parameters.AddWithValue("act", req.IsActive);
        await cmd.ExecuteNonQueryAsync();

        return (await GetOrgUnitsAsync()).First(o => o.Id == id);
    }

    public async Task<bool> DeleteOrgUnitAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_org_units WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<string>> GetSubtreeUsernamesAsync(string orgUnitId)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            WITH RECURSIVE org_tree AS (
                SELECT id, name FROM sys_org_units WHERE id = @rootId AND is_active = TRUE
                UNION ALL
                SELECT c.id, c.name
                FROM sys_org_units c
                JOIN org_tree p ON c.parent_id = p.id
                WHERE c.is_active = TRUE
            )
            SELECT DISTINCT u.username
            FROM sys_users u
            JOIN org_tree t ON u.org_unit_id = t.id OR LOWER(COALESCE(u.department, '')) = LOWER(t.name)
            WHERE u.is_active = TRUE
            ORDER BY u.username
            """, conn);
        cmd.Parameters.AddWithValue("rootId", orgUnitId);

        var users = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) users.Add(r.GetString(0));
        return users;
    }
}
