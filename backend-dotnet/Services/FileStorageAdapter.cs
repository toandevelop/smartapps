using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SmartApps.Api.Services;

public interface IFileStorageAdapter
{
    string ProviderName { get; }
    Task SaveAsync(string storageKey, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}

public sealed class LocalFileStorageAdapter : IFileStorageAdapter
{
    private readonly string _rootDir;

    public LocalFileStorageAdapter(IWebHostEnvironment env)
    {
        _rootDir = Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(_rootDir);
    }

    public LocalFileStorageAdapter(string rootDir)
    {
        _rootDir = rootDir;
        Directory.CreateDirectory(_rootDir);
    }

    public string ProviderName => "Local";

    public async Task SaveAsync(string storageKey, Stream content, string contentType, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_rootDir, storageKey);
        await using var fs = File.Create(fullPath);
        await content.CopyToAsync(fs, ct);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_rootDir, storageKey);
        if (!File.Exists(fullPath)) return Task.FromResult<Stream?>(null);
        Stream s = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(s);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_rootDir, storageKey);
        if (File.Exists(fullPath))
        {
            try { File.Delete(fullPath); } catch { /* ignore file lock */ }
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// S3 / MinIO compatible storage adapter using AWS Signature V4 over HttpClient.
/// </summary>
public sealed class S3CompatibleStorageAdapter : IFileStorageAdapter
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _endpoint;
    private readonly string _bucket;
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _region;

    public S3CompatibleStorageAdapter(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
        _endpoint = (config["Storage:S3:Endpoint"] ?? "http://localhost:9000").TrimEnd('/');
        _bucket = config["Storage:S3:Bucket"] ?? "smartapps-uploads";
        _accessKey = config["Storage:S3:AccessKey"] ?? "";
        _secretKey = config["Storage:S3:SecretKey"] ?? "";
        _region = config["Storage:S3:Region"] ?? "us-east-1";
    }

    public string ProviderName => "S3";

    public async Task SaveAsync(string storageKey, Stream content, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var payloadHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var url = $"{_endpoint}/{Uri.EscapeDataString(_bucket)}/{Uri.EscapeDataString(storageKey)}";

        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new ByteArrayContent(bytes)
        };
        req.Content.Headers.TryAddWithoutValidation("Content-Type", string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        SignRequest(req, payloadHash, DateTime.UtcNow);

        var resp = await _httpFactory.CreateClient().SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var url = $"{_endpoint}/{Uri.EscapeDataString(_bucket)}/{Uri.EscapeDataString(storageKey)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        SignRequest(req, EmptyPayloadSha256, DateTime.UtcNow);

        var resp = await _httpFactory.CreateClient().SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var ms = new MemoryStream();
        await resp.Content.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var url = $"{_endpoint}/{Uri.EscapeDataString(_bucket)}/{Uri.EscapeDataString(storageKey)}";
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        SignRequest(req, EmptyPayloadSha256, DateTime.UtcNow);

        var resp = await _httpFactory.CreateClient().SendAsync(req, ct);
        if (resp.StatusCode != HttpStatusCode.NotFound)
            resp.EnsureSuccessStatusCode();
    }

    private const string EmptyPayloadSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public void SignRequest(HttpRequestMessage req, string payloadSha256, DateTime utcNow)
    {
        var headers = BuildSigV4Headers(req.Method.Method, req.RequestUri!, _accessKey, _secretKey, _region, payloadSha256, utcNow);
        foreach (var kv in headers)
        {
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
    }

    public static Dictionary<string, string> BuildSigV4Headers(
        string method,
        Uri uri,
        string accessKey,
        string secretKey,
        string region,
        string payloadSha256,
        DateTime utcNow)
    {
        var amzDate = utcNow.ToString("yyyyMMddTHHmmssZ");
        var dateStamp = utcNow.ToString("yyyyMMdd");
        var host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var canonicalHeaders = $"host:{host}\nx-amz-content-sha256:{payloadSha256}\nx-amz-date:{amzDate}\n";
        var signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = $"{method}\n{uri.AbsolutePath}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadSha256}";

        var scope = $"{dateStamp}/{region}/s3/aws4_request";
        var canonicalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))).ToLowerInvariant();
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{canonicalHash}";

        var kDate = HmacSha256(Encoding.UTF8.GetBytes($"AWS4{secretKey}"), dateStamp);
        var kRegion = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, "s3");
        var kSigning = HmacSha256(kService, "aws4_request");
        var signature = Convert.ToHexString(HmacSha256(kSigning, stringToSign)).ToLowerInvariant();

        return new Dictionary<string, string>
        {
            ["x-amz-date"] = amzDate,
            ["x-amz-content-sha256"] = payloadSha256,
            ["Authorization"] = $"AWS4-HMAC-SHA256 Credential={accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}"
        };
    }

    private static byte[] HmacSha256(byte[] key, string data) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
}
