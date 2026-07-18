using S3VideoUploadApi.Configuration;

namespace S3VideoUploadApi.Infrastructure;

public static class VideoUploadValidator
{
    public static Dictionary<string, string[]> Validate(
        string fileName,
        string contentType,
        long length,
        S3Options options)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            errors["fileName"] = ["A file name is required."];
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            errors["contentType"] = ["A content type is required."];
        }
        else if (!options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            errors["contentType"] =
            [
                $"Unsupported content type. Allowed values: {string.Join(", ", options.AllowedContentTypes)}."
            ];
        }

        if (length <= 0)
        {
            errors["file"] = ["The video is empty."];
        }
        else if (length > options.MaxFileSizeBytes)
        {
            errors["file"] =
            [
                $"The video must not exceed {options.MaxFileSizeBytes} bytes."
            ];
        }

        return errors;
    }

    public static string NormalizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        return Path.GetFileName(fileName.Replace('\\', '/')).Trim();
    }

    public static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        return contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
    }
}
