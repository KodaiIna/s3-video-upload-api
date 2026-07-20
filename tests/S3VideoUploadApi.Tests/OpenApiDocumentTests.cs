using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using S3VideoUploadApi.Contracts;
using S3VideoUploadApi.Hosting;

namespace S3VideoUploadApi.Tests;

public sealed class OpenApiDocumentTests
{
    /// <summary>
    /// Verifies that the generated OpenAPI schema marks only the mandatory Base64 fields as required.
    /// </summary>
    [Fact(DisplayName = "OpenAPI: Base64 upload fields are marked as required")]
    public async Task GeneratedDocument_Base64Request_ContainsRequiredProperties()
    {
        using var document = await GenerateDocumentAsync();
        var schema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(nameof(Base64VideoUploadRequest));
        var required = schema
            .GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();

        Assert.Contains("fileName", required);
        Assert.Contains("base64Data", required);
        Assert.DoesNotContain("contentType", required);
    }

    /// <summary>
    /// Verifies that a Kestrel-generated 413 response does not promise a Problem Details response body.
    /// </summary>
    [Theory(DisplayName = "OpenAPI: 413 responses do not declare a Problem Details body")]
    [InlineData("/api/videos/multipart")]
    [InlineData("/api/videos/base64")]
    public async Task GeneratedDocument_PayloadTooLarge_DoesNotDeclareResponseBody(string path)
    {
        using var document = await GenerateDocumentAsync();
        var response = document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post")
            .GetProperty("responses")
            .GetProperty("413");

        if (response.TryGetProperty("content", out var content))
        {
            Assert.Empty(content.EnumerateObject());
        }
    }

    private static async Task<JsonDocument> GenerateDocumentAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["S3:BucketName"] = "test-bucket",
            ["S3:Region"] = "ap-northeast-1"
        });
        builder.AddVideoUploadServices();
        builder.Services.AddSingleton<IServer, NoOpServer>();

        await using var app = builder.Build();
        app.MapVideoUploadApi();

        // Starting with a no-op server finalizes endpoint metadata without opening a network port.
        await app.StartAsync();

        var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = await provider.GetOpenApiDocumentAsync();
        var json = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1);

        await app.StopAsync();
        return JsonDocument.Parse(json);
    }

    private sealed class NoOpServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(
            IHttpApplication<TContext> application,
            CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
