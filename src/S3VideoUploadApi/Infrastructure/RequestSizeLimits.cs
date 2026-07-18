namespace S3VideoUploadApi.Infrastructure;

public static class RequestSizeLimits
{
    public static long AddSaturating(long value, long amount) =>
        value > long.MaxValue - amount ? long.MaxValue : value + amount;
}
