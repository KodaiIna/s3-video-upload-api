namespace S3VideoUploadApi.Contracts;

public sealed record VideoUploadResponse(
    string Bucket,
    string Key,
    string ETag,
    long Size,
    string ContentType);
