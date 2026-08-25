using System.Diagnostics;
using System.IO;
using System.Windows;
using ScreenshotCutter.Interop;
using ScreenshotCutter.Models;
using ScreenshotCutter.Views;

namespace ScreenshotCutter.Services;

/// <summary>
/// アプリ全体の調整役。常駐に必要な部品をまとめて保持し、
/// トレイ操作とホットキーからの入口を提供する。
/// </summary>
public sealed class AppController : IDisposable
{
    /// <summary>
    /// 多重起動時に、既存インスタンスへ設定画面の表示を依頼するための
    /// ブロードキャストメッセージ名（確定仕様書 4.1.2）。
    /// </summary>
    public const string ShowSettingsMessageName = "ScreenshotCutter_ShowSettings";

    private readonly SettingsService _settingsService = new();
    private readonly MonitorService _monitorService = new();
    private readonly ScreenCaptureService _captureService = new();
    private readonly ImageOutputService _outputService = new();

    private readonly uint _showSettingsMessage;

    private MessageWindow? _messageWindow;
    private TrayIcon? _trayIcon;
    private HotkeyService? _hotkeyService;
    private NotificationService? _notificationService;
    private ScreenshotWorkflow? _workflow;
    private SettingsWindow? _settingsWindow;

    private AppSettings _settings = new();
    private bool _disposed;

    public AppController()
    {
        _showSettingsMessage = NativeMethods.RegisterWindowMessage(ShowSettingsMessageName);
    }

    /// <summary>現在の設定。設定画面へは複製を渡すこと。</summary>
    public AppSettings Settings => _settings;

    public MonitorService MonitorService => _monitorService;

    public ScreenCaptureService CaptureService => _captureService;

    /// <summary>ホットキー登録に失敗している場合の理由。成功時は null。</summary>
    public string? HotkeyError => _hotkeyService?.LastError;

    /// <summary>常駐を開始する。</summary>
    public void Start()
    {
        // exe を移動された場合に自動起動の登録パスを直す（確定仕様書 4.10.3）。
        AutoStartService.SyncRegisteredPath();

        _settings = _settingsService.Load();

        _messageWindow = new MessageWindow(AppPaths.AppName);
        _messageWindow.MessageHandler = HandleWindowMessage;

        _trayIcon = new TrayIcon(_messageWindow.Handle, AppPaths.AppName);
        _trayIcon.DoubleClicked += ShowSettings;
        _trayIcon.MenuCommandInvoked += HandleMenuCommand;
        _trayIcon.MenuStateProvider = BuildMenuState;
        _trayIcon.Show();

        _notificationService = new NotificationService(_trayIcon, () => _settings.Notification);

        _workflow = new ScreenshotWorkflow(
            _monitorService,
            _captureService,
            _outputService,
            _notificationService,
            () => _settings);

        _hotkeyService = new HotkeyService(_messageWindow.Handle);
        _hotkeyService.Pressed += CaptureNow;

        if (!_hotkeyService.Register(_settings.Hotkey))
        {
            // 起動時に気づけるよう通知する（確定仕様書 4.4.6）。
            _notificationService.NotifyError(_hotkeyService.LastError ?? "ホットキーを登録できませんでした。");
        }

        switch (_settingsService.Status)
        {
            case SettingsLoadStatus.CreatedDefault:
                // 初回起動は設定画面を開く（確定仕様書 4.1.5）。
                ShowSettings();
                break;

            case SettingsLoadStatus.RecoveredFromCorruption:
                _notificationService.NotifyError(
                    "設定ファイルを読み込めなかったため、既定値で起動しました。元のファイルは settings.json.bak に退避しています。");
                break;
        }

        // 起動処理で確保した一時オブジェクトを待機前に手放す。
        MemoryTrimmer.Trim();
    }

    private IntPtr? HandleWindowMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        // 2 個目のインスタンスからの依頼で設定画面を前面に出す。
        if (message == _showSettingsMessage && _showSettingsMessage != 0)
        {
            ShowSettings();
            return IntPtr.Zero;
        }

        if (_hotkeyService?.HandleMessage(message, wParam) == true)
        {
            return IntPtr.Zero;
        }

        if (_trayIcon?.HandleMessage(message, wParam, lParam) == true)
        {
            return IntPtr.Zero;
        }

        return null;
    }

    private TrayMenuState BuildMenuState() => new(
        CropEnabled: _settings.Capture.Crop.Enabled,
        ClipboardEnabled: _settings.Output.CopyToClipboard,

        // 保存が無効なときにクリップボードまで切ると何も残らなくなる
        // （確定仕様書 4.8.3）。
        CanToggleClipboard: _settings.Output.SaveToFile);

    private void HandleMenuCommand(TrayMenuCommand command)
    {
        switch (command)
        {
            case TrayMenuCommand.CaptureNow:
                CaptureNow();
                break;

            case TrayMenuCommand.ToggleCrop:
                ToggleAndPersist(s => s.Capture.Crop.Enabled = !s.Capture.Crop.Enabled);
                break;

            case TrayMenuCommand.ToggleClipboard:
                ToggleAndPersist(s => s.Output.CopyToClipboard = !s.Output.CopyToClipboard);
                break;

            case TrayMenuCommand.OpenOutputFolder:
                OpenOutputFolder();
                break;

            case TrayMenuCommand.OpenSettings:
                ShowSettings();
                break;

            case TrayMenuCommand.Exit:
                Exit();
                break;
        }
    }

    /// <summary>
    /// トレイのトグルは即時反映・即保存する（設定画面の OK/適用とは独立）。
    /// </summary>
    private void ToggleAndPersist(Action<AppSettings> mutate)
    {
        mutate(_settings);

        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            Logger.Error("設定の保存に失敗しました。", ex);
            _notificationService?.NotifyError($"設定の保存に失敗しました。: {ex.Message}");
        }

        // 設定画面が開いている場合、表示が古いままにならないよう反映する。
        _settingsWindow?.ReloadFromSettings(_settings);
    }

    /// <summary>撮影を実行する（ホットキー／トレイメニュー共通の入口）。</summary>
    public void CaptureNow() => _workflow?.Execute();

    /// <summary>
    /// 設定を保存して反映する。致命的な失敗があればメッセージを返す。
    /// ホットキーの登録失敗は <see cref="HotkeyError"/> で取得する。
    /// </summary>
    public string? ApplySettings(AppSettings updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        try
        {
            _settingsService.Save(updated);
        }
        catch (Exception ex)
        {
            Logger.Error($"設定の保存に失敗しました。: {AppPaths.SettingsFile}", ex);

            // 書き込み不可のフォルダーに置かれている場合はここに来る
            // （確定仕様書 5.3）。
            return $"設定を保存できませんでした。{AppPaths.BaseDirectory} への書き込み権限を確認してください。\n\n{ex.Message}";
        }

        var previousRunAtLogon = _settings.Startup.RunAtLogon;
        _settings = updated;

        string? autoStartError = null;

        if (previousRunAtLogon != updated.Startup.RunAtLogon)
        {
            try
            {
                AutoStartService.SetEnabled(updated.Startup.RunAtLogon);
            }
            catch (Exception ex)
            {
                Logger.Error("自動起動の設定に失敗しました。", ex);
                autoStartError = $"自動起動の設定に失敗しました。: {ex.Message}";
            }
        }

        // ホットキーは設定確定のタイミングで登録し直す。
        _hotkeyService?.Register(updated.Hotkey);

        return autoStartError;
    }

    /// <summary>設定ウィンドウを開く。既に開いている場合は前面に出す。</summary>
    public void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        // 使うまで生成しない。閉じたら破棄してメモリを返す（確定仕様書 6.4）。
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            MemoryTrimmer.Trim();
        };

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void OpenOutputFolder()
    {
        var folder = _settings.Output.EffectiveFolder;

        try
        {
            Directory.CreateDirectory(folder);

            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            })?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error($"保存先フォルダーを開けませんでした。: {folder}", ex);
            _notificationService?.NotifyError($"保存先フォルダーを開けませんでした。: {ex.Message}");
        }
    }

    public void Exit() => Application.Current.Shutdown();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _messageWindow?.Dispose();
    }
}
