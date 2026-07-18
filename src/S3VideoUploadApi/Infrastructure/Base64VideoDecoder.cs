namespace S3VideoUploadApi.Infrastructure;

public static class Base64VideoDecoder
{
    public static Base64DecodeResult Decode(string? value, long maximumDecodedBytes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Base64DecodeResult.Failure("base64Data is required.");
        }

        var payload = value.Trim();
        string? embeddedContentType = null;

        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = payload.IndexOf(',');
            if (commaIndex < 0)
            {
                return Base64DecodeResult.Failure("The data URI is malformed.");
            }

            var header = payload[5..commaIndex];
            if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return Base64DecodeResult.Failure("The data URI must contain base64 data.");
            }

            var mediaTypeEnd = header.IndexOf(';');
            embeddedContentType = mediaTypeEnd >= 0
                ? header[..mediaTypeEnd]
                : header;
            payload = payload[(commaIndex + 1)..];
        }

        var encodedCharacterCount = 0L;
        foreach (var character in payload)
        {
            if (!char.IsWhiteSpace(character))
            {
                encodedCharacterCount++;
            }
        }

        if (encodedCharacterCount > GetMaximumEncodedLength(maximumDecodedBytes))
        {
            return Base64DecodeResult.Failure(
                $"The decoded video must not exceed {maximumDecodedBytes} bytes.");
        }

        try
        {
            var bytes = Convert.FromBase64String(payload);
            if (bytes.LongLength == 0)
            {
                return Base64DecodeResult.Failure("The decoded video is empty.");
            }

            if (bytes.LongLength > maximumDecodedBytes)
            {
                return Base64DecodeResult.Failure(
                    $"The decoded video must not exceed {maximumDecodedBytes} bytes.");
            }

            return Base64DecodeResult.Success(bytes, embeddedContentType);
        }
        catch (FormatException)
        {
            return Base64DecodeResult.Failure("base64Data is not valid Base64.");
        }
    }

    public static long GetMaximumEncodedLength(long maximumDecodedBytes)
    {
        if (maximumDecodedBytes <= 0)
        {
            return 0;
        }

        if (maximumDecodedBytes > ((long.MaxValue / 4L) * 3L) - 2L)
        {
            return long.MaxValue;
        }

        return ((maximumDecodedBytes + 2L) / 3L) * 4L;
    }
}

public sealed record Base64DecodeResult(
    byte[]? Bytes,
    string? EmbeddedContentType,
    string? Error)
{
    public bool IsSuccess => Bytes is not null;

    public static Base64DecodeResult Success(byte[] bytes, string? embeddedContentType) =>
        new(bytes, embeddedContentType, null);

    public static Base64DecodeResult Failure(string error) =>
        new(null, null, error);
}
