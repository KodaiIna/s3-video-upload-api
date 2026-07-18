using Amazon.S3;
using Microsoft.AspNetCore.Diagnostics;

namespace S3VideoUploadApi.Infrastructure;

public sealed class S3ExceptionHandler(
    ILogger<S3ExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not AmazonS3Exception s3Exception)
        {
            return false;
        }

        logger.LogError(
            s3Exception,
            "S3 upload failed with AWS error code {ErrorCode} and request ID {RequestId}",
            s3Exception.ErrorCode,
            s3Exception.RequestId);

        httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
        await Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "S3 upload failed",
                detail: "The video could not be stored. Retry the request later.")
            .ExecuteAsync(httpContext);

        return true;
    }
}
