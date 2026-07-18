using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using S3VideoUploadApi.Configuration;
using S3VideoUploadApi.Endpoints;
using S3VideoUploadApi.Infrastructure;
using S3VideoUploadApi.Storage;

var builder = WebApplication.CreateBuilder(args);

// The UserSecretsId is shared by the project, so Development would normally
// load the real-S3 bucket secret too. Reapply the LocalStack file afterward to
// keep the two local modes isolated, then restore standard override precedence.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile(
        "appsettings.Development.json",
        optional: false,
        reloadOnChange: true);
    builder.Configuration.AddEnvironmentVariables();
    if (args.Length > 0)
    {
        builder.Configuration.AddCommandLine(args);
    }
}

// ASP.NET Core loads User Secrets automatically only for Development. LocalAws
// deliberately does not load appsettings.Development.json (the LocalStack
// endpoint), and environment/command-line values retain their normal priority.
if (builder.Environment.IsEnvironment("LocalAws"))
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
    builder.Configuration.AddEnvironmentVariables();
    if (args.Length > 0)
    {
        builder.Configuration.AddCommandLine(args);
    }
}

var startupS3Options = builder.Configuration
    .GetSection(S3Options.SectionName)
    .Get<S3Options>() ?? new S3Options();

var configuredMaxFileSize = startupS3Options.MaxFileSizeBytes > 0
    ? startupS3Options.MaxFileSizeBytes
    : S3Options.DefaultMaxFileSizeBytes;

var requestOverhead = 1024L * 1024L;
var multipartRequestLimit = RequestSizeLimits.AddSaturating(configuredMaxFileSize, requestOverhead);
var base64RequestLimit = RequestSizeLimits.AddSaturating(
    Base64VideoDecoder.GetMaximumEncodedLength(configuredMaxFileSize),
    requestOverhead);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = Math.Max(multipartRequestLimit, base64RequestLimit);
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = multipartRequestLimit;
});

builder.Services.PostConfigure<S3Options>(options =>
{
    if (options.AllowedContentTypes.Length == 0)
    {
        options.AllowedContentTypes = S3Options.CreateDefaultAllowedContentTypes();
    }
});

builder.Services
    .AddOptions<S3Options>()
    .Bind(builder.Configuration.GetSection(S3Options.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.BucketName),
        "S3:BucketName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Region),
        "S3:Region is required.")
    .Validate(options => options.MaxFileSizeBytes is > 0 and <= S3Options.MaximumAllowedFileSizeBytes,
        $"S3:MaxFileSizeBytes must be between 1 and {S3Options.MaximumAllowedFileSizeBytes} bytes.")
    .Validate(options => options.AllowedContentTypes.Length > 0,
        "S3:AllowedContentTypes must contain at least one value.")
    .ValidateOnStart();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<S3ExceptionHandler>();

builder.Services.AddSingleton<IAmazonS3>(serviceProvider =>
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
});

builder.Services.AddSingleton<IS3ObjectClient, AmazonS3ObjectClient>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IVideoStorage, S3VideoStorage>();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod());
    });
}

var app = builder.Build();

app.UseExceptionHandler();

if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.MapVideoEndpoints();

app.Run();

public partial class Program;
