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
            .WithSummary("Upload a video using multipart/form-data")
            .WithDescription("Validates a small video and uploads it to the configured S3 bucket.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<VideoUploadResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapPost("/base64", VideoUploadHandlers.UploadBase64Async)
            .WithName("UploadVideoBase64")
            .WithSummary("Upload a video using Base64 JSON")
            .WithDescription("Decodes and validates a Base64 video before uploading it to S3.")
            .Accepts<Base64VideoUploadRequest>("application/json")
            .Produces<VideoUploadResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return endpoints;
    }
}
