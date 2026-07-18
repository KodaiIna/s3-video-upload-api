namespace S3VideoUploadApi.Contracts;

public sealed class Base64VideoUploadRequest
{
    public string? FileName { get; init; }

    public string? ContentType { get; init; }

    public string? Base64Data { get; init; }
}
