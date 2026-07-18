using Microsoft.Extensions.Options;
using S3VideoUploadApi.Configuration;
using S3VideoUploadApi.Contracts;
using S3VideoUploadApi.Infrastructure;
using S3VideoUploadApi.Storage;

namespace S3VideoUploadApi.Endpoints;

public static class VideoUploadHandlers
{
    public static async Task<IResult> UploadMultipartAsync(
        IFormFile file,
        IVideoStorage storage,
        IOptions<S3Options> options,
        CancellationToken cancellationToken)
    {
        var normalizedFileName = VideoUploadValidator.NormalizeFileName(file.FileName);
        var normalizedContentType = VideoUploadValidator.NormalizeContentType(file.ContentType);
        var errors = VideoUploadValidator.Validate(
            normalizedFileName,
            normalizedContentType,
            file.Length,
            options.Value);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        await using var stream = file.OpenReadStream();
        var stored = await storage.UploadAsync(
            stream,
            normalizedFileName,
            normalizedContentType,
            file.Length,
            cancellationToken);

        return Results.Ok(ToResponse(stored));
    }

    public static async Task<IResult> UploadBase64Async(
        Base64VideoUploadRequest request,
        IVideoStorage storage,
        IOptions<S3Options> options,
        CancellationToken cancellationToken)
    {
        var normalizedFileName = VideoUploadValidator.NormalizeFileName(request.FileName);

        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            return ValidationProblem("fileName", "fileName is required.");
        }

        var decodeResult = Base64VideoDecoder.Decode(
            request.Base64Data,
            options.Value.MaxFileSizeBytes);

        if (!decodeResult.IsSuccess)
        {
            return ValidationProblem("base64Data", decodeResult.Error!);
        }

        var requestedContentType = VideoUploadValidator.NormalizeContentType(request.ContentType);
        var embeddedContentType = VideoUploadValidator.NormalizeContentType(decodeResult.EmbeddedContentType);

        if (!string.IsNullOrWhiteSpace(requestedContentType) &&
            !string.IsNullOrWhiteSpace(embeddedContentType) &&
            !string.Equals(requestedContentType, embeddedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return ValidationProblem(
                "contentType",
                "contentType does not match the media type in the data URI.");
        }

        var effectiveContentType = !string.IsNullOrWhiteSpace(requestedContentType)
            ? requestedContentType
            : embeddedContentType;

        var bytes = decodeResult.Bytes!;
        var errors = VideoUploadValidator.Validate(
            normalizedFileName,
            effectiveContentType,
            bytes.LongLength,
            options.Value);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        await using var stream = new MemoryStream(bytes, writable: false);
        var stored = await storage.UploadAsync(
            stream,
            normalizedFileName,
            effectiveContentType,
            bytes.LongLength,
            cancellationToken);

        return Results.Ok(ToResponse(stored));
    }

    private static VideoUploadResponse ToResponse(StoredVideo stored) =>
        new(stored.Bucket, stored.Key, stored.ETag, stored.Size, stored.ContentType);

    private static IResult ValidationProblem(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [key] = [message]
        });
}
