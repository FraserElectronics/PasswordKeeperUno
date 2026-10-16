using System.Text.Json.Serialization;

namespace PasswordKeeper.Core.Cloud;

internal sealed class B2AuthResponse
{
    public string AccountId { get; set; } = "";
    public string AuthorizationToken { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public B2Allowed? Allowed { get; set; }
}

internal sealed class B2Allowed
{
    public string? BucketId { get; set; }
    public string? BucketName { get; set; }
}

internal sealed class B2ListBucketsRequest
{
    public string AccountId { get; set; } = "";
    public string BucketName { get; set; } = "";
}

internal sealed class B2ListBucketsResponse
{
    public List<B2Bucket>? Buckets { get; set; }
}

internal sealed class B2Bucket
{
    public string BucketId { get; set; } = "";
    public string BucketName { get; set; } = "";
}

internal sealed class B2ListFilesRequest
{
    public string BucketId { get; set; } = "";
    public string StartFileName { get; set; } = "";
    public string Prefix { get; set; } = "";
    public int MaxFileCount { get; set; } = 1;
}

internal sealed class B2ListFilesResponse
{
    public List<B2FileEntry>? Files { get; set; }
}

internal sealed class B2FileEntry
{
    public string FileId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? ContentSha1 { get; set; }
    public long ContentLength { get; set; }
    public long UploadTimestamp { get; set; }
}

internal sealed class B2GetUploadUrlRequest
{
    public string BucketId { get; set; } = "";
}

internal sealed class B2UploadUrlResponse
{
    public string UploadUrl { get; set; } = "";
    public string AuthorizationToken { get; set; } = "";
}

internal sealed class B2ErrorResponse
{
    public string? Code { get; set; }
    public string? Message { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(B2AuthResponse))]
[JsonSerializable(typeof(B2ListBucketsRequest))]
[JsonSerializable(typeof(B2ListBucketsResponse))]
[JsonSerializable(typeof(B2ListFilesRequest))]
[JsonSerializable(typeof(B2ListFilesResponse))]
[JsonSerializable(typeof(B2GetUploadUrlRequest))]
[JsonSerializable(typeof(B2UploadUrlResponse))]
[JsonSerializable(typeof(B2FileEntry))]
[JsonSerializable(typeof(B2ErrorResponse))]
internal partial class B2JsonContext : JsonSerializerContext
{
}
