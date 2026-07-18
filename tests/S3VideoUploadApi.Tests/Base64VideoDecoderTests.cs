using S3VideoUploadApi.Infrastructure;

namespace S3VideoUploadApi.Tests;

public sealed class Base64VideoDecoderTests
{
    /// <summary>通常のBase64文字列を元のバイト列へ復元できることを確認します。</summary>
    [Fact(DisplayName = "Base64: 通常文字列をデコードできる")]
    public void Decode_RawBase64_ReturnsBytes()
    {
        var result = Base64VideoDecoder.Decode("AQIDBA==", 100);

        Assert.True(result.IsSuccess);
        Assert.Equal([1, 2, 3, 4], result.Bytes);
        Assert.Null(result.EmbeddedContentType);
    }

    /// <summary>Data URIから動画のメディアタイプとバイト列を抽出できることを確認します。</summary>
    [Fact(DisplayName = "Base64: Data URIからContent-Typeも抽出できる")]
    public void Decode_DataUri_ReturnsEmbeddedContentType()
    {
        var result = Base64VideoDecoder.Decode("data:video/mp4;base64,AQIDBA==", 100);

        Assert.True(result.IsSuccess);
        Assert.Equal([1, 2, 3, 4], result.Bytes);
        Assert.Equal("video/mp4", result.EmbeddedContentType);
    }

    /// <summary>charset等のパラメーターを含むData URIでも先頭のメディアタイプを取得できることを確認します。</summary>
    [Fact(DisplayName = "Base64: パラメーター付きData URIを処理できる")]
    public void Decode_DataUriWithParameters_ReturnsMediaTypeOnly()
    {
        var result = Base64VideoDecoder.Decode(
            "data:video/mp4;charset=utf-8;base64,AQ==",
            100);

        Assert.True(result.IsSuccess);
        Assert.Equal("video/mp4", result.EmbeddedContentType);
    }

    /// <summary>改行や空白を含むBase64もConvert.FromBase64Stringと同じ仕様で処理できることを確認します。</summary>
    [Fact(DisplayName = "Base64: 改行と空白を無視してデコードできる")]
    public void Decode_Base64WithWhitespace_ReturnsBytes()
    {
        var result = Base64VideoDecoder.Decode("AQID\r\n BA==", 100);

        Assert.True(result.IsSuccess);
        Assert.Equal([1, 2, 3, 4], result.Bytes);
    }

    /// <summary>null・空文字・空白だけの入力を必須エラーとして扱うことを確認します。</summary>
    [Theory(DisplayName = "Base64: 未入力を拒否する")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decode_MissingValue_ReturnsRequiredError(string? value)
    {
        var result = Base64VideoDecoder.Decode(value, 100);

        Assert.False(result.IsSuccess);
        Assert.Equal("base64Data is required.", result.Error);
    }

    /// <summary>カンマのないData URIを不正な形式として拒否することを確認します。</summary>
    [Fact(DisplayName = "Base64: カンマのないData URIを拒否する")]
    public void Decode_DataUriWithoutComma_ReturnsMalformedError()
    {
        var result = Base64VideoDecoder.Decode("data:video/mp4;base64", 100);

        Assert.False(result.IsSuccess);
        Assert.Equal("The data URI is malformed.", result.Error);
    }

    /// <summary>Base64指定がないData URIを拒否し、URLエンコード文字列を誤処理しないことを確認します。</summary>
    [Fact(DisplayName = "Base64: base64指定のないData URIを拒否する")]
    public void Decode_NonBase64DataUri_ReturnsError()
    {
        var result = Base64VideoDecoder.Decode("data:video/mp4,AQID", 100);

        Assert.False(result.IsSuccess);
        Assert.Equal("The data URI must contain base64 data.", result.Error);
    }

    /// <summary>Base64として不正な文字列をFormatExceptionとして外へ漏らさず検証エラーにすることを確認します。</summary>
    [Fact(DisplayName = "Base64: 不正な文字列を検証エラーにする")]
    public void Decode_InvalidBase64_ReturnsValidationError()
    {
        var result = Base64VideoDecoder.Decode("not-base64", 100);

        Assert.False(result.IsSuccess);
        Assert.Equal("base64Data is not valid Base64.", result.Error);
    }

    /// <summary>Data URIのデータ部分が空の場合に空動画として拒否することを確認します。</summary>
    [Fact(DisplayName = "Base64: デコード結果が0バイトなら拒否する")]
    public void Decode_EmptyDecodedData_ReturnsError()
    {
        var result = Base64VideoDecoder.Decode("data:video/mp4;base64,", 100);

        Assert.False(result.IsSuccess);
        Assert.Equal("The decoded video is empty.", result.Error);
    }

    /// <summary>明らかに上限を超えるBase64をデコード前の文字数検査で拒否することを確認します。</summary>
    [Fact(DisplayName = "Base64: 上限超過をデコード前に拒否する")]
    public void Decode_EncodedLengthOverLimit_IsRejectedBeforeDecoding()
    {
        var result = Base64VideoDecoder.Decode("AQIDBA==", 3);

        Assert.False(result.IsSuccess);
        Assert.Contains("must not exceed 3 bytes", result.Error);
    }

    /// <summary>Base64のパディングを考慮し、文字数上限内でも復元後に超過するデータを拒否することを確認します。</summary>
    [Fact(DisplayName = "Base64: 復元後サイズの上限超過も拒否する")]
    public void Decode_DecodedLengthOverLimit_IsRejectedAfterDecoding()
    {
        var result = Base64VideoDecoder.Decode("AQI=", 1);

        Assert.False(result.IsSuccess);
        Assert.Contains("must not exceed 1 bytes", result.Error);
    }

    /// <summary>最大サイズと同じ1バイトのデータは正常に受け付ける境界値を確認します。</summary>
    [Fact(DisplayName = "Base64: 最大サイズちょうどのデータを許可する")]
    public void Decode_ExactlyAtLimit_Succeeds()
    {
        var result = Base64VideoDecoder.Decode("AQ==", 1);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Bytes!);
    }

    /// <summary>Base64文字列の最大長計算が0・通常値・オーバーフロー領域で安全に動くことを確認します。</summary>
    [Theory(DisplayName = "Base64: 最大エンコード長を安全に計算する")]
    [InlineData(0L, 0L)]
    [InlineData(-1L, 0L)]
    [InlineData(1L, 4L)]
    [InlineData(3L, 4L)]
    [InlineData(4L, 8L)]
    [InlineData(long.MaxValue, long.MaxValue)]
    public void GetMaximumEncodedLength_ReturnsExpectedValue(long input, long expected)
    {
        Assert.Equal(expected, Base64VideoDecoder.GetMaximumEncodedLength(input));
    }
}
