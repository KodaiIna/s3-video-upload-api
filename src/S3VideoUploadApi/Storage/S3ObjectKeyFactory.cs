namespace S3VideoUploadApi.Storage;

public static class S3ObjectKeyFactory
{
    private static readonly IReadOnlyDictionary<string, string> KnownExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["video/mp4"] = ".mp4",
            ["video/webm"] = ".webm",
            ["video/quicktime"] = ".mov",
            ["video/x-matroska"] = ".mkv",
            ["video/x-msvideo"] = ".avi"
        };

    public static string Create(
        string keyPrefix,
        string originalFileName,
        string contentType,
        DateTimeOffset timestamp,
        Guid id)
    {
        var prefix = keyPrefix.Trim().Trim('/');
        var extension = GetSafeExtension(originalFileName, contentType);
        var datedName = $"{timestamp:yyyy/MM/dd}/{id:N}{extension}";

        return string.IsNullOrWhiteSpace(prefix)
            ? datedName
            : $"{prefix}/{datedName}";
    }

    private static string GetSafeExtension(string originalFileName, string contentType)
    {
        if (KnownExtensions.TryGetValue(contentType, out var knownExtension))
        {
            return knownExtension;
        }

        var suppliedExtension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (suppliedExtension.Length is > 1 and <= 10 &&
            suppliedExtension[1..].All(char.IsAsciiLetterOrDigit))
        {
            return suppliedExtension;
        }

        return ".bin";
    }
}
