using S3VideoUploadApi.Contracts;
using S3VideoUploadApi.Endpoints;

namespace S3VideoUploadApi.Hosting;

public static class WebApplicationExtensions
{
    public static WebApplication MapVideoUploadApi(this WebApplication app)
    {
        app.UseExceptionHandler();

        if (ServiceRegistrationExtensions.GetAllowedOrigins(app.Configuration).Length > 0)
        {
            app.UseCors();
        }

        if (IsLocalDevelopment(app.Environment))
        {
            app.MapOpenApi();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "S3 Video Upload API v1");
            });
        }

        app.MapGet("/health", () => Results.Ok(new HealthResponse("ok")))
            .WithName("Health")
            .WithTags("System")
            .WithSummary("Check whether the API process is responding")
            .Produces<HealthResponse>(StatusCodes.Status200OK);

        app.MapVideoEndpoints();
        return app;
    }

    private static bool IsLocalDevelopment(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("LocalAws");
}
