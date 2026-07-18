namespace S3VideoUploadApi.Configuration;

public sealed class S3Options
{
    public const string SectionName = "S3";
    public const long DefaultMaxFileSizeBytes = 25L * 1024L * 1024L;
    public const long MaximumAllowedFileSizeBytes = 256L * 1024L * 1024L;

    public string BucketName { get; set; } = string.Empty;

    public string Region { get; set; } = "ap-northeast-1";

    public string KeyPrefix { get; set; } = "videos";

    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

    // Start empty so configuration binding replaces the list instead of appending
    // to an initialized array. Program applies these defaults after binding.
    public string[] AllowedContentTypes { get; set; } = [];

    public static string[] CreateDefaultAllowedContentTypes() =>
    [
        "video/mp4",
        "video/webm",
        "video/quicktime",
        "video/x-matroska",
        "video/x-msvideo"
    ];

    // Optional: useful for LocalStack, MinIO, or another S3-compatible endpoint.
    public string? ServiceUrl { get; set; }

    public bool ForcePathStyle { get; set; }
}
