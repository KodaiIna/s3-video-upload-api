using System.ComponentModel.DataAnnotations;

namespace S3VideoUploadApi.Contracts;

public sealed class Base64VideoUploadRequest
{
    [Required]
    public string? FileName { get; init; }

    public string? ContentType { get; init; }

    [Required]
    public string? Base64Data { get; init; }
}
