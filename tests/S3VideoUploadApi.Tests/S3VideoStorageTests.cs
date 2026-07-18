using Microsoft.Extensions.Options;
using S3VideoUploadApi.Configuration;
using S3VideoUploadApi.Storage;

namespace S3VideoUploadApi.Tests;

public sealed class S3VideoStorageTests
{
    /// <summary>S3 PutObjectへバケット・キー・ストリーム・Content-Typeを正しく渡し、ETagを整形することを確認します。</summary>
    [Fact(DisplayName = "S3保存: PutObjectリクエストと戻り値を正しく構築する")]
    public async Task UploadAsync_ValidVideo_SendsExpectedPutObjectRequest()
    {
        var client = new RecordingS3ObjectClient();
        var options = Microsoft.Extensions.Options.Options.Create(new S3Options
        {
            BucketName = "unit-test-bucket",
            KeyPrefix = "/uploads/"
        });
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));
        var storage = new S3VideoStorage(client, options, timeProvider);
        await using var stream = new MemoryStream([1, 2, 3, 4]);
        using var cancellationSource = new CancellationTokenSource();

        var result = await storage.UploadAsync(
            stream,
            "sample.mp4",
            "video/mp4",
            4,
            cancellationSource.Token);

        var request = Assert.IsType<Amazon.S3.Model.PutObjectRequest>(client.Request);
        Assert.Equal("unit-test-bucket", request.BucketName);
        Assert.Matches(
            "^uploads/2026/07/18/[0-9a-f]{32}\\.mp4$",
            request.Key);
        Assert.Same(stream, request.InputStream);
        Assert.Equal("video/mp4", request.ContentType);
        Assert.False(request.AutoCloseStream);
        Assert.Equal(cancellationSource.Token, client.CancellationToken);

        Assert.Equal("unit-test-bucket", result.Bucket);
        Assert.Equal(request.Key, result.Key);
        Assert.Equal("test-etag", result.ETag);
        Assert.Equal(4, result.Size);
        Assert.Equal("video/mp4", result.ContentType);
    }
}
