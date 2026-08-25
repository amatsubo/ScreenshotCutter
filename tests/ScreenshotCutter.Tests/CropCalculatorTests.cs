using ScreenshotCutter.Models;
using ScreenshotCutter.Services;

namespace ScreenshotCutter.Tests;

/// <summary>
/// 切り出し矩形の補正計算（確定仕様書 4.6.2）。
/// </summary>
public class CropCalculatorTests
{
    // ------------------------------------------------------------- Clamp

    [Fact]
    public void Clamp_収まっている矩形はそのまま返す()
    {
        var rect = new PixelRect(243, 32, 2208, 1344);

        var result = CropCalculator.Clamp(rect, 2560, 1440);

        Assert.Equal(rect, result);
    }

    [Fact]
    public void Clamp_右下にはみ出す場合は位置を内側へ寄せる()
    {
        // 幅・高さはモニターに収まるので、サイズは変えずに位置だけ動かす。
        var rect = new PixelRect(2000, 1200, 1000, 400);

        var result = CropCalculator.Clamp(rect, 2560, 1440);

        Assert.Equal(new PixelRect(1560, 1040, 1000, 400), result);
    }

    [Fact]
    public void Clamp_負の座標は原点側へ寄せる()
    {
        var rect = new PixelRect(-100, -50, 800, 600);

        var result = CropCalculator.Clamp(rect, 2560, 1440);

        Assert.Equal(new PixelRect(0, 0, 800, 600), result);
    }

    [Fact]
    public void Clamp_位置を動かしても収まらない場合にだけサイズを縮める()
    {
        // 解像度が 2560x1440 から 1920x1080 に変わったケース。
        var rect = new PixelRect(243, 32, 2208, 1344);

        var result = CropCalculator.Clamp(rect, 1920, 1080);

        // 幅・高さがモニターを超えるため、モニターいっぱいまで縮めて原点に置く。
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), result);
    }

    [Fact]
    public void Clamp_幅だけが超過する場合は高さを保つ()
    {
        var rect = new PixelRect(100, 100, 3000, 500);

        var result = CropCalculator.Clamp(rect, 2560, 1440);

        Assert.Equal(new PixelRect(0, 100, 2560, 500), result);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    public void Clamp_サイズが0以下の矩形は空を返す(int width, int height)
    {
        var result = CropCalculator.Clamp(new PixelRect(10, 10, width, height), 2560, 1440);

        Assert.Equal(PixelRect.Empty, result);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Clamp_モニターサイズが不正なら空を返す()
    {
        var result = CropCalculator.Clamp(new PixelRect(0, 0, 100, 100), 0, 0);

        Assert.Equal(PixelRect.Empty, result);
    }

    [Fact]
    public void Clamp_モニターと同じサイズの矩形はそのまま通る()
    {
        var rect = new PixelRect(0, 0, 2560, 1440);

        var result = CropCalculator.Clamp(rect, 2560, 1440);

        Assert.Equal(rect, result);
    }

    // --------------------------------------------------------------- Fit

    [Fact]
    public void Fit_はみ出した分を切り詰め位置は動かさない()
    {
        // ドラッグ中に枠が勝手に移動すると操作感が壊れるため、
        // Clamp と違って位置は維持する。
        var rect = new PixelRect(2000, 1200, 1000, 400);

        var result = CropCalculator.Fit(rect, 2560, 1440);

        Assert.Equal(new PixelRect(2000, 1200, 560, 240), result);
    }

    [Fact]
    public void Fit_左上にはみ出した場合は原点で切り詰める()
    {
        var rect = new PixelRect(-100, -100, 500, 500);

        var result = CropCalculator.Fit(rect, 2560, 1440);

        Assert.Equal(new PixelRect(0, 0, 400, 400), result);
    }

    [Fact]
    public void Fit_最小サイズを下回らない()
    {
        var result = CropCalculator.Fit(new PixelRect(100, 100, 0, 0), 2560, 1440);

        Assert.Equal(CropCalculator.MinimumSize, result.Width);
        Assert.Equal(CropCalculator.MinimumSize, result.Height);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Fit_右端に接する矩形は最小サイズを保って収まる()
    {
        var result = CropCalculator.Fit(new PixelRect(2559, 1439, 100, 100), 2560, 1440);

        Assert.Equal(new PixelRect(2559, 1439, 1, 1), result);
    }

    // ------------------------------------------------------------ Offset

    [Fact]
    public void Offset_指定量だけ平行移動する()
    {
        var result = CropCalculator.Offset(new PixelRect(100, 100, 400, 300), 10, -20, 2560, 1440);

        Assert.Equal(new PixelRect(110, 80, 400, 300), result);
    }

    [Fact]
    public void Offset_画面外へは出ずサイズも変わらない()
    {
        var result = CropCalculator.Offset(new PixelRect(2100, 1100, 400, 300), 500, 500, 2560, 1440);

        Assert.Equal(new PixelRect(2160, 1140, 400, 300), result);
    }

    [Fact]
    public void Offset_原点より手前へは動かない()
    {
        var result = CropCalculator.Offset(new PixelRect(10, 10, 400, 300), -100, -100, 2560, 1440);

        Assert.Equal(new PixelRect(0, 0, 400, 300), result);
    }

    // ---------------------------------------------------------- IsWithin

    [Theory]
    [InlineData(0, 0, 2560, 1440, true)]
    [InlineData(243, 32, 2208, 1344, true)]
    [InlineData(-1, 0, 100, 100, false)]
    [InlineData(0, -1, 100, 100, false)]
    [InlineData(2500, 0, 100, 100, false)]
    [InlineData(0, 1400, 100, 100, false)]
    [InlineData(0, 0, 0, 0, false)]
    public void IsWithin_範囲内判定(int x, int y, int width, int height, bool expected)
    {
        var result = CropCalculator.IsWithin(new PixelRect(x, y, width, height), 2560, 1440);

        Assert.Equal(expected, result);
    }

    // --------------------------------------------------------- PixelRect

    [Fact]
    public void FromPoints_点の前後関係によらず正の矩形を作る()
    {
        var forward = PixelRect.FromPoints(100, 200, 400, 500);
        var backward = PixelRect.FromPoints(400, 500, 100, 200);

        Assert.Equal(new PixelRect(100, 200, 300, 300), forward);
        Assert.Equal(forward, backward);
    }

    [Fact]
    public void FromPoints_同一点は幅も高さも0になる()
    {
        var result = PixelRect.FromPoints(50, 50, 50, 50);

        Assert.Equal(new PixelRect(50, 50, 0, 0), result);
        Assert.False(result.IsValid);
    }
}
