using S3VideoUploadApi.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.ApplyEnvironmentConfiguration(args);
builder.AddVideoUploadServices();

var app = builder.Build();
app.MapVideoUploadApi();
app.Run();

public partial class Program;
