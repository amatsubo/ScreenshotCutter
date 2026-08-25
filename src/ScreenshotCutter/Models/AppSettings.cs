using System.IO;
using System.Text.Json.Serialization;

namespace ScreenshotCutter.Models;

/// <summary>
/// settings.json のルート。既定値は確定仕様書 5.2 に準拠する。
/// </summary>
public sealed class AppSettings
{
    /// <summary>将来のマイグレーション用のスキーマ版数。</summary>
    public int Version { get; set; } = 1;

    public HotkeySettings Hotkey { get; set; } = new();

    public CaptureSettings Capture { get; set; } = new();

    public OutputSettings Output { get; set; } = new();

    public NotificationSettings Notification { get; set; } = new();

    public StartupSettings Startup { get; set; } = new();

    /// <summary>設定ウィンドウのキャンセル用に、値の完全なコピーを作る。</summary>
    public AppSettings Clone() => new()
    {
        Version = Version,
        Hotkey = Hotkey.Clone(),
        Capture = Capture.Clone(),
        Output = Output.Clone(),
        Notification = Notification.Clone(),
        Startup = Startup.Clone(),
    };
}

public sealed class HotkeySettings
{
    /// <summary>"Ctrl" / "Alt" / "Shift" / "Win" の組み合わせ。</summary>
    public List<string> Modifiers { get; set; } = ["Ctrl", "Alt"];

    /// <summary><see cref="System.Windows.Input.Key"/> の名前。</summary>
    public string Key { get; set; } = "S";

    public HotkeySettings Clone() => new()
    {
        Modifiers = [.. Modifiers],
        Key = Key,
    };
}

public sealed class CaptureSettings
{
    /// <summary>
    /// 対象モニターの識別子（主キー）。EDID 由来のデバイスパスから生成する。
    /// 空文字はプライマリモニターを指す（初回起動時）。
    /// </summary>
    public string MonitorId { get; set; } = string.Empty;

    /// <summary>GDI のデバイス名（例: DISPLAY1）。主キーで一致しない場合の補助キー。</summary>
    public string MonitorDeviceName { get; set; } = string.Empty;

    /// <summary>設定画面での表示専用。識別には使用しない。</summary>
    public string MonitorFriendlyName { get; set; } = string.Empty;

    public CropSettings Crop { get; set; } = new();

    public CaptureSettings Clone() => new()
    {
        MonitorId = MonitorId,
        MonitorDeviceName = MonitorDeviceName,
        MonitorFriendlyName = MonitorFriendlyName,
        Crop = Crop.Clone(),
    };
}

public sealed class CropSettings
{
    /// <summary>
    /// false のときはモニター全体をそのまま出力する（確定仕様書 4.6.1.2）。
    /// 初回起動時は矩形が未設定のため false。
    /// </summary>
    public bool Enabled { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    [JsonIgnore]
    public PixelRect Rect
    {
        get => new(X, Y, Width, Height);
        set
        {
            X = value.X;
            Y = value.Y;
            Width = value.Width;
            Height = value.Height;
        }
    }

    /// <summary>
    /// 実際に切り出しを行うか。Enabled が true でも矩形が潰れている場合は
    /// 切り出さない（設定ファイルを手で壊された場合の保険）。
    /// </summary>
    [JsonIgnore]
    public bool IsEffective => Enabled && Rect.IsValid;

    public CropSettings Clone() => new()
    {
        Enabled = Enabled,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
    };
}

public sealed class OutputSettings
{
    /// <summary>空文字の場合は <see cref="DefaultFolder"/> を使う。</summary>
    public string Folder { get; set; } = string.Empty;

    public string FileNameTemplate { get; set; } = DefaultFileNameTemplate;

    public bool SaveToFile { get; set; } = true;

    public bool CopyToClipboard { get; set; } = true;

    public const string DefaultFileNameTemplate = "ScreenShot_{yyyyMMdd}_{HHmmss}";

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "ScreenshotCutter");

    /// <summary>設定値が空なら既定フォルダーにフォールバックした保存先。</summary>
    [JsonIgnore]
    public string EffectiveFolder =>
        string.IsNullOrWhiteSpace(Folder) ? DefaultFolder : Folder;

    public OutputSettings Clone() => new()
    {
        Folder = Folder,
        FileNameTemplate = FileNameTemplate,
        SaveToFile = SaveToFile,
        CopyToClipboard = CopyToClipboard,
    };
}

public sealed class NotificationSettings
{
    public bool Toast { get; set; } = true;

    public bool ShutterSound { get; set; }

    public NotificationSettings Clone() => new()
    {
        Toast = Toast,
        ShutterSound = ShutterSound,
    };
}

public sealed class StartupSettings
{
    public bool RunAtLogon { get; set; }

    public StartupSettings Clone() => new()
    {
        RunAtLogon = RunAtLogon,
    };
}
