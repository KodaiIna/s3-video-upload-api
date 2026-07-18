namespace S3VideoUploadApi.Storage;

public interface IVideoStorage
{
    Task<StoredVideo> UploadAsync(
        Stream stream,
        string originalFileName,
        string contentType,
        long length,
        CancellationToken cancellationToken);
}

public sealed record StoredVideo(
    string Bucket,
    string Key,
    string ETag,
    long Size,
    string ContentType);
