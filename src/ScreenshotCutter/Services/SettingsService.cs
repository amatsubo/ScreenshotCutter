using System.IO;
using System.Text;
using System.Text.Json;
using ScreenshotCutter.Models;

namespace ScreenshotCutter.Services;

/// <summary>設定ファイルの読み込み結果。</summary>
public enum SettingsLoadStatus
{
    /// <summary>既存の設定を正常に読み込んだ。</summary>
    Loaded,

    /// <summary>設定ファイルが無かったため既定値を作成した（初回起動）。</summary>
    CreatedDefault,

    /// <summary>設定ファイルが壊れていたため退避し、既定値で復旧した。</summary>
    RecoveredFromCorruption,
}

/// <summary>
/// exe と同じフォルダーの settings.json を読み書きする（確定仕様書 5章）。
/// </summary>
public sealed class SettingsService
{
    private readonly string _settingsPath;
    private readonly string _backupPath;

    public SettingsService()
        : this(AppPaths.SettingsFile, AppPaths.SettingsBackupFile)
    {
    }

    /// <summary>テストから任意のパスを差し込むためのコンストラクター。</summary>
    public SettingsService(string settingsPath, string backupPath)
    {
        _settingsPath = settingsPath;
        _backupPath = backupPath;
    }

    /// <summary>直近の読み込み結果。</summary>
    public SettingsLoadStatus Status { get; private set; } = SettingsLoadStatus.CreatedDefault;

    /// <summary>
    /// 設定を読み込む。ファイルが無い場合は既定値を作成し、
    /// 壊れている場合は .bak へ退避したうえで既定値を返す（確定仕様書 5.3）。
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            Status = SettingsLoadStatus.CreatedDefault;
            var defaults = CreateDefault();

            // 初回起動時にファイルを作っておく。書き込めない場所に置かれている
            // 場合は失敗するが、起動自体は継続する（確定仕様書 5.3）。
            try
            {
                Save(defaults);
            }
            catch (Exception ex)
            {
                Logger.Error($"設定ファイルの新規作成に失敗しました。: {_settingsPath}", ex);
            }

            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

            if (loaded is null)
            {
                // 中身が "null" だけ、あるいは空のファイル。
                throw new JsonException("設定ファイルの内容が空です。");
            }

            Status = SettingsLoadStatus.Loaded;
            Normalize(loaded);
            return loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Logger.Error($"設定ファイルの読み込みに失敗しました。既定値で復旧します。: {_settingsPath}", ex);
            BackupCorruptedFile();

            Status = SettingsLoadStatus.RecoveredFromCorruption;
            return CreateDefault();
        }
    }

    /// <summary>
    /// 設定を保存する。書き込みに失敗した場合は例外を投げるので、
    /// 呼び出し側でユーザーへ提示する（確定仕様書 5.3・8章）。
    /// </summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

        // 書き込み中にプロセスが落ちても既存の設定を失わないよう、
        // 一時ファイルへ書いてから置き換える。
        var tempPath = _settingsPath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            // 一時ファイルが残ること自体は動作に影響しない。
            Logger.Error($"一時ファイルの削除に失敗しました。: {tempPath}", ex);
        }
    }

    private void BackupCorruptedFile()
    {
        try
        {
            File.Copy(_settingsPath, _backupPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error($"壊れた設定ファイルの退避に失敗しました。: {_backupPath}", ex);
        }
    }

    private static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.Output.Folder = OutputSettings.DefaultFolder;
        return settings;
    }

    /// <summary>
    /// 手で編集された設定ファイルに備えて、null や不正値を既定値へ寄せる。
    /// JSON に明示的な null が書かれているとプロパティが null になり、
    /// そのままでは実行時に落ちるため。
    /// </summary>
    private static void Normalize(AppSettings settings)
    {
        settings.Hotkey ??= new HotkeySettings();
        settings.Capture ??= new CaptureSettings();
        settings.Output ??= new OutputSettings();
        settings.Notification ??= new NotificationSettings();
        settings.Startup ??= new StartupSettings();

        settings.Hotkey.Modifiers ??= [];
        settings.Hotkey.Key ??= string.Empty;

        settings.Capture.MonitorId ??= string.Empty;
        settings.Capture.MonitorDeviceName ??= string.Empty;
        settings.Capture.MonitorFriendlyName ??= string.Empty;
        settings.Capture.Crop ??= new CropSettings();

        // 負のサイズは切り出し不能なため無効化する。
        if (settings.Capture.Crop.Width < 0 || settings.Capture.Crop.Height < 0)
        {
            settings.Capture.Crop.Width = 0;
            settings.Capture.Crop.Height = 0;
            settings.Capture.Crop.Enabled = false;
        }

        settings.Output.Folder ??= string.Empty;

        if (string.IsNullOrWhiteSpace(settings.Output.FileNameTemplate))
        {
            settings.Output.FileNameTemplate = OutputSettings.DefaultFileNameTemplate;
        }

        // 保存もクリップボードも無効だと撮影しても何も残らないため、
        // 少なくとも片方は有効にする（確定仕様書 4.8.3）。
        if (!settings.Output.SaveToFile && !settings.Output.CopyToClipboard)
        {
            settings.Output.SaveToFile = true;
        }
    }
}
