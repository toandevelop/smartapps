using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Npgsql;
using SmartApps.Api.Models;

namespace SmartApps.Api.Services;

public class SsoService
{
    private readonly string _connStr;
    private readonly IHttpClientFactory _httpFactory;
    private readonly AuthService _auth;

    private sealed record PendingSsoState(
        string ProviderCode,
        string RedirectUri,
        string CodeVerifier,
        string Nonce,
        string ReturnUrl,
        DateTimeOffset ExpiresAt
    );

    private readonly ConcurrentDictionary<string, (string Json, DateTimeOffset FetchedAt)> _jwksCache = new(StringComparer.OrdinalIgnoreCase);

    public SsoService(IConfiguration config, IHttpClientFactory httpFactory, AuthService auth)
    {
        _connStr = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
        _httpFactory = httpFactory;
        _auth = auth;
    }

    private sealed class SsoProviderInternal : SsoProviderAdminDto
    {
        public string? ClientSecret { get; set; }
    }

    public async Task<List<SsoProviderPublicDto>> ListPublicProvidersAsync()
    {
        var list = await LoadProvidersAsync();
        return list
            .Where(p => p.IsEnabled && p.IsConfigured)
            .Select(p => new SsoProviderPublicDto
            {
                Id = p.Id,
                Code = p.Code,
                ProviderType = p.ProviderType,
                DisplayName = p.DisplayName,
                IsEnabled = p.IsEnabled,
                IsConfigured = p.IsConfigured,
                ButtonColor = p.ButtonColor,
                OrderIndex = p.OrderIndex
            })
            .ToList();
    }

    public async Task<List<SsoProviderAdminDto>> ListAdminProvidersAsync()
    {
        var list = await LoadProvidersAsync();
        return list.Cast<SsoProviderAdminDto>().ToList();
    }

    public async Task<(SsoProviderAdminDto? Provider, string? Error)> UpsertProviderAsync(UpsertSsoProviderRequest req)
    {
        var code = req.Code.Trim().ToUpperInvariant().Replace(" ", "_");
        if (string.IsNullOrWhiteSpace(code))
            return (null, "Mã nhà cung cấp SSO không được để trống.");
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return (null, "Tên hiển thị SSO là bắt buộc.");

        var providerType = string.IsNullOrWhiteSpace(req.ProviderType)
            ? "CUSTOM_OIDC"
            : req.ProviderType.Trim().ToUpperInvariant();

        var id = string.IsNullOrWhiteSpace(req.Id) ? $"sso_{code.ToLowerInvariant()}" : req.Id.Trim();
        var tenant = string.IsNullOrWhiteSpace(req.TenantId) ? "common" : req.TenantId.Trim();

        var issuerUrl = req.IssuerUrl?.Trim();
        var authUrl = req.AuthorizationUrl?.Trim();
        var tokenUrl = req.TokenUrl?.Trim();
        var userinfoUrl = req.UserinfoUrl?.Trim();

        if (providerType == "GOOGLE")
        {
            issuerUrl = string.IsNullOrWhiteSpace(issuerUrl) ? "https://accounts.google.com" : issuerUrl;
            authUrl = string.IsNullOrWhiteSpace(authUrl) ? "https://accounts.google.com/o/oauth2/v2/auth" : authUrl;
            tokenUrl = string.IsNullOrWhiteSpace(tokenUrl) ? "https://oauth2.googleapis.com/token" : tokenUrl;
            userinfoUrl = string.IsNullOrWhiteSpace(userinfoUrl) ? "https://openidconnect.googleapis.com/v1/userinfo" : userinfoUrl;
        }
        else if (providerType == "MICROSOFT")
        {
            var tEnc = Uri.EscapeDataString(tenant);
            issuerUrl = $"https://login.microsoftonline.com/{tEnc}/v2.0";
            authUrl = $"https://login.microsoftonline.com/{tEnc}/oauth2/v2.0/authorize";
            tokenUrl = $"https://login.microsoftonline.com/{tEnc}/oauth2/v2.0/token";
            userinfoUrl = string.IsNullOrWhiteSpace(userinfoUrl) ? "https://graph.microsoft.com/oidc/userinfo" : userinfoUrl;
        }

        var domains = (req.AllowedDomains ?? new List<string>())
            .Select(d => d.Trim().TrimStart('@').ToLowerInvariant())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scopes = string.IsNullOrWhiteSpace(req.Scopes) ? "openid email profile" : req.Scopes.Trim();
        var defaultRole = string.IsNullOrWhiteSpace(req.DefaultRoleCode) ? "STAFF" : req.DefaultRoleCode.Trim().ToUpperInvariant();
        var btnColor = string.IsNullOrWhiteSpace(req.ButtonColor) ? "#1a73e8" : req.ButtonColor.Trim();
        var hasNewSecret = !string.IsNullOrWhiteSpace(req.ClientSecret);

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var sql = """
            INSERT INTO sys_sso_providers (
                id, code, provider_type, display_name, is_enabled, client_id, client_secret,
                tenant_id, issuer_url, authorization_url, token_url, userinfo_url, scopes,
                allowed_domains, auto_provision, default_role_code, button_color, order_index, updated_at
            )
            VALUES (
                @id, @c, @pt, @dn, @en, @cid, @csec,
                @tid, @iss, @auth, @tok, @ui, @sc,
                @dom::jsonb, @auto, @drc, @col, @ord, CURRENT_TIMESTAMP
            )
            ON CONFLICT (id) DO UPDATE SET
                display_name = EXCLUDED.display_name,
                provider_type = EXCLUDED.provider_type,
                is_enabled = EXCLUDED.is_enabled,
                client_id = EXCLUDED.client_id,
                client_secret = CASE WHEN @hasSec THEN EXCLUDED.client_secret ELSE sys_sso_providers.client_secret END,
                tenant_id = EXCLUDED.tenant_id,
                issuer_url = EXCLUDED.issuer_url,
                authorization_url = EXCLUDED.authorization_url,
                token_url = EXCLUDED.token_url,
                userinfo_url = EXCLUDED.userinfo_url,
                scopes = EXCLUDED.scopes,
                allowed_domains = EXCLUDED.allowed_domains,
                auto_provision = EXCLUDED.auto_provision,
                default_role_code = EXCLUDED.default_role_code,
                button_color = EXCLUDED.button_color,
                order_index = EXCLUDED.order_index,
                updated_at = CURRENT_TIMESTAMP
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("c", code);
        cmd.Parameters.AddWithValue("pt", providerType);
        cmd.Parameters.AddWithValue("dn", req.DisplayName.Trim());
        cmd.Parameters.AddWithValue("en", req.IsEnabled);
        cmd.Parameters.AddWithValue("cid", string.IsNullOrWhiteSpace(req.ClientId) ? DBNull.Value : req.ClientId.Trim());
        cmd.Parameters.AddWithValue("csec", hasNewSecret ? req.ClientSecret!.Trim() : DBNull.Value);
        cmd.Parameters.AddWithValue("hasSec", hasNewSecret);
        cmd.Parameters.AddWithValue("tid", providerType == "MICROSOFT" ? tenant : (object?)req.TenantId?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("iss", (object?)issuerUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("auth", (object?)authUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tok", (object?)tokenUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ui", (object?)userinfoUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("sc", scopes);
        cmd.Parameters.AddWithValue("dom", JsonSerializer.Serialize(domains));
        cmd.Parameters.AddWithValue("auto", req.AutoProvision);
        cmd.Parameters.AddWithValue("drc", defaultRole);
        cmd.Parameters.AddWithValue("col", btnColor);
        cmd.Parameters.AddWithValue("ord", req.OrderIndex);
        await cmd.ExecuteNonQueryAsync();

        var saved = (await LoadProvidersAsync()).FirstOrDefault(p => p.Id == id || p.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        return (saved, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteProviderAsync(string id)
    {
        if (id is "sso_google" or "sso_microsoft" or "sso_custom")
            return (false, "Không thể xóa nhà cung cấp SSO mặc định. Bạn có thể tắt kích hoạt nếu không sử dụng.");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM sys_sso_providers WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0
            ? (true, null)
            : (false, "Không tìm thấy nhà cung cấp SSO.");
    }

    public async Task<(OidcDiscoveryResultDto? Result, string? Error)> DiscoverOidcAsync(string issuerUrl)
    {
        if (string.IsNullOrWhiteSpace(issuerUrl))
            return (null, "Vui lòng nhập Issuer URL (ví dụ: https://sso.company.vn/realms/corp).");

        var baseUrl = issuerUrl.Trim().TrimEnd('/');
        var wellKnown = baseUrl.EndsWith("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/.well-known/openid-configuration";

        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var resp = await client.GetAsync(wellKnown);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (null, $"Không thể tải OIDC Discovery ({(int)resp.StatusCode}) từ {wellKnown}.");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var auth = root.TryGetProperty("authorization_endpoint", out var ae) ? ae.GetString() : null;
            var tok = root.TryGetProperty("token_endpoint", out var te) ? te.GetString() : null;
            var ui = root.TryGetProperty("userinfo_endpoint", out var ue) ? ue.GetString() : null;
            var iss = root.TryGetProperty("issuer", out var ie) ? ie.GetString() : baseUrl;
            var jwks = root.TryGetProperty("jwks_uri", out var je) ? je.GetString() : null;

            if (string.IsNullOrWhiteSpace(auth) || string.IsNullOrWhiteSpace(tok))
                return (null, "Metadata OIDC thiếu authorization_endpoint hoặc token_endpoint.");

            return (new OidcDiscoveryResultDto
            {
                Issuer = iss ?? baseUrl,
                AuthorizationUrl = auth!,
                TokenUrl = tok!,
                UserinfoUrl = ui,
                JwksUri = jwks
            }, null);
        }
        catch (Exception ex)
        {
            return (null, $"Lỗi kết nối OIDC Discovery: {ex.Message}");
        }
    }

    public async Task<(string? AuthorizeUrl, string? Error)> BeginAuthorizationAsync(
        string code,
        string redirectUri,
        string? returnUrl)
    {
        var provider = (await LoadProvidersAsync()).FirstOrDefault(p => p.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
        if (provider == null)
            return (null, $"Không tìm thấy phương thức đăng nhập SSO '{code}'.");
        if (!provider.IsEnabled)
            return (null, $"Phương thức '{provider.DisplayName}' chưa được bật. Quản trị viên cần bật tại Quản trị → Đăng nhập SSO.");
        if (string.IsNullOrWhiteSpace(provider.ClientId))
            return (null, $"Phương thức '{provider.DisplayName}' chưa được cấu hình Client ID.");

        var authEndpoint = provider.AuthorizationUrl;
        var tokenEndpoint = provider.TokenUrl;
        if ((string.IsNullOrWhiteSpace(authEndpoint) || string.IsNullOrWhiteSpace(tokenEndpoint))
            && !string.IsNullOrWhiteSpace(provider.IssuerUrl))
        {
            var (disc, err) = await DiscoverOidcAsync(provider.IssuerUrl);
            if (disc == null) return (null, err ?? "Không dò được OIDC endpoints.");
            authEndpoint = disc.AuthorizationUrl;
            tokenEndpoint = disc.TokenUrl;
        }

        if (string.IsNullOrWhiteSpace(authEndpoint) || string.IsNullOrWhiteSpace(tokenEndpoint))
            return (null, $"Phương thức '{provider.DisplayName}' thiếu Authorization URL hoặc Token URL.");

        var (verifier, challenge) = GeneratePkcePair();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var safeReturn = !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') ? returnUrl : "/";

        await SavePendingStateAsync(state, new PendingSsoState(
            provider.Code,
            redirectUri,
            verifier,
            nonce,
            safeReturn,
            DateTimeOffset.UtcNow.AddMinutes(10)));

        var url = BuildAuthorizeUrl(
            authEndpoint!,
            provider.ClientId!,
            redirectUri,
            provider.Scopes,
            state,
            challenge,
            nonce,
            provider.ProviderType);
        return (url, null);
    }

    public async Task<(SysUserDto? User, string? Token, string ReturnUrl, string? Error)> CompleteCallbackAsync(
        string code,
        string? authCode,
        string? state,
        string? oauthError,
        string? oauthErrorDesc)
    {
        if (!string.IsNullOrWhiteSpace(oauthError))
        {
            return (null, null, "/", $"Nhà cung cấp SSO từ chối đăng nhập: {oauthErrorDesc ?? oauthError}");
        }
        var pending = string.IsNullOrWhiteSpace(state) ? null : await ConsumePendingStateAsync(state);
        if (pending == null)
        {
            return (null, null, "/", "Phiên xác thực SSO đã hết hạn hoặc state không hợp lệ. Vui lòng thử lại.");
        }
        if (pending.ExpiresAt < DateTimeOffset.UtcNow || !pending.ProviderCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (null, null, "/", "Phiên xác thực SSO không khớp hoặc đã quá hạn.");
        }
        if (string.IsNullOrWhiteSpace(authCode))
        {
            return (null, null, pending.ReturnUrl, "Không nhận được mã xác thực (authorization code) từ nhà cung cấp SSO.");
        }

        var provider = (await LoadProvidersAsync()).FirstOrDefault(p => p.Code.Equals(pending.ProviderCode, StringComparison.OrdinalIgnoreCase));
        if (provider == null || !provider.IsEnabled || string.IsNullOrWhiteSpace(provider.ClientId))
        {
            return (null, null, pending.ReturnUrl, "Cấu hình SSO đã thay đổi hoặc bị tắt.");
        }

        var tokenEndpoint = provider.TokenUrl;
        var userinfoEndpoint = provider.UserinfoUrl;
        string? expectedIssuer = provider.IssuerUrl;
        string? jwksUri = null;
        if (!string.IsNullOrWhiteSpace(provider.IssuerUrl))
        {
            var (disc, _) = await DiscoverOidcAsync(provider.IssuerUrl);
            if (disc != null)
            {
                tokenEndpoint ??= disc.TokenUrl;
                userinfoEndpoint ??= disc.UserinfoUrl;
                expectedIssuer = string.IsNullOrWhiteSpace(disc.Issuer) ? expectedIssuer : disc.Issuer;
                jwksUri = disc.JwksUri;
            }
        }
        if (string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            return (null, null, pending.ReturnUrl, "Thiếu Token Endpoint cho nhà cung cấp SSO.");
        }

        // Exchange code -> tokens
        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode,
            ["redirect_uri"] = pending.RedirectUri,
            ["client_id"] = provider.ClientId!,
            ["code_verifier"] = pending.CodeVerifier
        };
        if (!string.IsNullOrWhiteSpace(provider.ClientSecret))
        {
            form["client_secret"] = provider.ClientSecret!;
        }

        using var tokenResp = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form));
        var tokenRaw = await tokenResp.Content.ReadAsStringAsync();
        if (!tokenResp.IsSuccessStatusCode)
        {
            return (null, null, pending.ReturnUrl, $"Đổi SSO Token thất bại ({(int)tokenResp.StatusCode}). Kiểm tra Client Secret và Redirect URI.");
        }

        string? accessToken = null;
        string? idToken = null;
        using (var tokDoc = JsonDocument.Parse(tokenRaw))
        {
            if (tokDoc.RootElement.TryGetProperty("access_token", out var at)) accessToken = at.GetString();
            if (tokDoc.RootElement.TryGetProperty("id_token", out var it)) idToken = it.GetString();
        }

        Dictionary<string, string> claims;
        if (!string.IsNullOrWhiteSpace(idToken))
        {
            string? jwksJson = null;
            if (!string.IsNullOrWhiteSpace(jwksUri))
            {
                jwksJson = await GetJwksJsonAsync(client, jwksUri, forceRefresh: false);
                var (ok, verifiedClaims, verifyErr) = VerifyJwtWithJwkJson(
                    idToken,
                    jwksJson ?? "",
                    expectedIssuer,
                    provider.ClientId,
                    pending.Nonce);
                if (!ok && verifyErr != null && verifyErr.Contains("kid", StringComparison.OrdinalIgnoreCase))
                {
                    // Refresh JWKS once on key rotation miss
                    jwksJson = await GetJwksJsonAsync(client, jwksUri, forceRefresh: true);
                    (ok, verifiedClaims, verifyErr) = VerifyJwtWithJwkJson(
                        idToken,
                        jwksJson ?? "",
                        expectedIssuer,
                        provider.ClientId,
                        pending.Nonce);
                }
                if (!ok)
                {
                    return (null, null, pending.ReturnUrl, verifyErr ?? "Xác thực chữ ký OIDC id_token thất bại.");
                }
                claims = verifiedClaims;
            }
            else
            {
                // Still validate non-cryptographic standard claims if OIDC JWKS URI was not configured
                var (claimsOk, validatedClaims, claimErr) = ValidateJwtClaimsOnly(
                    idToken,
                    expectedIssuer,
                    provider.ClientId,
                    pending.Nonce);
                if (!claimsOk)
                {
                    return (null, null, pending.ReturnUrl, claimErr ?? "Claims trên id_token không hợp lệ.");
                }
                claims = validatedClaims;
            }
        }
        else
        {
            claims = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // Enrich with UserInfo endpoint if available
        if (!string.IsNullOrWhiteSpace(userinfoEndpoint) && !string.IsNullOrWhiteSpace(accessToken))
        {
            try
            {
                using var uiReq = new HttpRequestMessage(HttpMethod.Get, userinfoEndpoint);
                uiReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var uiResp = await client.SendAsync(uiReq);
                if (uiResp.IsSuccessStatusCode)
                {
                    var uiRaw = await uiResp.Content.ReadAsStringAsync();
                    using var uiDoc = JsonDocument.Parse(uiRaw);
                    foreach (var prop in uiDoc.RootElement.EnumerateObject())
                    {
                        var val = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(val) && !claims.ContainsKey(prop.Name))
                        {
                            claims[prop.Name] = val;
                        }
                    }
                }
            }
            catch
            {
                // Non-fatal if id_token already contains required claims
            }
        }

        var subject = FirstNonEmpty(claims, "sub", "id", "oid", "user_id");
        var email = FirstNonEmpty(claims, "email", "mail", "upn", "preferred_username");
        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@')) email = null;
        var fullName = FirstNonEmpty(claims, "name", "displayName", "given_name");
        var preferredUsername = FirstNonEmpty(claims, "preferred_username", "nickname");

        if (string.IsNullOrWhiteSpace(subject))
        {
            return (null, null, pending.ReturnUrl, "Nhà cung cấp SSO không trả về mã định danh người dùng (sub).");
        }

        if (!IsDomainAllowed(email, provider.AllowedDomains))
        {
            return (null, null, pending.ReturnUrl, $"Email '{email ?? "(trống)"}' không thuộc tên miền công ty được phép đăng nhập.");
        }

        var (user, jwt, err) = await _auth.ResolveOrProvisionSsoUserAsync(
            provider.Code,
            subject,
            email,
            fullName,
            preferredUsername,
            provider.AutoProvision,
            provider.DefaultRoleCode);

        return (user, jwt, pending.ReturnUrl, err);
    }

    public static (string CodeVerifier, string CodeChallenge) GeneratePkcePair()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(bytes);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(hash);
        return (verifier, challenge);
    }

    public static string BuildAuthorizeUrl(
        string authorizationEndpoint,
        string clientId,
        string redirectUri,
        string scopes,
        string state,
        string codeChallenge,
        string nonce,
        string providerType)
    {
        var uriBuilder = new UriBuilder(authorizationEndpoint);
        var q = HttpUtility.ParseQueryString(uriBuilder.Query);
        q["client_id"] = clientId;
        q["redirect_uri"] = redirectUri;
        q["response_type"] = "code";
        q["scope"] = string.IsNullOrWhiteSpace(scopes) ? "openid email profile" : scopes;
        q["state"] = state;
        q["nonce"] = nonce;
        q["code_challenge"] = codeChallenge;
        q["code_challenge_method"] = "S256";
        if (providerType.Equals("GOOGLE", StringComparison.OrdinalIgnoreCase))
        {
            q["prompt"] = "select_account";
            q["access_type"] = "online";
        }
        uriBuilder.Query = q.ToString() ?? string.Empty;
        return uriBuilder.ToString();
    }

    public static Dictionary<string, string> ParseJwtPayloadClaims(string? jwt)
    {
        var claims = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(jwt)) return claims;
        var parts = jwt.Split('.');
        if (parts.Length < 2) return claims;

        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return claims;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var val = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                if (!string.IsNullOrWhiteSpace(val))
                    claims[prop.Name] = val;
            }
        }
        catch
        {
            // Ignore malformed JWT payload
        }
        return claims;
    }

    public static bool IsDomainAllowed(string? email, IReadOnlyCollection<string> allowedDomains)
    {
        if (allowedDomains == null || allowedDomains.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(email)) return false;
        var atIdx = email.IndexOf('@');
        if (atIdx < 0 || atIdx == email.Length - 1) return false;
        var domain = email[(atIdx + 1)..].Trim().ToLowerInvariant();
        return allowedDomains.Any(d => domain.Equals(d.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase));
    }

    private static string? FirstNonEmpty(Dictionary<string, string> claims, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (claims.TryGetValue(k, out var val) && !string.IsNullOrWhiteSpace(val))
                return val.Trim();
        }
        return null;
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private async Task<string?> GetJwksJsonAsync(HttpClient client, string jwksUri, bool forceRefresh)
    {
        var hasCached = _jwksCache.TryGetValue(jwksUri, out var cached);
        if (!forceRefresh && hasCached && cached.FetchedAt > DateTimeOffset.UtcNow.AddMinutes(-15))
        {
            return cached.Json;
        }
        try
        {
            var resp = await client.GetAsync(jwksUri);
            if (!resp.IsSuccessStatusCode) return hasCached ? cached.Json : null;
            var json = await resp.Content.ReadAsStringAsync();
            _jwksCache[jwksUri] = (json, DateTimeOffset.UtcNow);
            return json;
        }
        catch
        {
            return hasCached ? cached.Json : null;
        }
    }

    public static (bool Ok, Dictionary<string, string> Claims, string? Error) ValidateJwtClaimsOnly(
        string? jwt,
        string? expectedIssuer,
        string? expectedAudience,
        string? expectedNonce,
        DateTimeOffset? utcNow = null)
    {
        var empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(jwt))
            return (false, empty, "Thiếu JWT id_token.");
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return (false, empty, "Định dạng JWT id_token không hợp lệ.");

        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (false, empty, "Payload id_token không phải JSON object.");

            var nowUnix = (utcNow ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            var root = doc.RootElement;

            if (root.TryGetProperty("exp", out var expEl) && TryGetUnixSeconds(expEl, out var exp) && nowUnix > exp + 120)
                return (false, empty, "SSO id_token đã hết hạn (exp).");

            if (root.TryGetProperty("nbf", out var nbfEl) && TryGetUnixSeconds(nbfEl, out var nbf) && nowUnix < nbf - 120)
                return (false, empty, "SSO id_token chưa đến thời điểm có hiệu lực (nbf).");

            if (!string.IsNullOrWhiteSpace(expectedIssuer))
            {
                var actualIss = root.TryGetProperty("iss", out var issEl) ? issEl.GetString()?.TrimEnd('/') : null;
                var normExpectedIss = expectedIssuer.Trim().TrimEnd('/');
                if (string.IsNullOrWhiteSpace(actualIss) || !string.Equals(actualIss, normExpectedIss, StringComparison.OrdinalIgnoreCase))
                    return (false, empty, $"Issuer (iss) không khớp: nhận '{actualIss ?? "(trống)"}'.");
            }

            if (!string.IsNullOrWhiteSpace(expectedAudience))
            {
                var matchedAud = false;
                if (root.TryGetProperty("aud", out var audEl))
                {
                    if (audEl.ValueKind == JsonValueKind.String)
                        matchedAud = string.Equals(audEl.GetString(), expectedAudience.Trim(), StringComparison.Ordinal);
                    else if (audEl.ValueKind == JsonValueKind.Array)
                        matchedAud = audEl.EnumerateArray().Any(e => string.Equals(e.GetString(), expectedAudience.Trim(), StringComparison.Ordinal));
                }
                if (!matchedAud)
                    return (false, empty, "Audience (aud) của id_token không khớp Client ID.");
            }

            if (!string.IsNullOrWhiteSpace(expectedNonce))
            {
                var actualNonce = root.TryGetProperty("nonce", out var nEl) ? nEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(actualNonce) || !string.Equals(actualNonce, expectedNonce, StringComparison.Ordinal))
                    return (false, empty, "Nonce bảo mật SSO không khớp.");
            }

            var claims = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in root.EnumerateObject())
            {
                var val = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                if (!string.IsNullOrWhiteSpace(val)) claims[prop.Name] = val;
            }
            return (true, claims, null);
        }
        catch (Exception ex)
        {
            return (false, empty, $"Không thể phân tích payload id_token: {ex.Message}");
        }
    }

    public static (bool Ok, Dictionary<string, string> Claims, string? Error) VerifyJwtWithJwkJson(
        string? jwt,
        string jwksJson,
        string? expectedIssuer,
        string? expectedAudience,
        string? expectedNonce,
        DateTimeOffset? utcNow = null)
    {
        var empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(jwt))
            return (false, empty, "Thiếu id_token.");
        var parts = jwt.Split('.');
        if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[2]))
            return (false, empty, "id_token phải gồm đủ header.payload.signature.");
        if (string.IsNullOrWhiteSpace(jwksJson))
            return (false, empty, "Không tải được khóa công khai JWKS từ nhà cung cấp SSO.");

        string? alg;
        string? kid;
        try
        {
            var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            using var hDoc = JsonDocument.Parse(headerJson);
            alg = hDoc.RootElement.TryGetProperty("alg", out var aEl) ? aEl.GetString() : null;
            kid = hDoc.RootElement.TryGetProperty("kid", out var kEl) ? kEl.GetString() : null;
        }
        catch
        {
            return (false, empty, "Header JWT id_token không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(alg) || alg.Equals("none", StringComparison.OrdinalIgnoreCase))
            return (false, empty, "Thuật toán chữ ký JWT 'none' bị từ chối.");

        byte[] sigBytes;
        try
        {
            sigBytes = Base64UrlDecode(parts[2]);
        }
        catch
        {
            return (false, empty, "Chữ ký Base64Url trên id_token không hợp lệ.");
        }
        var signedBytes = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");

        try
        {
            using var jwkDoc = JsonDocument.Parse(jwksJson);
            if (!jwkDoc.RootElement.TryGetProperty("keys", out var keysEl) || keysEl.ValueKind != JsonValueKind.Array)
                return (false, empty, "JWKS không chứa danh sách keys hợp lệ.");

            var keyCandidates = new List<JsonElement>();
            foreach (var k in keysEl.EnumerateArray())
            {
                var kKid = k.TryGetProperty("kid", out var kidEl) ? kidEl.GetString() : null;
                var kUse = k.TryGetProperty("use", out var useEl) ? useEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(kUse) && !kUse.Equals("sig", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrWhiteSpace(kid))
                {
                    if (string.Equals(kKid, kid, StringComparison.Ordinal))
                        keyCandidates.Add(k);
                }
                else
                {
                    keyCandidates.Add(k);
                }
            }

            if (keyCandidates.Count == 0)
                return (false, empty, $"Không tìm thấy JWK phù hợp cho kid '{kid ?? "(none)"}'.");

            var signatureValid = false;
            foreach (var jwk in keyCandidates)
            {
                if (VerifySignatureAgainstJwk(signedBytes, sigBytes, alg, jwk))
                {
                    signatureValid = true;
                    break;
                }
            }

            if (!signatureValid)
                return (false, empty, "Chữ ký số RSA/ECDSA của id_token không khớp JWKS.");

            return ValidateJwtClaimsOnly(jwt, expectedIssuer, expectedAudience, expectedNonce, utcNow);
        }
        catch (Exception ex)
        {
            return (false, empty, $"Lỗi xác thực JWKS: {ex.Message}");
        }
    }

    private static bool VerifySignatureAgainstJwk(byte[] signedBytes, byte[] sigBytes, string alg, JsonElement jwk)
    {
        var kty = jwk.TryGetProperty("kty", out var ktyEl) ? ktyEl.GetString() : null;
        if (string.Equals(kty, "RSA", StringComparison.OrdinalIgnoreCase) && alg.StartsWith("RS", StringComparison.OrdinalIgnoreCase))
        {
            var hashAlg = alg.ToUpperInvariant() switch
            {
                "RS256" => HashAlgorithmName.SHA256,
                "RS384" => HashAlgorithmName.SHA384,
                "RS512" => HashAlgorithmName.SHA512,
                _ => default
            };
            if (hashAlg == default) return false;
            if (!jwk.TryGetProperty("n", out var nEl) || !jwk.TryGetProperty("e", out var eEl)) return false;
            using var rsa = RSA.Create(new RSAParameters
            {
                Modulus = Base64UrlDecode(nEl.GetString()!),
                Exponent = Base64UrlDecode(eEl.GetString()!)
            });
            return rsa.VerifyData(signedBytes, sigBytes, hashAlg, RSASignaturePadding.Pkcs1);
        }

        if (string.Equals(kty, "EC", StringComparison.OrdinalIgnoreCase) && alg.StartsWith("ES", StringComparison.OrdinalIgnoreCase))
        {
            var (hashAlg, curve, coordBytes) = alg.ToUpperInvariant() switch
            {
                "ES256" => (HashAlgorithmName.SHA256, ECCurve.NamedCurves.nistP256, 32),
                "ES384" => (HashAlgorithmName.SHA384, ECCurve.NamedCurves.nistP384, 48),
                "ES512" => (HashAlgorithmName.SHA512, ECCurve.NamedCurves.nistP521, 66),
                _ => (default(HashAlgorithmName), default(ECCurve), 0)
            };
            if (coordBytes == 0) return false;
            if (!jwk.TryGetProperty("x", out var xEl) || !jwk.TryGetProperty("y", out var yEl)) return false;
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = curve,
                Q = new ECPoint
                {
                    X = PadBigEndian(Base64UrlDecode(xEl.GetString()!), coordBytes),
                    Y = PadBigEndian(Base64UrlDecode(yEl.GetString()!), coordBytes)
                }
            });
            return ecdsa.VerifyData(signedBytes, sigBytes, hashAlg, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        return false;
    }

    private static byte[] PadBigEndian(byte[] bytes, int targetLen)
    {
        if (bytes.Length == targetLen) return bytes;
        var res = new byte[targetLen];
        if (bytes.Length > targetLen)
            Buffer.BlockCopy(bytes, bytes.Length - targetLen, res, 0, targetLen);
        else
            Buffer.BlockCopy(bytes, 0, res, targetLen - bytes.Length, bytes.Length);
        return res;
    }

    private static bool TryGetUnixSeconds(JsonElement el, out long value)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out value)) return true;
        if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out value)) return true;
        value = 0;
        return false;
    }

    private async Task SavePendingStateAsync(string state, PendingSsoState pending)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using (var prune = new NpgsqlCommand("DELETE FROM sys_sso_states WHERE expires_at < CURRENT_TIMESTAMP", conn))
        {
            await prune.ExecuteNonQueryAsync();
        }
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO sys_sso_states (state, provider_code, redirect_uri, code_verifier, nonce, return_url, expires_at)
            VALUES (@st, @pc, @ru, @cv, @nc, @ret, @exp)
            ON CONFLICT (state) DO UPDATE SET
                provider_code = EXCLUDED.provider_code,
                redirect_uri = EXCLUDED.redirect_uri,
                code_verifier = EXCLUDED.code_verifier,
                nonce = EXCLUDED.nonce,
                return_url = EXCLUDED.return_url,
                expires_at = EXCLUDED.expires_at
            """, conn);
        cmd.Parameters.AddWithValue("st", state);
        cmd.Parameters.AddWithValue("pc", pending.ProviderCode);
        cmd.Parameters.AddWithValue("ru", pending.RedirectUri);
        cmd.Parameters.AddWithValue("cv", pending.CodeVerifier);
        cmd.Parameters.AddWithValue("nc", pending.Nonce);
        cmd.Parameters.AddWithValue("ret", pending.ReturnUrl);
        cmd.Parameters.AddWithValue("exp", pending.ExpiresAt.UtcDateTime);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<PendingSsoState?> ConsumePendingStateAsync(string state)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            DELETE FROM sys_sso_states
            WHERE state = @st
            RETURNING provider_code, redirect_uri, code_verifier, nonce, return_url, expires_at
            """, conn);
        cmd.Parameters.AddWithValue("st", state);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new PendingSsoState(
            r.GetString(0),
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.GetString(4),
            new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc)));
    }

    private async Task<List<SsoProviderInternal>> LoadProvidersAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var sql = """
            SELECT id, code, provider_type, display_name, is_enabled, client_id, client_secret,
                   tenant_id, issuer_url, authorization_url, token_url, userinfo_url, scopes,
                   COALESCE(allowed_domains::text, '[]'), auto_provision, default_role_code,
                   COALESCE(button_color, '#1a73e8'), order_index
            FROM sys_sso_providers
            ORDER BY order_index ASC, code ASC
            """;
        var list = new List<SsoProviderInternal>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var clientId = r.IsDBNull(5) ? null : r.GetString(5);
            var clientSecret = r.IsDBNull(6) ? null : r.GetString(6);
            var authUrl = r.IsDBNull(9) ? null : r.GetString(9);
            var issuerUrl = r.IsDBNull(8) ? null : r.GetString(8);

            list.Add(new SsoProviderInternal
            {
                Id = r.GetString(0),
                Code = r.GetString(1),
                ProviderType = r.GetString(2),
                DisplayName = r.GetString(3),
                IsEnabled = r.GetBoolean(4),
                ClientId = clientId,
                ClientSecret = clientSecret,
                HasClientSecret = !string.IsNullOrWhiteSpace(clientSecret),
                TenantId = r.IsDBNull(7) ? null : r.GetString(7),
                IssuerUrl = issuerUrl,
                AuthorizationUrl = authUrl,
                TokenUrl = r.IsDBNull(10) ? null : r.GetString(10),
                UserinfoUrl = r.IsDBNull(11) ? null : r.GetString(11),
                Scopes = r.IsDBNull(12) ? "openid email profile" : r.GetString(12),
                AllowedDomains = JsonSerializer.Deserialize<List<string>>(r.GetString(13)) ?? new(),
                AutoProvision = r.GetBoolean(14),
                DefaultRoleCode = r.GetString(15),
                ButtonColor = r.GetString(16),
                OrderIndex = r.GetInt32(17),
                IsConfigured = !string.IsNullOrWhiteSpace(clientId) && (!string.IsNullOrWhiteSpace(authUrl) || !string.IsNullOrWhiteSpace(issuerUrl))
            });
        }
        return list;
    }
}
