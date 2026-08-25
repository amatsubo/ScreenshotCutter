using ScreenshotCutter.Models;

namespace ScreenshotCutter.Services;

/// <summary>
/// 切り出し矩形の補正計算。副作用を持たない純粋関数のみを置く。
/// </summary>
public static class CropCalculator
{
    /// <summary>切り出し矩形として許容する最小サイズ（確定仕様書 4.6.2.1）。</summary>
    public const int MinimumSize = 1;

    /// <summary>
    /// 矩形をモニターの範囲内に収める。
    /// まず位置を内側へ寄せ、それでも収まらない場合にサイズを縮める
    /// （確定仕様書 4.6.2.4）。解像度比でのスケーリングは行わない。
    /// </summary>
    public static PixelRect Clamp(PixelRect rect, int monitorWidth, int monitorHeight)
    {
        if (monitorWidth < MinimumSize || monitorHeight < MinimumSize)
        {
            return PixelRect.Empty;
        }

        if (rect.Width < MinimumSize || rect.Height < MinimumSize)
        {
            return PixelRect.Empty;
        }

        // サイズがモニターを超えている場合のみ縮める。
        var width = Math.Min(rect.Width, monitorWidth);
        var height = Math.Min(rect.Height, monitorHeight);

        // 位置はモニター内へ移動させる。上で縮めていれば必ず 0 に寄る。
        var x = Math.Clamp(rect.X, 0, monitorWidth - width);
        var y = Math.Clamp(rect.Y, 0, monitorHeight - height);

        return new PixelRect(x, y, width, height);
    }

    /// <summary>
    /// ドラッグや数値入力の途中経過を、モニター内の有効な矩形へ丸める。
    /// <see cref="Clamp"/> と違い、はみ出した分は「切り詰める」（位置は動かさない）。
    /// 枠を引いている最中に枠が勝手に移動すると操作感が壊れるため。
    /// </summary>
    public static PixelRect Fit(PixelRect rect, int monitorWidth, int monitorHeight)
    {
        if (monitorWidth < MinimumSize || monitorHeight < MinimumSize)
        {
            return PixelRect.Empty;
        }

        var left = Math.Clamp(rect.X, 0, monitorWidth - MinimumSize);
        var top = Math.Clamp(rect.Y, 0, monitorHeight - MinimumSize);
        var right = Math.Clamp(rect.Right, left + MinimumSize, monitorWidth);
        var bottom = Math.Clamp(rect.Bottom, top + MinimumSize, monitorHeight);

        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>矩形をモニター内に収まる範囲で平行移動する。</summary>
    public static PixelRect Offset(PixelRect rect, int dx, int dy, int monitorWidth, int monitorHeight)
    {
        var moved = rect with { X = rect.X + dx, Y = rect.Y + dy };

        var maxX = Math.Max(0, monitorWidth - rect.Width);
        var maxY = Math.Max(0, monitorHeight - rect.Height);

        return moved with
        {
            X = Math.Clamp(moved.X, 0, maxX),
            Y = Math.Clamp(moved.Y, 0, maxY),
        };
    }

    /// <summary>矩形がモニターの範囲内に完全に収まっているか。</summary>
    public static bool IsWithin(PixelRect rect, int monitorWidth, int monitorHeight) =>
        rect.IsValid
        && rect.X >= 0
        && rect.Y >= 0
        && rect.Right <= monitorWidth
        && rect.Bottom <= monitorHeight;
}
