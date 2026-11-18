using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class SsoServiceTests
{
    [Fact]
    public void GeneratePkcePair_ProducesValidS256Challenge()
    {
        var (verifier, challenge) = SsoService.GeneratePkcePair();
        Assert.False(string.IsNullOrWhiteSpace(verifier));
        Assert.False(string.IsNullOrWhiteSpace(challenge));

        var expectedHash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var expectedB64Url = Convert.ToBase64String(expectedHash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expectedB64Url, challenge);
    }

    [Fact]
    public void BuildAuthorizeUrl_IncludesPkceStateAndProviderHints()
    {
        var googleUrl = SsoService.BuildAuthorizeUrl(
            "https://accounts.google.com/o/oauth2/v2/auth",
            "goog-client-123",
            "http://localhost:4100/api/auth/sso/callback/GOOGLE",
            "openid email profile",
            "state-xyz",
            "challenge-abc",
            "nonce-1",
            "GOOGLE");

        Assert.Contains("client_id=goog-client-123", googleUrl);
        Assert.Contains("code_challenge=challenge-abc", googleUrl);
        Assert.Contains("code_challenge_method=S256", googleUrl);
        Assert.Contains("state=state-xyz", googleUrl);
        Assert.Contains("prompt=select_account", googleUrl);

        var msUrl = SsoService.BuildAuthorizeUrl(
            "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
            "ms-client-456",
            "http://localhost:4100/api/auth/sso/callback/MICROSOFT",
            "openid email profile User.Read",
            "state-ms",
            "challenge-ms",
            "nonce-2",
            "MICROSOFT");

        Assert.Contains("client_id=ms-client-456", msUrl);
        Assert.DoesNotContain("prompt=select_account", msUrl);
    }

    [Fact]
    public void ParseJwtPayloadClaims_ExtractsEmailSubAndName()
    {
        var payloadJson = "{\"sub\":\"google-109\",\"email\":\"nv.an@company.vn\",\"name\":\"Nguyen Van An\"}";
        var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var fakeJwt = $"eyJhbGciOiJSUzI1NiJ9.{payloadB64}.sig";

        var claims = SsoService.ParseJwtPayloadClaims(fakeJwt);
        Assert.Equal("google-109", claims["sub"]);
        Assert.Equal("nv.an@company.vn", claims["email"]);
        Assert.Equal("Nguyen Van An", claims["name"]);
    }

    [Fact]
    public void IsDomainAllowed_EnforcesCompanyDomainWhitelist()
    {
        var allowed = new[] { "company.vn", "corp.example.com" };
        Assert.True(SsoService.IsDomainAllowed("minh@company.vn", allowed));
        Assert.True(SsoService.IsDomainAllowed("User@CORP.EXAMPLE.COM", allowed));
        Assert.False(SsoService.IsDomainAllowed("outsider@gmail.com", allowed));
        Assert.False(SsoService.IsDomainAllowed(null, allowed));
        Assert.True(SsoService.IsDomainAllowed("anyone@gmail.com", Array.Empty<string>()));
    }

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void VerifyJwtWithJwkJson_ValidatesRsaSignatureAndRejectsTamperedOrInvalidClaims()
    {
        using var rsa = RSA.Create(2048);
        var pub = rsa.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "RSA", kid = "rsa-k1", use = "sig", alg = "RS256", n = B64Url(pub.Modulus!), e = B64Url(pub.Exponent!) }
            }
        });

        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var headerB64 = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\",\"kid\":\"rsa-k1\"}"));
        var payloadB64 = B64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            sub = "user-42",
            email = "an@company.vn",
            iss = "https://sso.company.vn",
            aud = "smartapps-web",
            nonce = "nc-123",
            exp = now.AddMinutes(10).ToUnixTimeSeconds()
        })));
        var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");
        var sigB64 = B64Url(rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var jwt = $"{headerB64}.{payloadB64}.{sigB64}";

        var (ok, claims, err) = SsoService.VerifyJwtWithJwkJson(jwt, jwks, "https://sso.company.vn", "smartapps-web", "nc-123", now);
        Assert.True(ok, err);
        Assert.Equal("user-42", claims["sub"]);

        // Tamper payload claim
        var tamperedPayloadB64 = B64Url(Encoding.UTF8.GetBytes("{\"sub\":\"admin\",\"iss\":\"https://sso.company.vn\",\"aud\":\"smartapps-web\",\"nonce\":\"nc-123\",\"exp\":9999999999}"));
        var (okTamper, _, _) = SsoService.VerifyJwtWithJwkJson($"{headerB64}.{tamperedPayloadB64}.{sigB64}", jwks, "https://sso.company.vn", "smartapps-web", "nc-123", now);
        Assert.False(okTamper);

        // Wrong audience / nonce / expired checks
        Assert.False(SsoService.VerifyJwtWithJwkJson(jwt, jwks, "https://sso.company.vn", "other-client", "nc-123", now).Ok);
        Assert.False(SsoService.VerifyJwtWithJwkJson(jwt, jwks, "https://sso.company.vn", "smartapps-web", "wrong-nonce", now).Ok);
        Assert.False(SsoService.VerifyJwtWithJwkJson(jwt, jwks, "https://sso.company.vn", "smartapps-web", "nc-123", now.AddHours(1)).Ok);
    }

    [Fact]
    public void VerifyJwtWithJwkJson_ValidatesEcdsaEs256Signature()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdsa.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "EC", kid = "ec-1", use = "sig", crv = "P-256", x = B64Url(pub.Q.X!), y = B64Url(pub.Q.Y!) }
            }
        });

        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var headerB64 = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"ES256\",\"kid\":\"ec-1\"}"));
        var payloadB64 = B64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            sub = "ec-user",
            iss = "https://accounts.google.com",
            aud = new[] { "other", "smartapps-client" },
            nonce = "ec-nonce",
            exp = now.AddMinutes(5).ToUnixTimeSeconds()
        })));
        var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");
        var sigBytes = ecdsa.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var jwt = $"{headerB64}.{payloadB64}.{B64Url(sigBytes)}";

        var (ok, claims, err) = SsoService.VerifyJwtWithJwkJson(jwt, jwks, "https://accounts.google.com", "smartapps-client", "ec-nonce", now);
        Assert.True(ok, err);
        Assert.Equal("ec-user", claims["sub"]);
    }
}
