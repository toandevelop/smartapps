using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class AuthService
{
    private readonly string _connStr;
    private readonly string _hmacSecret;
    private static readonly ConcurrentDictionary<string, (int Fails, DateTimeOffset LockUntil)> _loginAttempts = new();

    public AuthService(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        var configuredSecret = config["Auth:Secret"];
        var env = config["ASPNETCORE_ENVIRONMENT"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var isProduction = string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
        if (isProduction && (string.IsNullOrWhiteSpace(configuredSecret)
            || configuredSecret is "SmartApps_Enterprise_Default_Hmac_Secret_2026" or "SmartApps_Enterprise_Production_Secret_Change_Me"))
        {
            throw new InvalidOperationException("Auth:Secret must be configured to a strong non-default value in Production.");
        }
        _hmacSecret = string.IsNullOrWhiteSpace(configuredSecret)
            ? "SmartApps_Enterprise_Default_Hmac_Secret_2026"
            : configuredSecret;
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);
        return $"pbkdf2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static string LegacySha256(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"smartapps::{password}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool VerifyPassword(string password, string storedHash, out bool needsUpgrade)
    {
        needsUpgrade = false;
        if (storedHash.StartsWith("pbkdf2$", StringComparison.Ordinal))
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations)) return false;
            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }

        // Backward-compatible verification for legacy seeded SHA256 passwords
        var legacy = LegacySha256(password);
        if (string.Equals(storedHash, legacy, StringComparison.OrdinalIgnoreCase))
        {
            needsUpgrade = true;
            return true;
        }
        return false;
    }

    public async Task<string> CreateTokenAsync(SysUserDto user)
    {
        // Read the persisted setting at issuance; changing it does not rewrite existing sessions.
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT setting_value::text FROM sys_system_settings WHERE setting_key = 'workspace_config' LIMIT 1", conn);
        var raw = await cmd.ExecuteScalarAsync() as string;
        var settings = string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<SystemSettingsDto>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return CreateToken(user, settings?.SessionTimeoutDays ?? 7);
    }

    public string CreateToken(SysUserDto user, int sessionTimeoutDays = 7)
    {
        var payloadObj = new
        {
            id = user.Id,
            username = user.Username,
            roleCode = user.RoleCode,
            jti = Guid.NewGuid().ToString("N")[..12],
            exp = DateTimeOffset.UtcNow.AddDays(Math.Clamp(sessionTimeoutDays, 1, 30)).ToUnixTimeSeconds()
        };
        var json = JsonSerializer.Serialize(payloadObj);
        var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hmacSecret));
        var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64))).ToLowerInvariant();
        return $"{payloadB64}.{sig}";
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public DateTimeOffset GetTokenExpiration(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) throw new ArgumentException("Invalid session token.", nameof(token));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hmacSecret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[1])))
            throw new ArgumentException("Invalid session signature.", nameof(token));
        using var payload = JsonDocument.Parse(Convert.FromBase64String(parts[0]));
        return DateTimeOffset.FromUnixTimeSeconds(payload.RootElement.GetProperty("exp").GetInt64());
    }

    public async Task RevokeTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        DateTimeOffset expiresAt;
        try { expiresAt = GetTokenExpiration(token); }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return; }
        if (expiresAt <= DateTimeOffset.UtcNow) return;
        var hash = HashToken(token);
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_revoked_tokens (token_hash, expires_at)
            VALUES (@h, @expires)
            ON CONFLICT (token_hash) DO NOTHING;
            DELETE FROM sys_revoked_tokens WHERE expires_at < CURRENT_TIMESTAMP;
            """, conn);
        cmd.Parameters.AddWithValue("h", hash);
        cmd.Parameters.AddWithValue("expires", expiresAt.UtcDateTime);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<SysUserDto?> ValidateTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hmacSecret));
        var expectedSig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSig),
                Encoding.UTF8.GetBytes(parts[1])))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
            using var doc = JsonDocument.Parse(json);
            var exp = doc.RootElement.GetProperty("exp").GetInt64();
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= exp) return null;

            await using (var conn = new NpgsqlConnection(_connStr))
            {
                await conn.OpenAsync();
                await using var revCmd = new NpgsqlCommand(
                    "SELECT 1 FROM sys_revoked_tokens WHERE token_hash = @h LIMIT 1", conn);
                revCmd.Parameters.AddWithValue("h", HashToken(token));
                if (await revCmd.ExecuteScalarAsync() != null) return null;
            }

            var username = doc.RootElement.GetProperty("username").GetString();
            return string.IsNullOrWhiteSpace(username) ? null : await GetByUsernameAsync(username);
        }
        catch
        {
            return null;
        }
    }

    public async Task<SysUserDto?> ResolveRequestUserAsync(HttpRequest req)
    {
        var token = req.Cookies["smartapps_token"]
            ?? req.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "");
        return await ValidateTokenAsync(token);
    }

    public static PermissionCatalogDto GetPermissionCatalog() => new()
    {
        SystemPermissions = new List<PermissionCatalogItemDto>
        {
            new() { Code = "studio.manage", Name = "Thiết kế App Studio", Description = "Tạo/sửa/xóa App, Phân hệ, Bảng động, Cột, Danh mục & Blueprint", Scope = "SYSTEM" },
            new() { Code = "user.manage", Name = "Quản lý Người dùng", Description = "Tạo, sửa, khóa tài khoản và đặt lại mật khẩu người dùng toàn hệ thống", Scope = "SYSTEM" },
            new() { Code = "role.manage", Name = "Quản lý Vai trò & Phân quyền", Description = "Tạo Custom Roles, cấu hình quyền hệ thống và phân quyền vào từng App", Scope = "SYSTEM" },
            new() { Code = "workflow.manage", Name = "Thiết kế Quy trình (Workflow)", Description = "Xây dựng quy trình phê duyệt đa bước cho từng bảng dữ liệu", Scope = "SYSTEM" },
            new() { Code = "automation.manage", Name = "Quản lý Tự động hóa", Description = "Cấu hình và chạy thử quy tắc tự động hóa đa App", Scope = "SYSTEM" },
            new() { Code = "audit.view", Name = "Xem Nhật ký Hệ thống", Description = "Tra cứu lịch sử kiểm toán thay đổi dữ liệu toàn hệ thống", Scope = "SYSTEM" },
            new() { Code = "integration.manage", Name = "Quản lý Tích hợp API", Description = "Cấp và thu hồi Inbound API Keys", Scope = "SYSTEM" }
        },
        AppPermissions = new List<PermissionCatalogItemDto>
        {
            new() { Code = "app.admin", Name = "Quản trị App", Description = "Mở Chế độ Quản trị App, xem toàn bộ dữ liệu và phân quyền từng bảng trong App", Scope = "APP" },
            new() { Code = "record.view", Name = "Xem dữ liệu", Description = "Truy cập App và xem danh sách bản ghi", Scope = "APP" },
            new() { Code = "record.view_all", Name = "Xem toàn bộ bản ghi", Description = "Bỏ giới hạn OWNER_ONLY để xem dữ liệu của mọi nhân sự trong App", Scope = "APP" },
            new() { Code = "record.create", Name = "Thêm mới & Import", Description = "Tạo bản ghi mới, Import Excel và khôi phục từ thùng rác", Scope = "APP" },
            new() { Code = "record.edit", Name = "Chỉnh sửa dữ liệu", Description = "Sửa bản ghi, sửa trực tiếp trên bảng và kiểm tra công thức", Scope = "APP" },
            new() { Code = "record.delete", Name = "Xóa dữ liệu", Description = "Xóa mềm bản ghi vào thùng rác", Scope = "APP" },
            new() { Code = "record.export", Name = "Xuất Excel", Description = "Xuất dữ liệu bảng động ra file .xlsx", Scope = "APP" },
            new() { Code = "record.approve", Name = "Phê duyệt hồ sơ", Description = "Duyệt hoặc từ chối hồ sơ trong quy trình phê duyệt của App", Scope = "APP" },
            new() { Code = "widget.manage", Name = "Quản lý Dashboard", Description = "Thêm, sửa, sắp xếp Widget trên App Dashboard", Scope = "APP" }
        }
    };

    public static bool IsAdminUser(SysUserDto? user)
    {
        if (user == null) return false;
        return user.IsAdmin
            || user.RoleCode.Equals("ADMIN", StringComparison.OrdinalIgnoreCase)
            || user.RoleCodes.Contains("ADMIN", StringComparer.OrdinalIgnoreCase);
    }

    public static bool HasPermission(SysUserDto? user, string permission)
    {
        if (user == null) return false;
        if (IsAdminUser(user)) return true;
        return user.SystemPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase)
            || user.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    public static bool CanAccessApp(SysUserDto? user, string appCode)
    {
        if (user == null || string.IsNullOrWhiteSpace(appCode)) return false;
        if (IsAdminUser(user)) return true;
        if (user.AllowedApps.Contains("*")) return true;
        if (user.AppPermissions.TryGetValue(appCode, out var appPerms) && appPerms.Count > 0) return true;
        return user.AllowedApps.Any(a => a.Equals(appCode, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasAppPermission(SysUserDto? user, string? appCode, string permission)
    {
        if (user == null) return false;
        if (IsAdminUser(user)) return true;
        if (string.IsNullOrWhiteSpace(appCode)) return HasPermission(user, permission);

        if (user.AppPermissions.Count > 0)
        {
            if (user.AppPermissions.TryGetValue(appCode, out var perms))
            {
                return perms.Contains(permission, StringComparer.OrdinalIgnoreCase);
            }
            if (user.AllowedApps.Contains("*"))
            {
                return user.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
            }
            return false;
        }

        return CanAccessApp(user, appCode) && HasPermission(user, permission);
    }

    public static bool HasAnyRole(SysUserDto? user, IEnumerable<string> targetRoles)
    {
        if (user == null) return false;
        if (IsAdminUser(user)) return true;
        var userRoles = user.RoleCodes.Count > 0 ? user.RoleCodes : new List<string> { user.RoleCode };
        var targets = targetRoles as ICollection<string> ?? targetRoles.ToList();
        return userRoles.Any(ur => targets.Contains(ur, StringComparer.OrdinalIgnoreCase));
    }

    public static string ResolveRowScope(
        SysUserDto? user,
        string? appCode,
        string? tableId,
        string? tableRowScope,
        bool forcePersonalMode = false)
    {
        if (user == null) return "OWNER_ONLY";
        if (forcePersonalMode) return "OWNER_ONLY";
        if (IsAdminUser(user)) return "ALL";
        if (!string.IsNullOrWhiteSpace(appCode) && HasAppPermission(user, appCode, "app.admin")) return "ALL";

        if (!string.IsNullOrWhiteSpace(tableId) && user.TablePermissions.TryGetValue(tableId, out var tp))
        {
            var ts = (tp.RowScope ?? "").Trim().ToUpperInvariant();
            if (ts is "OWNER_ONLY" or "DEPARTMENT" or "ALL") return ts;
        }

        if (!string.IsNullOrWhiteSpace(appCode) && user.AppRowScopes.TryGetValue(appCode, out var roleScope))
        {
            var rs = (roleScope ?? "").Trim().ToUpperInvariant();
            if (rs is "OWNER_ONLY" or "DEPARTMENT" or "ALL") return rs;
        }

        var tScope = (tableRowScope ?? "").Trim().ToUpperInvariant();
        if (tScope is "OWNER_ONLY" or "DEPARTMENT")
        {
            return HasAppPermission(user, appCode, "record.view_all") ? "ALL" : tScope;
        }
        return "ALL";
    }

    private static int GetRowScopeRank(string? scope) => (scope ?? "").Trim().ToUpperInvariant() switch
    {
        "ALL" => 3,
        "DEPARTMENT" => 2,
        "OWNER_ONLY" => 1,
        _ => 0
    };

    public static bool IsOwnerScopedForApp(SysUserDto? user, string? appCode, string? tableRowScope)
    {
        return ResolveRowScope(user, appCode, null, tableRowScope, false) != "ALL";
    }

    public static bool CanAccessTable(SysUserDto? user, string? appCode, string? tableId, string action)
    {
        if (user == null) return false;
        if (IsAdminUser(user)) return true;
        if (!string.IsNullOrWhiteSpace(appCode) && !CanAccessApp(user, appCode)) return false;

        if (!string.IsNullOrWhiteSpace(tableId) && user.TablePermissions.TryGetValue(tableId, out var tp))
        {
            return action.ToLowerInvariant() switch
            {
                "view" => tp.CanView,
                "create" => tp.CanCreate,
                "edit" => tp.CanEdit,
                "delete" => tp.CanDelete,
                "export" => tp.CanExport,
                "approve" => tp.CanApprove,
                _ => false
            };
        }

        var perm = action.ToLowerInvariant() switch
        {
            "view" => "record.view",
            "create" => "record.create",
            "edit" => "record.edit",
            "delete" => "record.delete",
            "export" => "record.export",
            "approve" => "record.approve",
            _ => $"record.{action}"
        };
        return HasAppPermission(user, appCode, perm);
    }

    public static bool IsOwnerScopedForTable(
        SysUserDto? user,
        string? appCode,
        string? tableId,
        string? tableRowScope,
        bool forcePersonalMode = false)
    {
        return ResolveRowScope(user, appCode, tableId, tableRowScope, forcePersonalMode) != "ALL";
    }

    private const string UserSelectColumns = """
        id, username, full_name, email, employee_code, department, role_code, COALESCE(role_codes::text, '[]'),
        password_hash, allowed_apps::text, permissions::text, is_active, sso_provider, sso_subject
        """;

    public async Task<(SysUserDto? User, string? Token, string? Error)> LoginAsync(string username, string password)
    {
        var key = username.Trim().ToLowerInvariant();
        if (_loginAttempts.TryGetValue(key, out var attempt) && attempt.LockUntil > DateTimeOffset.UtcNow)
        {
            var secs = Math.Max(1, (int)(attempt.LockUntil - DateTimeOffset.UtcNow).TotalSeconds);
            return (null, null, $"Tài khoản tạm khóa do nhập sai nhiều lần. Vui lòng thử lại sau {secs} giây.");
        }

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = $"""
            SELECT {UserSelectColumns}
            FROM sys_users
            WHERE LOWER(username) = LOWER(@u) AND is_active = TRUE
            LIMIT 1
            """;

        string storedHash;
        SysUserDto user;
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("u", username.Trim());
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                RecordFailure(key);
                return (null, null, "Sai tên đăng nhập hoặc mật khẩu.");
            }
            storedHash = r.GetString(8);
            user = ReadUser(r);
        }

        if (!VerifyPassword(password, storedHash, out var needsUpgrade))
        {
            RecordFailure(key);
            return (null, null, "Sai tên đăng nhập hoặc mật khẩu.");
        }

        _loginAttempts.TryRemove(key, out _);

        if (needsUpgrade)
        {
            await using var up = new NpgsqlCommand("UPDATE sys_users SET password_hash = @ph WHERE id = @id", conn);
            up.Parameters.AddWithValue("ph", HashPassword(password));
            up.Parameters.AddWithValue("id", user.Id);
            await up.ExecuteNonQueryAsync();
        }

        await EnrichUsersWithRolesAsync(conn, new List<SysUserDto> { user });
        return (user, await CreateTokenAsync(user), null);
    }

    private static void RecordFailure(string key)
    {
        _loginAttempts.AddOrUpdate(
            key,
            _ => (1, DateTimeOffset.MinValue),
            (_, prev) =>
            {
                var next = prev.Fails + 1;
                return next >= 5 ? (0, DateTimeOffset.UtcNow.AddSeconds(60)) : (next, DateTimeOffset.MinValue);
            });
    }

    public async Task<(bool Ok, string? Error)> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            return (false, "Mật khẩu mới phải có tối thiểu 6 ký tự.");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        string? storedHash = null;
        await using (var cmd = new NpgsqlCommand("SELECT password_hash FROM sys_users WHERE id = @id LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("id", userId);
            storedHash = (await cmd.ExecuteScalarAsync())?.ToString();
        }

        if (storedHash == null || !VerifyPassword(currentPassword, storedHash, out _))
            return (false, "Mật khẩu hiện tại không đúng.");

        await using var up = new NpgsqlCommand("UPDATE sys_users SET password_hash = @ph WHERE id = @id", conn);
        up.Parameters.AddWithValue("ph", HashPassword(newPassword));
        up.Parameters.AddWithValue("id", userId);
        await up.ExecuteNonQueryAsync();
        return (true, null);
    }

    public async Task<bool> ResetPasswordAsync(string userId, string newPassword)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE sys_users SET password_hash = @ph WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("ph", HashPassword(string.IsNullOrWhiteSpace(newPassword) ? "123456" : newPassword));
        cmd.Parameters.AddWithValue("id", userId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<(bool Ok, string? Error)> SetUserActiveAsync(string userId, bool isActive, string currentUserId)
    {
        if (!isActive && (userId == currentUserId || userId == "usr_admin"))
            return (false, "Không thể khóa tài khoản quản trị mặc định hoặc chính bạn.");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE sys_users SET is_active = @act WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("act", isActive);
        cmd.Parameters.AddWithValue("id", userId);
        var updated = await cmd.ExecuteNonQueryAsync() > 0;
        return updated ? (true, null) : (false, "Không tìm thấy tài khoản.");
    }

    public async Task<(bool Ok, string? Error)> DeleteUserAsync(string userId, string currentUserId)
    {
        if (userId == currentUserId || userId == "usr_admin")
            return (false, "Không thể xóa tài khoản quản trị mặc định hoặc chính bạn.");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        var deleted = await cmd.ExecuteNonQueryAsync() > 0;
        return deleted ? (true, null) : (false, "Không tìm thấy tài khoản.");
    }

    public async Task<List<SysRoleDto>> ListRolesAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        return await LoadRolesInternalAsync(conn);
    }

    private static async Task<List<SysRoleDto>> LoadRolesInternalAsync(NpgsqlConnection conn)
    {
        var roleSql = """
            SELECT r.id, r.code, r.name, r.description, r.color,
                   r.is_system, COALESCE(r.is_active, TRUE),
                   COALESCE(r.all_apps_access, FALSE),
                   COALESCE(r.default_app_permissions::text, '[]'),
                   COALESCE(r.system_permissions::text, '[]'),
                   r.created_at,
                   COALESCE(r.updated_at, r.created_at),
                   (SELECT COUNT(*) FROM sys_users u
                    WHERE UPPER(u.role_code) = UPPER(r.code)
                       OR EXISTS (SELECT 1 FROM jsonb_array_elements_text(COALESCE(u.role_codes, '[]'::jsonb)) rc WHERE UPPER(rc) = UPPER(r.code))
                   ) AS user_count
            FROM sys_roles r
            ORDER BY r.is_system DESC, r.created_at ASC
            """;

        var list = new List<SysRoleDto>();
        await using (var cmd = new NpgsqlCommand(roleSql, conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                list.Add(new SysRoleDto
                {
                    Id = r.GetString(0),
                    Code = r.GetString(1),
                    Name = r.GetString(2),
                    Description = r.IsDBNull(3) ? null : r.GetString(3),
                    Color = r.IsDBNull(4) ? "#1a73e8" : r.GetString(4),
                    IsSystem = r.GetBoolean(5),
                    IsActive = r.GetBoolean(6),
                    AllAppsAccess = r.GetBoolean(7),
                    DefaultAppPermissions = JsonSerializer.Deserialize<List<string>>(r.GetString(8)) ?? new(),
                    SystemPermissions = JsonSerializer.Deserialize<List<string>>(r.GetString(9)) ?? new(),
                    CreatedAt = r.GetDateTime(10),
                    UpdatedAt = r.GetDateTime(11),
                    UserCount = Convert.ToInt32(r.GetInt64(12))
                });
            }
        }

        var byId = list.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var rapSql = """
            SELECT rap.role_id, rap.app_id, ap.code, ap.name, ap.color,
                   COALESCE(rap.permissions::text, '[]'), COALESCE(rap.row_scope, 'INHERIT')
            FROM sys_role_app_permissions rap
            JOIN sys_apps ap ON ap.id = rap.app_id
            WHERE ap.is_active = TRUE
            ORDER BY ap.order_index ASC, ap.name ASC
            """;

        await using (var rapCmd = new NpgsqlCommand(rapSql, conn))
        await using (var rr = await rapCmd.ExecuteReaderAsync())
        {
            while (await rr.ReadAsync())
            {
                var roleId = rr.GetString(0);
                if (!byId.TryGetValue(roleId, out var role)) continue;
                role.AppPermissions.Add(new RoleAppPermissionDto
                {
                    AppId = rr.GetString(1),
                    AppCode = rr.GetString(2),
                    AppName = rr.GetString(3),
                    AppColor = rr.IsDBNull(4) ? "#1a73e8" : rr.GetString(4),
                    Permissions = JsonSerializer.Deserialize<List<string>>(rr.GetString(5)) ?? new(),
                    RowScope = rr.IsDBNull(6) ? "INHERIT" : rr.GetString(6)
                });
            }
        }

        return list;
    }

    public async Task<(SysRoleDto? Role, string? Error)> UpsertRoleAsync(UpsertRoleRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var code = req.Code.Trim().ToUpperInvariant().Replace(" ", "_");
        if (string.IsNullOrWhiteSpace(code))
            return (null, "Mã vai trò không hợp lệ.");

        var id = string.IsNullOrWhiteSpace(req.Id) ? $"role_{code.ToLowerInvariant()}" : req.Id.Trim();
        var isSystemAdmin = code.Equals("ADMIN", StringComparison.OrdinalIgnoreCase);

        var sysPerms = (req.SystemPermissions ?? new())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var defAppPerms = (req.DefaultAppPermissions ?? new())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (isSystemAdmin)
        {
            var cat = GetPermissionCatalog();
            sysPerms = cat.SystemPermissions.Select(x => x.Code).ToList();
            defAppPerms = cat.AppPermissions.Select(x => x.Code).ToList();
        }

        var sql = """
            INSERT INTO sys_roles (id, code, name, description, color, is_system, is_active, all_apps_access, default_app_permissions, system_permissions, updated_at)
            VALUES (@id, @c, @n, @d, @col, @sys, @act, @all, @dap::jsonb, @sp::jsonb, CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                color = EXCLUDED.color,
                is_active = CASE WHEN sys_roles.is_system THEN TRUE ELSE EXCLUDED.is_active END,
                all_apps_access = CASE WHEN UPPER(sys_roles.code) = 'ADMIN' THEN TRUE ELSE EXCLUDED.all_apps_access END,
                default_app_permissions = EXCLUDED.default_app_permissions,
                system_permissions = EXCLUDED.system_permissions,
                updated_at = CURRENT_TIMESTAMP
            """;

        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("c", code);
            cmd.Parameters.AddWithValue("n", req.Name.Trim());
            cmd.Parameters.AddWithValue("d", (object?)req.Description?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("col", string.IsNullOrWhiteSpace(req.Color) ? "#1a73e8" : req.Color.Trim());
            cmd.Parameters.AddWithValue("sys", isSystemAdmin);
            cmd.Parameters.AddWithValue("act", isSystemAdmin || req.IsActive);
            cmd.Parameters.AddWithValue("all", isSystemAdmin || req.AllAppsAccess);
            cmd.Parameters.AddWithValue("dap", JsonSerializer.Serialize(defAppPerms));
            cmd.Parameters.AddWithValue("sp", JsonSerializer.Serialize(sysPerms));
            await cmd.ExecuteNonQueryAsync();
        }

        // Replace per-app permissions for this role (unless it's ADMIN which always gets full access)
        if (!isSystemAdmin)
        {
            await using (var delRap = new NpgsqlCommand("DELETE FROM sys_role_app_permissions WHERE role_id = @rid", conn, tx))
            {
                delRap.Parameters.AddWithValue("rid", id);
                await delRap.ExecuteNonQueryAsync();
            }

            foreach (var ap in req.AppPermissions ?? new())
            {
                if (string.IsNullOrWhiteSpace(ap.AppId)) continue;
                var perms = (ap.Permissions ?? new())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (perms.Count == 0) continue;

                var rowScope = string.IsNullOrWhiteSpace(ap.RowScope) ? "INHERIT" : ap.RowScope.Trim().ToUpperInvariant();
                await using var insRap = new NpgsqlCommand("""
                    INSERT INTO sys_role_app_permissions (id, role_id, app_id, permissions, row_scope, updated_at)
                    VALUES (@id, @rid, @aid, @perms::jsonb, @rs, CURRENT_TIMESTAMP)
                    ON CONFLICT (role_id, app_id) DO UPDATE SET
                        permissions = EXCLUDED.permissions,
                        row_scope = EXCLUDED.row_scope,
                        updated_at = CURRENT_TIMESTAMP
                    """, conn, tx);
                insRap.Parameters.AddWithValue("id", $"rap_{id}_{ap.AppId}");
                insRap.Parameters.AddWithValue("rid", id);
                insRap.Parameters.AddWithValue("aid", ap.AppId.Trim());
                insRap.Parameters.AddWithValue("perms", JsonSerializer.Serialize(perms));
                insRap.Parameters.AddWithValue("rs", rowScope);
                await insRap.ExecuteNonQueryAsync();
            }
        }

        await tx.CommitAsync();
        var saved = (await LoadRolesInternalAsync(conn)).FirstOrDefault(x => x.Id == id || x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        return (saved, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteRoleAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var chk = new NpgsqlCommand("""
            SELECT r.is_system, r.code,
                   (SELECT COUNT(*) FROM sys_users u
                    WHERE UPPER(u.role_code) = UPPER(r.code)
                       OR EXISTS (SELECT 1 FROM jsonb_array_elements_text(COALESCE(u.role_codes, '[]'::jsonb)) rc WHERE UPPER(rc) = UPPER(r.code)))
            FROM sys_roles r WHERE r.id = @id LIMIT 1
            """, conn);
        chk.Parameters.AddWithValue("id", id);
        await using var reader = await chk.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (false, "Không tìm thấy vai trò.");
        if (reader.GetBoolean(0) || reader.GetString(1).Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
            return (false, "Không thể xóa vai trò hệ thống mặc định.");
        var userCount = reader.GetInt64(2);
        if (userCount > 0)
            return (false, $"Đang có {userCount} người dùng mang vai trò này. Hãy chuyển vai trò cho họ trước khi xóa.");
        await reader.CloseAsync();

        await using var del = new NpgsqlCommand("DELETE FROM sys_roles WHERE id = @id", conn);
        del.Parameters.AddWithValue("id", id);
        await del.ExecuteNonQueryAsync();
        return (true, null);
    }

    public async Task<List<AppRoleMatrixItemDto>?> GetAppRoleMatrixAsync(string appIdOrCode)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        string? appId = null;
        await using (var appCmd = new NpgsqlCommand(
            "SELECT id FROM sys_apps WHERE id = @v OR UPPER(code) = UPPER(@v) LIMIT 1", conn))
        {
            appCmd.Parameters.AddWithValue("v", appIdOrCode.Trim());
            appId = (await appCmd.ExecuteScalarAsync())?.ToString();
        }
        if (string.IsNullOrWhiteSpace(appId)) return null;

        var roles = await LoadRolesInternalAsync(conn);
        var result = new List<AppRoleMatrixItemDto>();
        foreach (var role in roles)
        {
            var rap = role.AppPermissions.FirstOrDefault(x => x.AppId == appId);
            result.Add(new AppRoleMatrixItemDto
            {
                RoleId = role.Id,
                RoleCode = role.Code,
                RoleName = role.Name,
                RoleColor = role.Color,
                IsSystem = role.IsSystem,
                AllAppsAccess = role.AllAppsAccess,
                HasAccess = role.AllAppsAccess || rap != null,
                Permissions = role.AllAppsAccess
                    ? (rap?.Permissions.Count > 0 ? rap.Permissions : role.DefaultAppPermissions)
                    : (rap?.Permissions ?? new List<string>()),
                RowScope = rap?.RowScope ?? "INHERIT"
            });
        }
        return result;
    }

    public async Task<(bool Ok, string? Error)> SaveAppRoleMatrixAsync(string appIdOrCode, UpdateAppRoleMatrixRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        string? appId = null;
        await using (var appCmd = new NpgsqlCommand(
            "SELECT id FROM sys_apps WHERE id = @v OR UPPER(code) = UPPER(@v) LIMIT 1", conn))
        {
            appCmd.Parameters.AddWithValue("v", appIdOrCode.Trim());
            appId = (await appCmd.ExecuteScalarAsync())?.ToString();
        }
        if (string.IsNullOrWhiteSpace(appId)) return (false, "Không tìm thấy ứng dụng.");

        await using var tx = await conn.BeginTransactionAsync();
        foreach (var item in req.Assignments ?? new())
        {
            if (string.IsNullOrWhiteSpace(item.RoleId)) continue;

            // Skip locked ADMIN role
            await using var chk = new NpgsqlCommand("SELECT code, is_system, all_apps_access FROM sys_roles WHERE id = @id LIMIT 1", conn, tx);
            chk.Parameters.AddWithValue("id", item.RoleId);
            string? rCode = null;
            bool allApps = false;
            await using (var r = await chk.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) continue;
                rCode = r.GetString(0);
                allApps = r.GetBoolean(2);
            }
            if (rCode.Equals("ADMIN", StringComparison.OrdinalIgnoreCase)) continue;
            if (allApps) continue; // Wildcard roles follow default_app_permissions

            var perms = (item.Permissions ?? new()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!item.HasAccess || perms.Count == 0)
            {
                await using var del = new NpgsqlCommand(
                    "DELETE FROM sys_role_app_permissions WHERE role_id = @rid AND app_id = @aid", conn, tx);
                del.Parameters.AddWithValue("rid", item.RoleId);
                del.Parameters.AddWithValue("aid", appId);
                await del.ExecuteNonQueryAsync();
            }
            else
            {
                var rowScope = string.IsNullOrWhiteSpace(item.RowScope) ? "INHERIT" : item.RowScope.Trim().ToUpperInvariant();
                await using var up = new NpgsqlCommand("""
                    INSERT INTO sys_role_app_permissions (id, role_id, app_id, permissions, row_scope, updated_at)
                    VALUES (@id, @rid, @aid, @perms::jsonb, @rs, CURRENT_TIMESTAMP)
                    ON CONFLICT (role_id, app_id) DO UPDATE SET
                        permissions = EXCLUDED.permissions,
                        row_scope = EXCLUDED.row_scope,
                        updated_at = CURRENT_TIMESTAMP
                    """, conn, tx);
                up.Parameters.AddWithValue("id", $"rap_{item.RoleId}_{appId}");
                up.Parameters.AddWithValue("rid", item.RoleId);
                up.Parameters.AddWithValue("aid", appId);
                up.Parameters.AddWithValue("perms", JsonSerializer.Serialize(perms));
                up.Parameters.AddWithValue("rs", rowScope);
                await up.ExecuteNonQueryAsync();
            }
        }
        await tx.CommitAsync();
        return (true, null);
    }

    public async Task<List<RoleTablePermissionItemDto>?> GetAppTableRoleMatrixAsync(string appIdOrCode)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        string? appId = null;
        await using (var appCmd = new NpgsqlCommand(
            "SELECT id FROM sys_apps WHERE id = @v OR UPPER(code) = UPPER(@v) LIMIT 1", conn))
        {
            appCmd.Parameters.AddWithValue("v", appIdOrCode.Trim());
            appId = (await appCmd.ExecuteScalarAsync())?.ToString();
        }
        if (string.IsNullOrWhiteSpace(appId)) return null;

        var roles = await LoadRolesInternalAsync(conn);
        var tables = new List<(string Id, string Name, string DisplayName, string RowScope)>();
        await using (var tblCmd = new NpgsqlCommand("""
            SELECT id, table_name, display_name, COALESCE(row_scope, 'ALL')
            FROM sys_tables
            WHERE app_id = @aid AND is_active = TRUE
            ORDER BY order_index ASC, display_name ASC
            """, conn))
        {
            tblCmd.Parameters.AddWithValue("aid", appId);
            await using var tr = await tblCmd.ExecuteReaderAsync();
            while (await tr.ReadAsync())
            {
                tables.Add((tr.GetString(0), tr.GetString(1), tr.GetString(2), tr.GetString(3)));
            }
        }

        var rules = new Dictionary<(string RoleId, string TableId), (bool V, bool C, bool E, bool D, bool Ex, bool Ap, string Scope)>();
        await using (var rCmd = new NpgsqlCommand("""
            SELECT role_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, COALESCE(row_scope, 'INHERIT')
            FROM sys_role_table_permissions
            WHERE app_id = @aid
            """, conn))
        {
            rCmd.Parameters.AddWithValue("aid", appId);
            await using var rr = await rCmd.ExecuteReaderAsync();
            while (await rr.ReadAsync())
            {
                rules[(rr.GetString(0), rr.GetString(1))] = (
                    rr.GetBoolean(2), rr.GetBoolean(3), rr.GetBoolean(4),
                    rr.GetBoolean(5), rr.GetBoolean(6), rr.GetBoolean(7), rr.GetString(8)
                );
            }
        }

        var result = new List<RoleTablePermissionItemDto>();
        foreach (var role in roles)
        {
            var rap = role.AppPermissions.FirstOrDefault(x => x.AppId == appId);
            if (!role.AllAppsAccess && rap == null) continue;
            var appPerms = role.AllAppsAccess ? role.DefaultAppPermissions : rap!.Permissions;

            foreach (var tbl in tables)
            {
                var hasRule = rules.TryGetValue((role.Id, tbl.Id), out var rule);
                var isAdminRole = role.Code.Equals("ADMIN", StringComparison.OrdinalIgnoreCase);
                result.Add(new RoleTablePermissionItemDto
                {
                    RoleId = role.Id,
                    RoleCode = role.Code,
                    RoleName = role.Name,
                    RoleColor = role.Color,
                    IsSystem = role.IsSystem,
                    AllAppsAccess = role.AllAppsAccess,
                    AppId = appId,
                    TableId = tbl.Id,
                    TableName = tbl.Name,
                    TableDisplayName = tbl.DisplayName,
                    HasExplicitRule = hasRule,
                    CanView = isAdminRole || (hasRule ? rule.V : appPerms.Contains("record.view", StringComparer.OrdinalIgnoreCase)),
                    CanCreate = isAdminRole || (hasRule ? rule.C : appPerms.Contains("record.create", StringComparer.OrdinalIgnoreCase)),
                    CanEdit = isAdminRole || (hasRule ? rule.E : appPerms.Contains("record.edit", StringComparer.OrdinalIgnoreCase)),
                    CanDelete = isAdminRole || (hasRule ? rule.D : appPerms.Contains("record.delete", StringComparer.OrdinalIgnoreCase)),
                    CanExport = isAdminRole || (hasRule ? rule.Ex : appPerms.Contains("record.export", StringComparer.OrdinalIgnoreCase)),
                    CanApprove = isAdminRole || (hasRule ? rule.Ap : appPerms.Contains("record.approve", StringComparer.OrdinalIgnoreCase)),
                    RowScope = isAdminRole ? "ALL" : (hasRule ? rule.Scope : (rap?.RowScope ?? "INHERIT"))
                });
            }
        }
        return result;
    }

    public async Task<(bool Ok, string? Error)> SaveAppTableRoleMatrixAsync(string appIdOrCode, UpdateAppTableRoleMatrixRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        string? appId = null;
        await using (var appCmd = new NpgsqlCommand(
            "SELECT id FROM sys_apps WHERE id = @v OR UPPER(code) = UPPER(@v) LIMIT 1", conn))
        {
            appCmd.Parameters.AddWithValue("v", appIdOrCode.Trim());
            appId = (await appCmd.ExecuteScalarAsync())?.ToString();
        }
        if (string.IsNullOrWhiteSpace(appId)) return (false, "Không tìm thấy ứng dụng.");

        await using var tx = await conn.BeginTransactionAsync();
        foreach (var item in req.Rules ?? new())
        {
            if (string.IsNullOrWhiteSpace(item.RoleId) || string.IsNullOrWhiteSpace(item.TableId)) continue;

            await using var chk = new NpgsqlCommand("SELECT code FROM sys_roles WHERE id = @id LIMIT 1", conn, tx);
            chk.Parameters.AddWithValue("id", item.RoleId);
            var rCode = (await chk.ExecuteScalarAsync())?.ToString();
            if (string.IsNullOrWhiteSpace(rCode) || rCode.Equals("ADMIN", StringComparison.OrdinalIgnoreCase)) continue;

            if (item.UseAppDefault)
            {
                await using var del = new NpgsqlCommand(
                    "DELETE FROM sys_role_table_permissions WHERE role_id = @rid AND table_id = @tid", conn, tx);
                del.Parameters.AddWithValue("rid", item.RoleId);
                del.Parameters.AddWithValue("tid", item.TableId);
                await del.ExecuteNonQueryAsync();
            }
            else
            {
                var scope = string.IsNullOrWhiteSpace(item.RowScope) ? "INHERIT" : item.RowScope.Trim().ToUpperInvariant();
                await using var up = new NpgsqlCommand("""
                    INSERT INTO sys_role_table_permissions (id, role_id, app_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, row_scope, updated_at)
                    VALUES (@id, @rid, @aid, @tid, @v, @c, @e, @d, @ex, @ap, @rs, CURRENT_TIMESTAMP)
                    ON CONFLICT (role_id, table_id) DO UPDATE SET
                        can_view = EXCLUDED.can_view,
                        can_create = EXCLUDED.can_create,
                        can_edit = EXCLUDED.can_edit,
                        can_delete = EXCLUDED.can_delete,
                        can_export = EXCLUDED.can_export,
                        can_approve = EXCLUDED.can_approve,
                        row_scope = EXCLUDED.row_scope,
                        updated_at = CURRENT_TIMESTAMP
                    """, conn, tx);
                up.Parameters.AddWithValue("id", $"rtp_{item.RoleId}_{item.TableId}");
                up.Parameters.AddWithValue("rid", item.RoleId);
                up.Parameters.AddWithValue("aid", appId);
                up.Parameters.AddWithValue("tid", item.TableId);
                up.Parameters.AddWithValue("v", item.CanView);
                up.Parameters.AddWithValue("c", item.CanCreate);
                up.Parameters.AddWithValue("e", item.CanEdit);
                up.Parameters.AddWithValue("d", item.CanDelete);
                up.Parameters.AddWithValue("ex", item.CanExport);
                up.Parameters.AddWithValue("ap", item.CanApprove);
                up.Parameters.AddWithValue("rs", scope);
                await up.ExecuteNonQueryAsync();
            }
        }
        await tx.CommitAsync();
        return (true, null);
    }

    public async Task<SysUserDto?> GetByUsernameAsync(string username)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = $"""
            SELECT {UserSelectColumns}
            FROM sys_users
            WHERE LOWER(username) = LOWER(@u) AND is_active = TRUE
            LIMIT 1
            """;
        SysUserDto? user = null;
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("u", username.Trim());
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) user = ReadUser(r);
        }
        if (user == null) return null;
        await EnrichUsersWithRolesAsync(conn, new List<SysUserDto> { user });
        return user;
    }

    public async Task<List<SysUserDto>> ListUsersAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = $"""
            SELECT {UserSelectColumns}
            FROM sys_users
            ORDER BY created_at ASC
            """;
        var list = new List<SysUserDto>();
        await using (var cmd = new NpgsqlCommand(sql, conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) list.Add(ReadUser(r));
        }
        await EnrichUsersWithRolesAsync(conn, list);
        return list;
    }

    public async Task<(SysUserDto? User, string? Token, string? Error)> ResolveOrProvisionSsoUserAsync(
        string providerCode,
        string subject,
        string? email,
        string? fullName,
        string? preferredUsername,
        bool autoProvision,
        string defaultRoleCode)
    {
        var prov = providerCode.Trim().ToUpperInvariant();
        var sub = subject.Trim();
        var cleanEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        var fallbackName = !string.IsNullOrWhiteSpace(fullName)
            ? fullName.Trim()
            : (!string.IsNullOrWhiteSpace(cleanEmail) ? cleanEmail.Split('@')[0] : $"SSO User ({prov})");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        SysUserDto? matched = null;

        // 1) Match by (sso_provider, sso_subject)
        await using (var bySub = new NpgsqlCommand($"""
            SELECT {UserSelectColumns}
            FROM sys_users
            WHERE UPPER(sso_provider) = UPPER(@prov) AND sso_subject = @sub
            LIMIT 1
            """, conn))
        {
            bySub.Parameters.AddWithValue("prov", prov);
            bySub.Parameters.AddWithValue("sub", sub);
            await using var r = await bySub.ExecuteReaderAsync();
            if (await r.ReadAsync()) matched = ReadUser(r);
        }

        // 2) Fallback: match by email and bind external SSO subject
        if (matched == null && !string.IsNullOrWhiteSpace(cleanEmail))
        {
            await using (var byEmail = new NpgsqlCommand($"""
                SELECT {UserSelectColumns}
                FROM sys_users
                WHERE LOWER(email) = LOWER(@em)
                LIMIT 1
                """, conn))
            {
                byEmail.Parameters.AddWithValue("em", cleanEmail);
                await using var r = await byEmail.ExecuteReaderAsync();
                if (await r.ReadAsync()) matched = ReadUser(r);
            }

            if (matched != null)
            {
                await using var bindCmd = new NpgsqlCommand("""
                    UPDATE sys_users
                    SET sso_provider = COALESCE(NULLIF(sso_provider, ''), @prov),
                        sso_subject = COALESCE(NULLIF(sso_subject, ''), @sub)
                    WHERE id = @id
                    """, conn);
                bindCmd.Parameters.AddWithValue("prov", prov);
                bindCmd.Parameters.AddWithValue("sub", sub);
                bindCmd.Parameters.AddWithValue("id", matched.Id);
                await bindCmd.ExecuteNonQueryAsync();
                matched.SsoProvider ??= prov;
                matched.SsoSubject ??= sub;
            }
        }

        if (matched != null)
        {
            if (!matched.IsActive)
                return (null, null, "Tài khoản của bạn đang bị khóa. Liên hệ Quản trị viên để mở lại.");
            await EnrichUsersWithRolesAsync(conn, new List<SysUserDto> { matched });
            return (matched, await CreateTokenAsync(matched), null);
        }

        if (!autoProvision)
        {
            return (null, null, $"Email {cleanEmail ?? sub} chưa có tài khoản trong hệ thống và tính năng tự tạo tài khoản đang tắt.");
        }

        // 3) Auto-provision new company user assigned to defaultRoleCode
        var baseUsernameSource = !string.IsNullOrWhiteSpace(preferredUsername)
            ? preferredUsername
            : (!string.IsNullOrWhiteSpace(cleanEmail) ? cleanEmail.Split('@')[0] : $"sso_{sub[..Math.Min(8, sub.Length)]}");
        var sanitized = new string(baseUsernameSource.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_').ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "sso_user";

        var candidate = sanitized;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await using var chk = new NpgsqlCommand("SELECT 1 FROM sys_users WHERE LOWER(username) = LOWER(@u) LIMIT 1", conn);
            chk.Parameters.AddWithValue("u", candidate);
            if (await chk.ExecuteScalarAsync() == null) break;
            candidate = $"{sanitized}_{Guid.NewGuid().ToString("N")[..4]}";
        }

        var roleCode = string.IsNullOrWhiteSpace(defaultRoleCode) ? "STAFF" : defaultRoleCode.Trim().ToUpperInvariant();
        // Verify role exists, fallback to STAFF
        await using (var chkRole = new NpgsqlCommand("SELECT code FROM sys_roles WHERE UPPER(code) = UPPER(@rc) AND is_active = TRUE LIMIT 1", conn))
        {
            chkRole.Parameters.AddWithValue("rc", roleCode);
            var foundRole = (await chkRole.ExecuteScalarAsync())?.ToString();
            roleCode = string.IsNullOrWhiteSpace(foundRole) ? "STAFF" : foundRole.ToUpperInvariant();
        }

        var newId = $"usr_sso_{Guid.NewGuid().ToString("N")[..10]}";
        var randomPwHash = HashPassword(Guid.NewGuid().ToString("N"));

        await using (var ins = new NpgsqlCommand("""
            INSERT INTO sys_users (
                id, username, full_name, email, department, role_code, role_codes,
                password_hash, allowed_apps, permissions, sso_provider, sso_subject, is_active
            )
            VALUES (
                @id, @u, @fn, @em, 'Toàn công ty', @rc, jsonb_build_array(@rc),
                @ph, '[]'::jsonb, '[]'::jsonb, @prov, @sub, TRUE
            )
            """, conn))
        {
            ins.Parameters.AddWithValue("id", newId);
            ins.Parameters.AddWithValue("u", candidate);
            ins.Parameters.AddWithValue("fn", fallbackName);
            ins.Parameters.AddWithValue("em", (object?)cleanEmail ?? DBNull.Value);
            ins.Parameters.AddWithValue("rc", roleCode);
            ins.Parameters.AddWithValue("ph", randomPwHash);
            ins.Parameters.AddWithValue("prov", prov);
            ins.Parameters.AddWithValue("sub", sub);
            await ins.ExecuteNonQueryAsync();
        }

        await using (var fetchNew = new NpgsqlCommand($"""
            SELECT {UserSelectColumns}
            FROM sys_users WHERE id = @id LIMIT 1
            """, conn))
        {
            fetchNew.Parameters.AddWithValue("id", newId);
            await using var r = await fetchNew.ExecuteReaderAsync();
            await r.ReadAsync();
            matched = ReadUser(r);
        }

        await EnrichUsersWithRolesAsync(conn, new List<SysUserDto> { matched });
        return (matched, await CreateTokenAsync(matched), null);
    }

    public async Task<SysUserDto> UpsertUserAsync(UpsertUserRequest req)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var username = req.Username.Trim().ToLowerInvariant();
        var id = string.IsNullOrWhiteSpace(req.Id) ? $"usr_{username}" : req.Id.Trim();
        var hasNewPassword = !string.IsNullOrWhiteSpace(req.Password);
        var hash = HashPassword(hasNewPassword ? req.Password! : "123456");

        var roleCodes = (req.RoleCodes ?? new List<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roleCodes.Count == 0 && !string.IsNullOrWhiteSpace(req.RoleCode))
        {
            roleCodes.Add(req.RoleCode.Trim().ToUpperInvariant());
        }
        if (roleCodes.Count == 0)
        {
            roleCodes.Add("STAFF");
        }
        var primaryRoleCode = roleCodes[0];

        var sql = """
            INSERT INTO sys_users (id, username, full_name, email, employee_code, department, role_code, role_codes, password_hash, allowed_apps, permissions, is_active)
            VALUES (@id, @u, @fn, @em, @ec, @dept, @rc, @rcs::jsonb, @ph, @apps::jsonb, @perms::jsonb, @act)
            ON CONFLICT (id) DO UPDATE SET
                full_name = EXCLUDED.full_name,
                email = EXCLUDED.email,
                employee_code = EXCLUDED.employee_code,
                department = EXCLUDED.department,
                role_code = EXCLUDED.role_code,
                role_codes = EXCLUDED.role_codes,
                password_hash = CASE WHEN @hasPw THEN EXCLUDED.password_hash ELSE sys_users.password_hash END,
                allowed_apps = EXCLUDED.allowed_apps,
                permissions = EXCLUDED.permissions,
                is_active = EXCLUDED.is_active
            """;

        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("u", username);
            cmd.Parameters.AddWithValue("fn", req.FullName.Trim());
            cmd.Parameters.AddWithValue("em", (object?)req.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ec", string.IsNullOrWhiteSpace(req.EmployeeCode) ? DBNull.Value : req.EmployeeCode.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("dept", req.Department);
            cmd.Parameters.AddWithValue("rc", primaryRoleCode);
            cmd.Parameters.AddWithValue("rcs", JsonSerializer.Serialize(roleCodes));
            cmd.Parameters.AddWithValue("ph", hash);
            cmd.Parameters.AddWithValue("hasPw", hasNewPassword);
            cmd.Parameters.AddWithValue("apps", JsonSerializer.Serialize(req.AllowedApps ?? new()));
            cmd.Parameters.AddWithValue("perms", JsonSerializer.Serialize(req.Permissions ?? new()));
            cmd.Parameters.AddWithValue("act", req.IsActive);
            await cmd.ExecuteNonQueryAsync();
        }

        SysUserDto user;
        await using (var fetch = new NpgsqlCommand($"""
            SELECT {UserSelectColumns}
            FROM sys_users WHERE id = @id LIMIT 1
            """, conn))
        {
            fetch.Parameters.AddWithValue("id", id);
            await using var r = await fetch.ExecuteReaderAsync();
            await r.ReadAsync();
            user = ReadUser(r);
        }
        await EnrichUsersWithRolesAsync(conn, new List<SysUserDto> { user });
        return user;
    }

    private static async Task EnrichUsersWithRolesAsync(NpgsqlConnection conn, List<SysUserDto> users)
    {
        if (users.Count == 0) return;

        var roles = await LoadRolesInternalAsync(conn);
        var roleByCode = roles.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);

        var activeAppCodes = new List<string>();
        await using (var appCmd = new NpgsqlCommand("SELECT code FROM sys_apps WHERE is_active = TRUE ORDER BY order_index ASC", conn))
        await using (var ar = await appCmd.ExecuteReaderAsync())
        {
            while (await ar.ReadAsync()) activeAppCodes.Add(ar.GetString(0));
        }

        // Load per-table role permission overrides
        var tableRulesByRole = new Dictionary<string, List<(string TableId, EffectiveTablePermissionDto Rule)>>(StringComparer.OrdinalIgnoreCase);
        await using (var rtpCmd = new NpgsqlCommand("""
            SELECT role_id, table_id, can_view, can_create, can_edit, can_delete, can_export, can_approve, COALESCE(row_scope, 'INHERIT')
            FROM sys_role_table_permissions
            """, conn))
        await using (var rr = await rtpCmd.ExecuteReaderAsync())
        {
            while (await rr.ReadAsync())
            {
                var rid = rr.GetString(0);
                if (!tableRulesByRole.TryGetValue(rid, out var list))
                {
                    list = new List<(string, EffectiveTablePermissionDto)>();
                    tableRulesByRole[rid] = list;
                }
                list.Add((rr.GetString(1), new EffectiveTablePermissionDto
                {
                    CanView = rr.GetBoolean(2),
                    CanCreate = rr.GetBoolean(3),
                    CanEdit = rr.GetBoolean(4),
                    CanDelete = rr.GetBoolean(5),
                    CanExport = rr.GetBoolean(6),
                    CanApprove = rr.GetBoolean(7),
                    RowScope = rr.GetString(8)
                }));
            }
        }

        foreach (var u in users)
        {
            if (u.RoleCodes.Count == 0 && !string.IsNullOrWhiteSpace(u.RoleCode))
            {
                u.RoleCodes = new List<string> { u.RoleCode.Trim().ToUpperInvariant() };
            }
            u.RoleCode = u.RoleCodes.FirstOrDefault() ?? "STAFF";

            var matchedRoles = u.RoleCodes
                .Select(c => roleByCode.TryGetValue(c, out var role) ? role : null)
                .Where(r => r != null && r.IsActive)
                .Cast<SysRoleDto>()
                .ToList();

            if (matchedRoles.Count == 0)
            {
                u.IsAdmin = u.RoleCode.Equals("ADMIN", StringComparison.OrdinalIgnoreCase);
                u.RoleNames = new List<string> { u.RoleCode };
                continue;
            }

            u.RoleNames = matchedRoles.Select(r => r.Name).ToList();
            u.IsAdmin = matchedRoles.Any(r => r.Code.Equals("ADMIN", StringComparison.OrdinalIgnoreCase));

            var sysPerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allowedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var appPermsMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var appScopes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allFlatPerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tablePermsMap = new Dictionary<string, EffectiveTablePermissionDto>(StringComparer.OrdinalIgnoreCase);

            foreach (var role in matchedRoles)
            {
                foreach (var sp in role.SystemPermissions)
                {
                    sysPerms.Add(sp);
                    allFlatPerms.Add(sp);
                }

                if (role.AllAppsAccess)
                {
                    allowedApps.Add("*");
                    var defPerms = role.DefaultAppPermissions.Count > 0
                        ? role.DefaultAppPermissions
                        : (u.IsAdmin ? GetPermissionCatalog().AppPermissions.Select(x => x.Code).ToList() : new List<string>());
                    foreach (var ac in activeAppCodes)
                    {
                        if (!appPermsMap.TryGetValue(ac, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            appPermsMap[ac] = set;
                        }
                        foreach (var dp in defPerms)
                        {
                            set.Add(dp);
                            allFlatPerms.Add(dp);
                        }
                    }
                }

                foreach (var rap in role.AppPermissions)
                {
                    if (rap.Permissions.Count == 0) continue;
                    allowedApps.Add(rap.AppCode);
                    if (!appPermsMap.TryGetValue(rap.AppCode, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        appPermsMap[rap.AppCode] = set;
                    }
                    foreach (var p in rap.Permissions)
                    {
                        set.Add(p);
                        allFlatPerms.Add(p);
                    }

                    var nextScope = string.IsNullOrWhiteSpace(rap.RowScope) ? "INHERIT" : rap.RowScope.ToUpperInvariant();
                    if (!appScopes.TryGetValue(rap.AppCode, out var curScope) || GetRowScopeRank(nextScope) > GetRowScopeRank(curScope))
                    {
                        appScopes[rap.AppCode] = nextScope;
                    }
                }

                if (tableRulesByRole.TryGetValue(role.Id, out var rtpList))
                {
                    foreach (var (tid, rule) in rtpList)
                    {
                        if (!tablePermsMap.TryGetValue(tid, out var existing))
                        {
                            tablePermsMap[tid] = new EffectiveTablePermissionDto
                            {
                                CanView = rule.CanView,
                                CanCreate = rule.CanCreate,
                                CanEdit = rule.CanEdit,
                                CanDelete = rule.CanDelete,
                                CanExport = rule.CanExport,
                                CanApprove = rule.CanApprove,
                                RowScope = rule.RowScope
                            };
                        }
                        else
                        {
                            existing.CanView |= rule.CanView;
                            existing.CanCreate |= rule.CanCreate;
                            existing.CanEdit |= rule.CanEdit;
                            existing.CanDelete |= rule.CanDelete;
                            existing.CanExport |= rule.CanExport;
                            existing.CanApprove |= rule.CanApprove;
                            if (GetRowScopeRank(rule.RowScope) > GetRowScopeRank(existing.RowScope))
                            {
                                existing.RowScope = rule.RowScope;
                            }
                        }
                    }
                }
            }

            if (u.IsAdmin)
            {
                foreach (var item in GetPermissionCatalog().SystemPermissions)
                {
                    sysPerms.Add(item.Code);
                    allFlatPerms.Add(item.Code);
                }
                allowedApps.Add("*");
            }

            u.SystemPermissions = sysPerms.OrderBy(x => x).ToList();
            u.AllowedApps = allowedApps.Contains("*")
                ? new List<string> { "*" }
                : allowedApps.OrderBy(x => x).ToList();
            u.AppPermissions = appPermsMap.ToDictionary(
                kv => kv.Key.ToUpperInvariant(),
                kv => kv.Value.OrderBy(x => x).ToList(),
                StringComparer.OrdinalIgnoreCase);
            u.AppRowScopes = appScopes;
            u.TablePermissions = tablePermsMap;
            u.Permissions = allFlatPerms.OrderBy(x => x).ToList();
        }
    }

    private static SysUserDto ReadUser(NpgsqlDataReader r)
    {
        var primaryRole = r.GetString(6).Trim().ToUpperInvariant();
        var roleCodes = JsonSerializer.Deserialize<List<string>>(r.GetString(7)) ?? new List<string>();
        roleCodes = roleCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roleCodes.Count == 0 && !string.IsNullOrWhiteSpace(primaryRole))
        {
            roleCodes.Add(primaryRole);
        }

        return new SysUserDto
        {
            Id = r.GetString(0),
            Username = r.GetString(1),
            FullName = r.GetString(2),
            Email = r.IsDBNull(3) ? null : r.GetString(3),
            EmployeeCode = r.IsDBNull(4) ? null : r.GetString(4),
            Department = r.IsDBNull(5) ? "Toàn công ty" : r.GetString(5),
            RoleCode = roleCodes.FirstOrDefault() ?? primaryRole,
            RoleCodes = roleCodes,
            AllowedApps = JsonSerializer.Deserialize<List<string>>(r.GetString(9)) ?? new(),
            Permissions = JsonSerializer.Deserialize<List<string>>(r.GetString(10)) ?? new(),
            IsActive = r.GetBoolean(11),
            SsoProvider = r.FieldCount > 12 && !r.IsDBNull(12) ? r.GetString(12) : null,
            SsoSubject = r.FieldCount > 13 && !r.IsDBNull(13) ? r.GetString(13) : null
        };
    }
}
