using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using S3VideoUploadApi.Configuration;

namespace S3VideoUploadApi.Storage;

public sealed class S3VideoStorage(
    IS3ObjectClient s3Client,
    IOptions<S3Options> options,
    TimeProvider timeProvider) : IVideoStorage
{
    public async Task<StoredVideo> UploadAsync(
        Stream stream,
        string originalFileName,
        string contentType,
        long length,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var key = S3ObjectKeyFactory.Create(
            settings.KeyPrefix,
            originalFileName,
            contentType,
            timeProvider.GetUtcNow(),
            Guid.NewGuid());

        var request = new PutObjectRequest
        {
            BucketName = settings.BucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            AutoCloseStream = false
        };

        var response = await s3Client.PutObjectAsync(request, cancellationToken);

        return new StoredVideo(
            settings.BucketName,
            key,
            response.ETag?.Trim('"') ?? string.Empty,
            length,
            contentType);
    }
}
