using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using S3VideoUploadApi.Contracts;
using S3VideoUploadApi.Storage;

namespace S3VideoUploadApi.Tests;

internal sealed class RecordingVideoStorage : IVideoStorage
{
    public int CallCount { get; private set; }

    public string? OriginalFileName { get; private set; }

    public string? ContentType { get; private set; }

    public long Length { get; private set; }

    public byte[]? Bytes { get; private set; }

    public CancellationToken CancellationToken { get; private set; }

    public StoredVideo Result { get; init; } = new(
        "test-bucket",
        "videos/test.mp4",
        "test-etag",
        4,
        "video/mp4");

    public async Task<StoredVideo> UploadAsync(
        Stream stream,
        string originalFileName,
        string contentType,
        long length,
        CancellationToken cancellationToken)
    {
        CallCount++;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        Length = length;
        CancellationToken = cancellationToken;

        await using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        Bytes = copy.ToArray();

        return Result with { Size = length, ContentType = contentType };
    }
}

internal sealed class RecordingS3ObjectClient : IS3ObjectClient
{
    public PutObjectRequest? Request { get; private set; }

    public CancellationToken CancellationToken { get; private set; }

    public PutObjectResponse Response { get; init; } = new()
    {
        ETag = "\"test-etag\""
    };

    public Task<PutObjectResponse> PutObjectAsync(
        PutObjectRequest request,
        CancellationToken cancellationToken)
    {
        Request = request;
        CancellationToken = cancellationToken;
        return Task.FromResult(Response);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal static class ResultAssertions
{
    public static T AssertOk<T>(IResult result)
    {
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status200OK, statusResult.StatusCode);

        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        return Assert.IsType<T>(valueResult.Value);
    }

    public static HttpValidationProblemDetails AssertValidationProblem(IResult result)
    {
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusResult.StatusCode);

        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        return Assert.IsType<HttpValidationProblemDetails>(valueResult.Value);
    }
}
