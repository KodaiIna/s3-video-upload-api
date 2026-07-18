using Amazon.S3.Model;

namespace S3VideoUploadApi.Storage;

/// <summary>
/// Narrows the large AWS SDK interface to the single S3 operation used by this API.
/// </summary>
public interface IS3ObjectClient
{
    Task<PutObjectResponse> PutObjectAsync(
        PutObjectRequest request,
        CancellationToken cancellationToken);
}
