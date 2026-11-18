using System.Security.Cryptography;
using System.Text;
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
}
