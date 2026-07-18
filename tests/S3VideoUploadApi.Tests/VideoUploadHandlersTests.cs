using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using S3VideoUploadApi.Configuration;
using S3VideoUploadApi.Contracts;
using S3VideoUploadApi.Endpoints;

namespace S3VideoUploadApi.Tests;

public sealed class VideoUploadHandlersTests
{
    private static readonly IOptions<S3Options> Options = Microsoft.Extensions.Options.Options.Create(
        new S3Options
        {
            BucketName = "test-bucket",
            MaxFileSizeBytes = 100,
            AllowedContentTypes = S3Options.CreateDefaultAllowedContentTypes()
        });

    /// <summary>正常なmultipart動画をストレージへ渡し、保存結果を200レスポンスに変換することを確認します。</summary>
    [Fact(DisplayName = "multipart API: 正常な動画を保存して結果を返す")]
    public async Task UploadMultipartAsync_ValidFile_UploadsAndReturnsOk()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var file = CreateFormFile(bytes, @"C:\fakepath\sample.mp4", "Video/MP4; charset=binary");
        var storage = new RecordingVideoStorage();
        using var cancellationSource = new CancellationTokenSource();

        var result = await VideoUploadHandlers.UploadMultipartAsync(
            file,
            storage,
            Options,
            cancellationSource.Token);

        var response = ResultAssertions.AssertOk<VideoUploadResponse>(result);
        Assert.Equal(1, storage.CallCount);
        Assert.Equal("sample.mp4", storage.OriginalFileName);
        Assert.Equal("video/mp4", storage.ContentType);
        Assert.Equal(bytes, storage.Bytes);
        Assert.Equal(cancellationSource.Token, storage.CancellationToken);
        Assert.Equal("test-bucket", response.Bucket);
        Assert.Equal("videos/test.mp4", response.Key);
    }

    /// <summary>未許可Content-Typeのmultipart動画をS3へ渡さず400にすることを確認します。</summary>
    [Fact(DisplayName = "multipart API: 未許可Content-Typeを400で拒否する")]
    public async Task UploadMultipartAsync_UnsupportedContentType_DoesNotUpload()
    {
        var file = CreateFormFile([1], "sample.txt", "text/plain");
        var storage = new RecordingVideoStorage();

        var result = await VideoUploadHandlers.UploadMultipartAsync(
            file,
            storage,
            Options,
            CancellationToken.None);

        var problem = ResultAssertions.AssertValidationProblem(result);
        Assert.Contains("contentType", problem.Errors.Keys);
        Assert.Equal(0, storage.CallCount);
    }

    /// <summary>0バイトのmultipart動画をS3へ渡さず400にすることを確認します。</summary>
    [Fact(DisplayName = "multipart API: 空ファイルを400で拒否する")]
    public async Task UploadMultipartAsync_EmptyFile_DoesNotUpload()
    {
        var file = CreateFormFile([], "sample.mp4", "video/mp4");
        var storage = new RecordingVideoStorage();

        var result = await VideoUploadHandlers.UploadMultipartAsync(
            file,
            storage,
            Options,
            CancellationToken.None);

        var problem = ResultAssertions.AssertValidationProblem(result);
        Assert.Contains("file", problem.Errors.Keys);
        Assert.Equal(0, storage.CallCount);
    }

    /// <summary>通常のBase64 JSONをデコードしてストレージへ正しいメタデータとともに渡すことを確認します。</summary>
    [Fact(DisplayName = "Base64 API: 通常Base64を保存して結果を返す")]
    public async Task UploadBase64Async_RawBase64_UploadsAndReturnsOk()
    {
        var storage = new RecordingVideoStorage();
        var request = new Base64VideoUploadRequest
        {
            FileName = "sample.mp4",
            ContentType = "video/mp4",
            Base64Data = "AQIDBA=="
        };

        var result = await VideoUploadHandlers.UploadBase64Async(
            request,
            storage,
            Options,
            CancellationToken.None);

        var response = ResultAssertions.AssertOk<VideoUploadResponse>(result);
        Assert.Equal([1, 2, 3, 4], storage.Bytes);
        Assert.Equal("sample.mp4", storage.OriginalFileName);
        Assert.Equal("video/mp4", storage.ContentType);
        Assert.Equal(4, response.Size);
    }

    /// <summary>Content-Typeを省略してもData URI内のメディアタイプを採用できることを確認します。</summary>
    [Fact(DisplayName = "Base64 API: Data URIからContent-Typeを補完する")]
    public async Task UploadBase64Async_DataUriWithoutExplicitContentType_UsesEmbeddedType()
    {
        var storage = new RecordingVideoStorage();
        var request = new Base64VideoUploadRequest
        {
            FileName = "sample.mp4",
            Base64Data = "data:video/mp4;base64,AQIDBA=="
        };

        var result = await VideoUploadHandlers.UploadBase64Async(
            request,
            storage,
            Options,
            CancellationToken.None);

        ResultAssertions.AssertOk<VideoUploadResponse>(result);
        Assert.Equal("video/mp4", storage.ContentType);
        Assert.Equal(1, storage.CallCount);
    }

    /// <summary>JSONとData URIのContent-Typeが不一致なら曖昧なデータをS3へ送らず400にすることを確認します。</summary>
    [Fact(DisplayName = "Base64 API: Content-Type不一致を400で拒否する")]
    public async Task UploadBase64Async_ContentTypesMismatch_DoesNotUpload()
    {
        var storage = new RecordingVideoStorage();
        var request = new Base64VideoUploadRequest
        {
            FileName = "sample.mp4",
            ContentType = "video/webm",
            Base64Data = "data:video/mp4;base64,AQIDBA=="
        };

        var result = await VideoUploadHandlers.UploadBase64Async(
            request,
            storage,
            Options,
            CancellationToken.None);

        var problem = ResultAssertions.AssertValidationProblem(result);
        Assert.Contains("contentType", problem.Errors.Keys);
        Assert.Equal(0, storage.CallCount);
    }

    /// <summary>fileNameがないJSONをBase64デコードやS3保存へ進めず400にすることを確認します。</summary>
    [Fact(DisplayName = "Base64 API: fileName未入力を400で拒否する")]
    public async Task UploadBase64Async_MissingFileName_DoesNotUpload()
    {
        var storage = new RecordingVideoStorage();
        var request = new Base64VideoUploadRequest
        {
            ContentType = "video/mp4",
            Base64Data = "AQIDBA=="
        };

        var result = await VideoUploadHandlers.UploadBase64Async(
            request,
            storage,
            Options,
            CancellationToken.None);

        var problem = ResultAssertions.AssertValidationProblem(result);
        Assert.Contains("fileName", problem.Errors.Keys);
        Assert.Equal(0, storage.CallCount);
    }

    /// <summary>不正Base64を例外にせず400として返し、S3保存を呼ばないことを確認します。</summary>
    [Fact(DisplayName = "Base64 API: 不正Base64を400で拒否する")]
    public async Task UploadBase64Async_InvalidBase64_DoesNotUpload()
    {
        var storage = new RecordingVideoStorage();
        var request = new Base64VideoUploadRequest
        {
            FileName = "sample.mp4",
            ContentType = "video/mp4",
            Base64Data = "invalid"
        };

        var result = await VideoUploadHandlers.UploadBase64Async(
            request,
            storage,
            Options,
            CancellationToken.None);

        var problem = ResultAssertions.AssertValidationProblem(result);
        Assert.Contains("base64Data", problem.Errors.Keys);
        Assert.Equal(0, storage.CallCount);
    }

    /// <summary>Data URIも明示Content-Typeもない場合を必須エラーとして400にすることを確認します。</summary>
    [Fact(DisplayName = "Base64 API: Content-Type未入力を400で拒否する")]
    public async Task UploadBase64Async_MissingContentType_DoesNotUpload()
    {
        var storage = new RecordingVideoStorage();
        var request = new Base64VideoUploadRequest
        {
            FileName = "sample.mp4",
            Base64Data = "AQIDBA=="
        };

        var result = await VideoUploadHandlers.UploadBase64Async(
            request,
            storage,
            Options,
            CancellationToken.None);

        var problem = ResultAssertions.AssertValidationProblem(result);
        Assert.Contains("contentType", problem.Errors.Keys);
        Assert.Equal(0, storage.CallCount);
    }

    private static IFormFile CreateFormFile(byte[] bytes, string fileName, string contentType) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
}
