using S3VideoUploadApi.Configuration;
using S3VideoUploadApi.Infrastructure;

namespace S3VideoUploadApi.Tests;

public sealed class VideoUploadValidatorTests
{
    private readonly S3Options _options = new()
    {
        BucketName = "test-bucket",
        MaxFileSizeBytes = 10,
        AllowedContentTypes = S3Options.CreateDefaultAllowedContentTypes()
    };

    /// <summary>許可形式かつ最大サイズちょうどの動画がエラーなしになることを確認します。</summary>
    [Fact(DisplayName = "入力検証: 正常な動画を許可する")]
    public void Validate_ValidVideo_ReturnsNoErrors()
    {
        var errors = VideoUploadValidator.Validate("sample.mp4", "video/mp4", 10, _options);

        Assert.Empty(errors);
    }

    /// <summary>Content-Typeの大文字小文字は区別せず許可リストと照合することを確認します。</summary>
    [Fact(DisplayName = "入力検証: Content-Typeを大文字小文字非依存で照合する")]
    public void Validate_ContentTypeCaseDiffers_ReturnsNoErrors()
    {
        var errors = VideoUploadValidator.Validate("sample.mp4", "VIDEO/MP4", 1, _options);

        Assert.Empty(errors);
    }

    /// <summary>ファイル名がない入力をfileNameエラーとして返すことを確認します。</summary>
    [Fact(DisplayName = "入力検証: ファイル名の未入力を拒否する")]
    public void Validate_MissingFileName_ReturnsError()
    {
        var errors = VideoUploadValidator.Validate("", "video/mp4", 1, _options);

        Assert.Equal("A file name is required.", Assert.Single(errors["fileName"]));
    }

    /// <summary>Content-Typeがない入力をcontentTypeエラーとして返すことを確認します。</summary>
    [Fact(DisplayName = "入力検証: Content-Typeの未入力を拒否する")]
    public void Validate_MissingContentType_ReturnsError()
    {
        var errors = VideoUploadValidator.Validate("sample.mp4", "", 1, _options);

        Assert.Equal("A content type is required.", Assert.Single(errors["contentType"]));
    }

    /// <summary>許可リストにないContent-Typeを拒否し、許可値をメッセージに含めることを確認します。</summary>
    [Fact(DisplayName = "入力検証: 未許可Content-Typeを拒否する")]
    public void Validate_UnsupportedContentType_ReturnsError()
    {
        var errors = VideoUploadValidator.Validate("sample.txt", "text/plain", 5, _options);

        Assert.Contains("Allowed values", Assert.Single(errors["contentType"]));
    }

    /// <summary>0バイトおよび負数の長さを空動画として拒否することを確認します。</summary>
    [Theory(DisplayName = "入力検証: 0バイト以下の動画を拒否する")]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Validate_EmptyOrNegativeLength_ReturnsError(long length)
    {
        var errors = VideoUploadValidator.Validate("sample.mp4", "video/mp4", length, _options);

        Assert.Equal("The video is empty.", Assert.Single(errors["file"]));
    }

    /// <summary>設定した最大サイズを1バイトでも超えた動画を拒否することを確認します。</summary>
    [Fact(DisplayName = "入力検証: 最大サイズ超過を拒否する")]
    public void Validate_OverLimit_ReturnsError()
    {
        var errors = VideoUploadValidator.Validate("sample.mp4", "video/mp4", 11, _options);

        Assert.Contains("must not exceed 10 bytes", Assert.Single(errors["file"]));
    }

    /// <summary>ブラウザが付けるWindows形式のfakepathを除去し、安全なファイル名だけを残すことを確認します。</summary>
    [Fact(DisplayName = "正規化: Windows形式のクライアントパスを除去する")]
    public void NormalizeFileName_RemovesWindowsClientPath()
    {
        var result = VideoUploadValidator.NormalizeFileName(@"C:\fakepath\sample.mp4");

        Assert.Equal("sample.mp4", result);
    }

    /// <summary>Unix形式のパスや前後空白も除去してファイル名だけを取得することを確認します。</summary>
    [Fact(DisplayName = "正規化: Unix形式のパスと空白を除去する")]
    public void NormalizeFileName_RemovesUnixPathAndWhitespace()
    {
        var result = VideoUploadValidator.NormalizeFileName(" /tmp/sample.webm ");

        Assert.Equal("sample.webm", result);
    }

    /// <summary>null・空文字・空白だけのファイル名を空文字に統一することを確認します。</summary>
    [Theory(DisplayName = "正規化: 未入力ファイル名を空文字にする")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeFileName_MissingValue_ReturnsEmpty(string? value)
    {
        Assert.Equal(string.Empty, VideoUploadValidator.NormalizeFileName(value));
    }

    /// <summary>Content-Typeを小文字化し、charset等のパラメーターを除去することを確認します。</summary>
    [Fact(DisplayName = "正規化: Content-Typeのパラメーターを除去する")]
    public void NormalizeContentType_RemovesParametersAndLowercases()
    {
        var result = VideoUploadValidator.NormalizeContentType(" Video/MP4; charset=utf-8 ");

        Assert.Equal("video/mp4", result);
    }

    /// <summary>null・空文字・空白だけのContent-Typeを空文字に統一することを確認します。</summary>
    [Theory(DisplayName = "正規化: 未入力Content-Typeを空文字にする")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeContentType_MissingValue_ReturnsEmpty(string? value)
    {
        Assert.Equal(string.Empty, VideoUploadValidator.NormalizeContentType(value));
    }
}
