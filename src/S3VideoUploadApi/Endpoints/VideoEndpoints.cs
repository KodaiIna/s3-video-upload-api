using S3VideoUploadApi.Contracts;

namespace S3VideoUploadApi.Endpoints;

public static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/videos")
            .WithTags("Videos");

        group.MapPost("/multipart", VideoUploadHandlers.UploadMultipartAsync)
            .DisableAntiforgery()
            .WithName("UploadVideoMultipart")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<VideoUploadResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        group.MapPost("/base64", VideoUploadHandlers.UploadBase64Async)
            .WithName("UploadVideoBase64")
            .Accepts<Base64VideoUploadRequest>("application/json")
            .Produces<VideoUploadResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return endpoints;
    }
}
