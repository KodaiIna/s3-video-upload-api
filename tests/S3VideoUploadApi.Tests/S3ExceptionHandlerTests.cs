using Amazon.S3;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using S3VideoUploadApi.Infrastructure;

namespace S3VideoUploadApi.Tests;

public sealed class S3ExceptionHandlerTests
{
    /// <summary>AWS S3例外を502 Problem Detailsへ変換し、内部のAWS詳細をレスポンスへ漏らさないことを確認します。</summary>
    [Fact(DisplayName = "例外処理: AmazonS3Exceptionを502へ変換する")]
    public async Task TryHandleAsync_AmazonS3Exception_WritesBadGatewayProblem()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Response.Body = new MemoryStream();
        var handler = new S3ExceptionHandler(NullLogger<S3ExceptionHandler>.Instance);
        var exception = new AmazonS3Exception("secret AWS failure")
        {
            ErrorCode = "ServiceUnavailable"
        };

        var handled = await handler.TryHandleAsync(
            context,
            exception,
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("S3 upload failed", body);
        Assert.DoesNotContain("secret AWS failure", body);
    }

    /// <summary>S3以外の例外はこのハンドラーで握りつぶさず、後続の共通処理へ委譲することを確認します。</summary>
    [Fact(DisplayName = "例外処理: S3以外の例外は処理しない")]
    public async Task TryHandleAsync_NonS3Exception_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        var handler = new S3ExceptionHandler(NullLogger<S3ExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("test"),
            CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }
}
