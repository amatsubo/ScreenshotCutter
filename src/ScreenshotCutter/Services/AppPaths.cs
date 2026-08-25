using System.IO;
namespace ScreenshotCutter.Services;

/// <summary>
/// アプリが使うパスの集約。設定・ログはいずれも exe と同じフォルダーに置く
/// （コピーのみで動作させるポータブル要件。確定仕様書 2章・5.1・7章）。
/// </summary>
public static class AppPaths
{
    public const string AppName = "ScreenshotCutter";

    /// <summary>exe が置かれているフォルダー。</summary>
    public static string BaseDirectory { get; } =
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public static string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

    /// <summary>設定ファイルが壊れていたときの退避先。</summary>
    public static string SettingsBackupFile => Path.Combine(BaseDirectory, "settings.json.bak");

    public static string LogDirectory => Path.Combine(BaseDirectory, "logs");

    /// <summary>実行中の exe のフルパス。自動起動の登録に使う。</summary>
    public static string ExecutablePath { get; } =
        Environment.ProcessPath ?? Path.Combine(BaseDirectory, AppName + ".exe");
}
