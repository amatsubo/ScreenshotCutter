namespace ScreenshotCutter.Models;

/// <summary>
/// 列挙されたモニター 1 台の情報。座標・サイズはすべて物理ピクセル。
/// </summary>
public sealed class MonitorInfo
{
    /// <summary>設定画面に表示する 1 始まりの通し番号。</summary>
    public required int Index { get; init; }

    /// <summary>
    /// EDID 由来の安定した識別子（主キー）。
    /// 取得できなかった場合は空文字になり、その場合は補助キーで照合する。
    /// </summary>
    public required string Id { get; init; }

    /// <summary>GDI のデバイス名（例: <c>\\.\DISPLAY1</c>）。API 呼び出しに使う。</summary>
    public required string DeviceName { get; init; }

    /// <summary>設定ファイルに保存する短縮形のデバイス名（例: <c>DISPLAY1</c>）。</summary>
    public string ShortDeviceName =>
        DeviceName.StartsWith(@"\\.\", StringComparison.Ordinal) ? DeviceName[4..] : DeviceName;

    /// <summary>表示用の名称（例: <c>LG ULTRAGEAR</c>）。識別には使わない。</summary>
    public required string FriendlyName { get; init; }

    /// <summary>仮想デスクトップ座標での配置。負値を取りうる。</summary>
    public required PixelRect VirtualBounds { get; init; }

    public required bool IsPrimary { get; init; }

    /// <summary>このモニターの DPI（96 = 100%）。</summary>
    public required uint Dpi { get; init; }

    public int Width => VirtualBounds.Width;

    public int Height => VirtualBounds.Height;

    /// <summary>WPF の論理単位へ変換する際の倍率（100% なら 1.0）。</summary>
    public double ScaleFactor => Dpi / 96.0;

    /// <summary>設定画面のモニター一覧に出す 1 行分の表示文字列。</summary>
    public string DisplayLabel
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(FriendlyName) ? ShortDeviceName : FriendlyName;
            var primary = IsPrimary ? "  [プライマリ]" : string.Empty;
            var scale = Dpi == 96 ? string.Empty : $"  {Math.Round(ScaleFactor * 100)}%";
            return $"{Index}. {name}  {Width}x{Height}{scale}{primary}";
        }
    }

    public override string ToString() => DisplayLabel;
}
