using Amazon.S3;
using Amazon.S3.Model;

namespace S3VideoUploadApi.Storage;

public sealed class AmazonS3ObjectClient(IAmazonS3 client) : IS3ObjectClient
{
    public Task<PutObjectResponse> PutObjectAsync(
        PutObjectRequest request,
        CancellationToken cancellationToken) =>
        client.PutObjectAsync(request, cancellationToken);
}
