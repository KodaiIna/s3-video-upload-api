using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using S3VideoUploadApi.Configuration;
using S3VideoUploadApi.Infrastructure;
using S3VideoUploadApi.Storage;

namespace S3VideoUploadApi.Hosting;

public static class ServiceRegistrationExtensions
{
    private const long RequestOverheadBytes = 1024L * 1024L;

    public static WebApplicationBuilder AddVideoUploadServices(this WebApplicationBuilder builder)
    {
        ConfigureRequestLimits(builder);
        ConfigureS3Options(builder.Services, builder.Configuration);

        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<S3ExceptionHandler>();
        builder.Services.AddSingleton<IAmazonS3>(CreateS3Client);
        builder.Services.AddSingleton<IS3ObjectClient, AmazonS3ObjectClient>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IVideoStorage, S3VideoStorage>();

        AddCorsWhenConfigured(builder.Services, builder.Configuration);
        return builder;
    }

    private static void ConfigureRequestLimits(WebApplicationBuilder builder)
    {
        var startupOptions = builder.Configuration
            .GetSection(S3Options.SectionName)
            .Get<S3Options>() ?? new S3Options();

        var maximumFileSize = startupOptions.MaxFileSizeBytes > 0
            ? startupOptions.MaxFileSizeBytes
            : S3Options.DefaultMaxFileSizeBytes;

        var multipartLimit = RequestSizeLimits.AddSaturating(
            maximumFileSize,
            RequestOverheadBytes);
        var base64Limit = RequestSizeLimits.AddSaturating(
            Base64VideoDecoder.GetMaximumEncodedLength(maximumFileSize),
            RequestOverheadBytes);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = Math.Max(multipartLimit, base64Limit);
        });

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = multipartLimit;
        });
    }

    private static void ConfigureS3Options(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.PostConfigure<S3Options>(options =>
        {
            if (options.AllowedContentTypes.Length == 0)
            {
                options.AllowedContentTypes = S3Options.CreateDefaultAllowedContentTypes();
            }
        });

        services
            .AddOptions<S3Options>()
            .Bind(configuration.GetSection(S3Options.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BucketName),
                "S3:BucketName is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Region),
                "S3:Region is required.")
            .Validate(options => options.MaxFileSizeBytes is > 0 and <= S3Options.MaximumAllowedFileSizeBytes,
                $"S3:MaxFileSizeBytes must be between 1 and {S3Options.MaximumAllowedFileSizeBytes} bytes.")
            .Validate(options => options.AllowedContentTypes.Length > 0,
                "S3:AllowedContentTypes must contain at least one value.")
            .ValidateOnStart();
    }

    private static IAmazonS3 CreateS3Client(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<S3Options>>().Value;
        var config = new AmazonS3Config
        {
            ForcePathStyle = options.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = options.Region;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        // Credentials are resolved by the standard AWS SDK credential chain.
        return new AmazonS3Client(config);
    }

    private static void AddCorsWhenConfigured(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = GetAllowedOrigins(configuration);
        if (allowedOrigins.Length == 0)
        {
            return;
        }

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod());
        });
    }

    internal static string[] GetAllowedOrigins(IConfiguration configuration) =>
        configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
}
