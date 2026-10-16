using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PasswordKeeper.Core.Cloud;

/// <summary>
/// Minimal Backblaze B2 native-API client (v2): sign in, find, upload and download one file.
/// The vault is already encrypted, so only ciphertext ever leaves the device.
/// </summary>
public sealed class B2Client : IDisposable
{
    private const string AuthUrl = "https://api.backblazeb2.com/b2api/v2/b2_authorize_account";
    private const long MaxDownloadBytes = 50L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly CloudBackupSettings _settings;
    private B2AuthResponse? _auth;
    private string? _bucketId;

    /// <param name="handler">Test hook; production code leaves this null.</param>
    public B2Client(CloudBackupSettings settings, HttpMessageHandler? handler = null)
    {
        if (!settings.IsComplete) throw new B2Exception("Enter the key ID, application key, bucket and file name first.");
        _settings = settings;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    private string FileName => _settings.FileName.Trim();
    private string BucketName => _settings.Bucket.Trim();

    public void Dispose() => _http.Dispose();

    /// <summary>Lower-case hex SHA-1, which is what Backblaze uses to verify uploads.</summary>
    public static string Sha1Hex(byte[] data)
    {
#pragma warning disable CA5350 // Backblaze requires SHA-1 for upload verification; vault integrity comes from AES-GCM.
        return Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();
#pragma warning restore CA5350
    }

    /// <summary>The copy currently in the bucket, or null if there isn't one.</summary>
    public async Task<CloudFileInfo?> GetRemoteInfoAsync(CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct).ConfigureAwait(false);
        var bucketId = await GetBucketIdAsync(ct).ConfigureAwait(false);
        var body = await PostAsync(
            auth.ApiUrl + "/b2api/v2/b2_list_file_names",
            new B2ListFilesRequest { BucketId = bucketId, StartFileName = FileName, Prefix = FileName, MaxFileCount = 1 },
            B2JsonContext.Default.B2ListFilesRequest, auth.AuthorizationToken, "check the cloud copy", ct).ConfigureAwait(false);
        var list = JsonSerializer.Deserialize(body, B2JsonContext.Default.B2ListFilesResponse);
        var file = list?.Files?.FirstOrDefault(f => f.FileName == FileName);
        return file is null
            ? null
            : new CloudFileInfo(file.FileId, (file.ContentSha1 ?? "").ToLowerInvariant(), file.ContentLength,
                DateTimeOffset.FromUnixTimeMilliseconds(file.UploadTimestamp));
    }

    /// <summary>Uploads a new version of the file (older versions stay in the bucket per its lifecycle rules).</summary>
    public async Task<CloudFileInfo> UploadAsync(byte[] data, CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct).ConfigureAwait(false);
        var bucketId = await GetBucketIdAsync(ct).ConfigureAwait(false);
        var urlBody = await PostAsync(
            auth.ApiUrl + "/b2api/v2/b2_get_upload_url",
            new B2GetUploadUrlRequest { BucketId = bucketId },
            B2JsonContext.Default.B2GetUploadUrlRequest, auth.AuthorizationToken, "start the upload", ct).ConfigureAwait(false);
        var target = JsonSerializer.Deserialize(urlBody, B2JsonContext.Default.B2UploadUrlResponse)
                     ?? throw new B2Exception("Backblaze returned an unexpected reply.");

        var sha1 = Sha1Hex(data);
        using var req = new HttpRequestMessage(HttpMethod.Post, target.UploadUrl);
        req.Headers.TryAddWithoutValidation("Authorization", target.AuthorizationToken);
        req.Headers.TryAddWithoutValidation("X-Bz-File-Name", EncodePath(FileName));
        req.Headers.TryAddWithoutValidation("X-Bz-Content-Sha1", sha1);
        req.Content = new ByteArrayContent(data);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var resp = await SendAsync(req, ct).ConfigureAwait(false);
        var body = await ReadOkAsync(resp, "upload the vault", ct).ConfigureAwait(false);
        var file = JsonSerializer.Deserialize(body, B2JsonContext.Default.B2FileEntry)
                   ?? throw new B2Exception("Backblaze returned an unexpected reply.");
        return new CloudFileInfo(file.FileId, sha1, data.Length, DateTimeOffset.FromUnixTimeMilliseconds(file.UploadTimestamp));
    }

    /// <summary>Downloads the current version of the file and checks it against Backblaze's checksum.</summary>
    public async Task<byte[]> DownloadAsync(CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct).ConfigureAwait(false);
        var url = $"{auth.DownloadUrl}/file/{Uri.EscapeDataString(BucketName)}/{EncodePath(FileName)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", auth.AuthorizationToken);

        using var resp = await SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new B2Exception($"There is no \"{FileName}\" in bucket \"{BucketName}\" yet.");
        if (!resp.IsSuccessStatusCode) await ReadOkAsync(resp, "download the vault", ct).ConfigureAwait(false);

        if (resp.Content.Headers.ContentLength is > MaxDownloadBytes)
            throw new B2Exception("The file in the cloud is too large to be a vault.");
        var data = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        if (resp.Headers.TryGetValues("X-Bz-Content-Sha1", out var values))
        {
            var expected = values.FirstOrDefault() ?? "";
            if (expected.Length == 40 && !string.Equals(expected, Sha1Hex(data), StringComparison.OrdinalIgnoreCase))
                throw new B2Exception("The downloaded file failed its checksum. Try again.");
        }
        return data;
    }

    // ---- plumbing ------------------------------------------------------------------------

    private async Task<B2AuthResponse> AuthorizeAsync(CancellationToken ct)
    {
        if (_auth is not null) return _auth;
        using var req = new HttpRequestMessage(HttpMethod.Get, AuthUrl);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(_settings.KeyId.Trim() + ":" + _settings.ApplicationKey.Trim()));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        using var resp = await SendAsync(req, ct).ConfigureAwait(false);
        var body = await ReadOkAsync(resp, "sign in to Backblaze", ct).ConfigureAwait(false);
        _auth = JsonSerializer.Deserialize(body, B2JsonContext.Default.B2AuthResponse)
                ?? throw new B2Exception("Backblaze returned an unexpected reply.");
        return _auth;
    }

    private async Task<string> GetBucketIdAsync(CancellationToken ct)
    {
        if (_bucketId is not null) return _bucketId;
        var auth = await AuthorizeAsync(ct).ConfigureAwait(false);
        var bucket = BucketName;

        // A key restricted to one bucket says which one; an unrestricted key has to look it up.
        var allowed = auth.Allowed;
        if (allowed is not null && !string.IsNullOrEmpty(allowed.BucketId))
        {
            if (!string.IsNullOrEmpty(allowed.BucketName) && !string.Equals(allowed.BucketName, bucket, StringComparison.Ordinal))
                throw new B2Exception($"This key only works with bucket \"{allowed.BucketName}\", not \"{bucket}\".");
            return _bucketId = allowed.BucketId;
        }

        var body = await PostAsync(
            auth.ApiUrl + "/b2api/v2/b2_list_buckets",
            new B2ListBucketsRequest { AccountId = auth.AccountId, BucketName = bucket },
            B2JsonContext.Default.B2ListBucketsRequest, auth.AuthorizationToken, "find the bucket", ct).ConfigureAwait(false);
        var found = JsonSerializer.Deserialize(body, B2JsonContext.Default.B2ListBucketsResponse)?
            .Buckets?.FirstOrDefault(b => b.BucketName == bucket);
        return _bucketId = found?.BucketId ?? throw new B2Exception($"Bucket \"{bucket}\" was not found.");
    }

    private async Task<string> PostAsync<T>(string url, T payload, JsonTypeInfo<T> info, string token, string what, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Authorization", token);
        req.Content = new StringContent(JsonSerializer.Serialize(payload, info), Encoding.UTF8, "application/json");
        using var resp = await SendAsync(req, ct).ConfigureAwait(false);
        return await ReadOkAsync(resp, what, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new B2Exception("Could not reach Backblaze. Check the internet connection.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new B2Exception("Backblaze took too long to answer.", ex);
        }
    }

    private static async Task<string> ReadOkAsync(HttpResponseMessage resp, string what, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode) return body;

        string? detail = null;
        try { detail = JsonSerializer.Deserialize(body, B2JsonContext.Default.B2ErrorResponse)?.Message; }
        catch (JsonException) { /* not JSON; fall back to the status code */ }
        var reason = resp.StatusCode == HttpStatusCode.Unauthorized
            ? "the key ID or application key was not accepted"
            : detail ?? $"HTTP {(int)resp.StatusCode}";
        throw new B2Exception($"Could not {what}: {reason}.");
    }

    // B2 wants the file name URL-encoded, with "/" kept as a folder separator.
    private static string EncodePath(string name) =>
        string.Join("/", name.Trim().Split('/').Select(Uri.EscapeDataString));
}
