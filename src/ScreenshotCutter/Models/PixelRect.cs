namespace ScreenshotCutter.Models;

/// <summary>
/// 物理ピクセル単位の矩形。
/// 切り出し矩形は対象モニターの左上を (0,0) とするモニター相対座標で扱う
/// （確定仕様書 3章）。仮想デスクトップ座標は負値を取りうるため、
/// そのまま保存するとモニター配置の変更で矩形がずれる。
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect Empty => new(0, 0, 0, 0);

    public int Right => X + Width;

    public int Bottom => Y + Height;

    /// <summary>幅・高さがともに 1 以上か。</summary>
    public bool IsValid => Width > 0 && Height > 0;

    /// <summary>2 点から矩形を作る。点の前後関係は問わない。</summary>
    public static PixelRect FromPoints(int x1, int y1, int x2, int y2)
    {
        var left = Math.Min(x1, x2);
        var top = Math.Min(y1, y2);
        return new PixelRect(left, top, Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    public override string ToString() => $"({X}, {Y}) {Width}x{Height}";
}
