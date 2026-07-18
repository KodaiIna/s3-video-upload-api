using S3VideoUploadApi.Infrastructure;

namespace S3VideoUploadApi.Tests;

public sealed class RequestSizeLimitsTests
{
    /// <summary>通常の値ではリクエストオーバーヘッドを単純加算することを確認します。</summary>
    [Fact(DisplayName = "リクエスト上限: 通常値を加算する")]
    public void AddSaturating_NormalValues_ReturnsSum()
    {
        Assert.Equal(150, RequestSizeLimits.AddSaturating(100, 50));
    }

    /// <summary>longの上限を超える加算でもオーバーフローせずlong.MaxValueに丸めることを確認します。</summary>
    [Fact(DisplayName = "リクエスト上限: オーバーフロー時はlong.MaxValueに丸める")]
    public void AddSaturating_Overflow_ReturnsLongMaxValue()
    {
        Assert.Equal(long.MaxValue, RequestSizeLimits.AddSaturating(long.MaxValue - 1, 2));
    }
}
