using S3VideoUploadApi.Storage;

namespace S3VideoUploadApi.Tests;

public sealed class S3ObjectKeyFactoryTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Id =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>プレフィックス・UTC日付・GUID・Content-Type由来拡張子でキーを生成することを確認します。</summary>
    [Fact(DisplayName = "S3キー: 規定フォーマットで生成する")]
    public void Create_UsesPrefixDateGuidAndContentTypeExtension()
    {
        var key = S3ObjectKeyFactory.Create(
            "/videos/",
            "ignored.exe",
            "video/mp4",
            Timestamp,
            Id);

        Assert.Equal(
            "videos/2026/07/18/11111111222233334444555555555555.mp4",
            key);
    }

    /// <summary>プレフィックスが空でもキー先頭に余分なスラッシュが付かないことを確認します。</summary>
    [Fact(DisplayName = "S3キー: 空プレフィックスを処理する")]
    public void Create_EmptyPrefix_DoesNotAddLeadingSlash()
    {
        var key = S3ObjectKeyFactory.Create("", "sample.webm", "video/webm", Timestamp, Id);

        Assert.Equal(
            "2026/07/18/11111111222233334444555555555555.webm",
            key);
    }

    /// <summary>既知Content-Typeでは危険な元拡張子を無視して安全な拡張子を使うことを確認します。</summary>
    [Fact(DisplayName = "S3キー: 既知Content-Typeを元ファイル拡張子より優先する")]
    public void Create_KnownContentType_IgnoresUntrustedOriginalExtension()
    {
        var key = S3ObjectKeyFactory.Create("videos", "attack.html", "video/quicktime", Timestamp, Id);

        Assert.EndsWith(".mov", key);
    }

    /// <summary>追加許可した未知Content-Typeでは英数字だけの短い拡張子を保持することを確認します。</summary>
    [Fact(DisplayName = "S3キー: 未知Content-Typeでも安全な拡張子は保持する")]
    public void Create_UnknownContentTypeWithSafeExtension_KeepsExtension()
    {
        var key = S3ObjectKeyFactory.Create("videos", "sample.custom1", "video/custom", Timestamp, Id);

        Assert.EndsWith(".custom1", key);
    }

    /// <summary>長すぎる、または記号を含む拡張子を.binへ置き換えることを確認します。</summary>
    [Theory(DisplayName = "S3キー: 危険な拡張子を.binへ置換する")]
    [InlineData("sample.verylongext")]
    [InlineData("sample.bad-1")]
    [InlineData("sample")]
    public void Create_UnknownContentTypeWithUnsafeExtension_UsesBin(string fileName)
    {
        var key = S3ObjectKeyFactory.Create("videos", fileName, "video/custom", Timestamp, Id);

        Assert.EndsWith(".bin", key);
    }
}
