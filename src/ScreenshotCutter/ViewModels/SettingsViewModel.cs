using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using ScreenshotCutter.Models;
using ScreenshotCutter.Services;

namespace ScreenshotCutter.ViewModels;

/// <summary>
/// 設定ウィンドウの状態（確定仕様書 4.3）。
/// </summary>
/// <remarks>
/// 変更は OK / 適用 を押すまで確定しないため、現在の設定から複製した値を
/// 保持し、確定時に <see cref="BuildSettings"/> で組み立て直す。
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MonitorService _monitorService;

    /// <summary>設定の読み込み中は、値の変更に連動する補助動作を抑止する。</summary>
    private bool _isLoading;

    [ObservableProperty]
    private MonitorInfo? _selectedMonitor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CropSummary))]
    private bool _cropEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CropSummary))]
    private PixelRect _cropRect;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileNamePreview))]
    [NotifyPropertyChangedFor(nameof(FileNameWarning))]
    [NotifyPropertyChangedFor(nameof(HasFileNameWarning))]
    private string _fileNameTemplate = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleSaveToFile))]
    [NotifyPropertyChangedFor(nameof(CanToggleClipboard))]
    private bool _saveToFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleSaveToFile))]
    [NotifyPropertyChangedFor(nameof(CanToggleClipboard))]
    private bool _copyToClipboard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private bool _modifierCtrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private bool _modifierAlt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private bool _modifierShift;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private bool _modifierWin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private Key _selectedKey = Key.S;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHotkeyError))]
    private string? _hotkeyError;

    [ObservableProperty]
    private bool _toastEnabled;

    [ObservableProperty]
    private bool _shutterSoundEnabled;

    [ObservableProperty]
    private bool _runAtLogon;

    public SettingsViewModel(AppSettings settings, MonitorService monitorService, string? hotkeyError)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitorService);

        _monitorService = monitorService;

        Monitors = [];
        SelectableKeys = [.. HotkeyParser.SelectableKeys.Select(k => new KeyOption(k, DescribeKey(k)))];

        LoadFrom(settings);
        HotkeyError = hotkeyError;
    }

    public ObservableCollection<MonitorInfo> Monitors { get; }

    /// <summary>ホットキーの主キー候補。ComboBox にそのまま流し込む。</summary>
    public IReadOnlyList<KeyOption> SelectableKeys { get; }

    /// <summary>ComboBox 用のキー候補。</summary>
    /// <param name="Key">仮想キー。</param>
    /// <param name="Display">画面に出す名前。</param>
    public sealed record KeyOption(Key Key, string Display);

    /// <summary>現在の切り出し矩形の説明文。</summary>
    public string CropSummary
    {
        get
        {
            if (!CropRect.IsValid)
            {
                return "未設定（モニター全体を保存します）";
            }

            return $"X: {CropRect.X}   Y: {CropRect.Y}   幅: {CropRect.Width}   高さ: {CropRect.Height}";
        }
    }

    /// <summary>
    /// 保存とクリップボードの両方が OFF になるのを防ぐ（確定仕様書 4.8.3）。
    /// 片方しか有効でない場合、その片方のチェックは外せない。
    /// </summary>
    public bool CanToggleSaveToFile => CopyToClipboard || !SaveToFile;

    public bool CanToggleClipboard => SaveToFile || !CopyToClipboard;

    /// <summary>現在のテンプレートで生成されるファイル名の例。</summary>
    /// <remarks>
    /// 同名のプロパティ <see cref="FileNameTemplate"/> がヘルパークラスを隠すため、
    /// ここでは名前空間を明示して呼び分ける。
    /// </remarks>
    public string FileNamePreview
    {
        get
        {
            var expanded = Services.FileNameTemplate.Expand(FileNameTemplate, DateTime.Now, sequence: 1);
            var sanitized = Services.FileNameTemplate.Sanitize(expanded);
            return sanitized + ImageOutputService.Extension;
        }
    }

    public string? FileNameWarning =>
        Services.FileNameTemplate.TryValidate(FileNameTemplate, out var warning) ? null : warning;

    public bool HasFileNameWarning => FileNameWarning is not null;

    public string HotkeyDisplay => HotkeyParser.Format(BuildHotkeySettings());

    public bool HasHotkeyError => !string.IsNullOrEmpty(HotkeyError);

    /// <summary>接続中のモニターを読み直す。</summary>
    public void RefreshMonitors()
    {
        var previousId = SelectedMonitor?.Id;
        var previousDevice = SelectedMonitor?.ShortDeviceName;

        Monitors.Clear();

        foreach (var monitor in _monitorService.Enumerate())
        {
            Monitors.Add(monitor);
        }

        // 選択を可能な限り維持する。
        SelectedMonitor =
            Monitors.FirstOrDefault(m => !string.IsNullOrEmpty(previousId) && m.Id == previousId)
            ?? Monitors.FirstOrDefault(m => m.ShortDeviceName == previousDevice)
            ?? MonitorService.FindPrimary(Monitors);
    }

    /// <summary>設定値をビューモデルへ読み込む。</summary>
    public void LoadFrom(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 読み込み中は「矩形が入ったら切り出しを有効化する」補助動作を止める。
        // これが働くと、矩形を保存したまま切り出しを無効にしている設定が
        // 画面を開くたびに勝手に有効へ戻ってしまう。
        _isLoading = true;

        try
        {
            CropRect = settings.Capture.Crop.Rect;
            CropEnabled = settings.Capture.Crop.Enabled;
        }
        finally
        {
            _isLoading = false;
        }

        OutputFolder = settings.Output.EffectiveFolder;
        FileNameTemplate = settings.Output.FileNameTemplate;
        SaveToFile = settings.Output.SaveToFile;
        CopyToClipboard = settings.Output.CopyToClipboard;

        ModifierCtrl = HasModifier(settings.Hotkey, "Ctrl");
        ModifierAlt = HasModifier(settings.Hotkey, "Alt");
        ModifierShift = HasModifier(settings.Hotkey, "Shift");
        ModifierWin = HasModifier(settings.Hotkey, "Win");

        SelectedKey = Enum.TryParse<Key>(settings.Hotkey.Key, ignoreCase: true, out var key)
            ? key
            : Key.S;

        ToastEnabled = settings.Notification.Toast;
        ShutterSoundEnabled = settings.Notification.ShutterSound;
        RunAtLogon = settings.Startup.RunAtLogon;

        RefreshMonitors();

        // 保存済みの対象モニターを選択状態にする。
        var resolved = MonitorService.Resolve(settings.Capture, Monitors);
        if (resolved is not null)
        {
            SelectedMonitor = resolved;
        }
    }

    /// <summary>ビューモデルの状態から設定オブジェクトを組み立てる。</summary>
    public AppSettings BuildSettings(AppSettings baseSettings)
    {
        ArgumentNullException.ThrowIfNull(baseSettings);

        var settings = baseSettings.Clone();

        settings.Hotkey = BuildHotkeySettings();

        if (SelectedMonitor is not null)
        {
            settings.Capture.MonitorId = SelectedMonitor.Id;
            settings.Capture.MonitorDeviceName = SelectedMonitor.ShortDeviceName;
            settings.Capture.MonitorFriendlyName = SelectedMonitor.FriendlyName;
        }

        // 矩形が未設定のまま切り出しを有効にはできない。
        settings.Capture.Crop.Enabled = CropEnabled && CropRect.IsValid;
        settings.Capture.Crop.Rect = CropRect;

        settings.Output.Folder = OutputFolder;
        settings.Output.FileNameTemplate = string.IsNullOrWhiteSpace(FileNameTemplate)
            ? OutputSettings.DefaultFileNameTemplate
            : FileNameTemplate;
        settings.Output.SaveToFile = SaveToFile;
        settings.Output.CopyToClipboard = CopyToClipboard;

        settings.Notification.Toast = ToastEnabled;
        settings.Notification.ShutterSound = ShutterSoundEnabled;

        settings.Startup.RunAtLogon = RunAtLogon;

        return settings;
    }

    private HotkeySettings BuildHotkeySettings()
    {
        var modifiers = new List<string>();

        if (ModifierCtrl) { modifiers.Add("Ctrl"); }
        if (ModifierAlt) { modifiers.Add("Alt"); }
        if (ModifierShift) { modifiers.Add("Shift"); }
        if (ModifierWin) { modifiers.Add("Win"); }

        return new HotkeySettings
        {
            Modifiers = modifiers,
            Key = SelectedKey.ToString(),
        };
    }

    /// <summary>
    /// 確定前に、明らかな入力ミスを検出する。
    /// 問題がなければ null を返す。
    /// </summary>
    public string? Validate()
    {
        if (SelectedMonitor is null)
        {
            return "撮影対象のモニターを選択してください。";
        }

        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            return "保存先フォルダーを指定してください。";
        }

        if (OutputFolder.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
        {
            return "保存先フォルダーのパスに使用できない文字が含まれています。";
        }

        if (!HotkeyParser.TryParse(BuildHotkeySettings(), out _, out _, out var hotkeyError))
        {
            return hotkeyError;
        }

        return null;
    }

    private static bool HasModifier(HotkeySettings hotkey, string name) =>
        hotkey.Modifiers.Any(m => string.Equals(m?.Trim(), name, StringComparison.OrdinalIgnoreCase));

    partial void OnSaveToFileChanged(bool value)
    {
        // 両方 OFF になる操作は受け付けない（UI 側でも無効化しているが二重に守る）。
        if (!value && !CopyToClipboard)
        {
            CopyToClipboard = true;
        }
    }

    partial void OnCopyToClipboardChanged(bool value)
    {
        if (!value && !SaveToFile)
        {
            SaveToFile = true;
        }
    }

    partial void OnCropRectChanged(PixelRect value)
    {
        // オーバーレイで矩形を確定したときは、そのまま使えるよう有効化する。
        // ただし設定の読み込み中は、保存されている有効/無効をそのまま尊重する。
        if (!_isLoading && value.IsValid && !CropEnabled)
        {
            CropEnabled = true;
        }
    }

    /// <summary>ファイル名プレビューを再計算させる（時刻が進むため）。</summary>
    public void RefreshFileNamePreview()
    {
        OnPropertyChanged(nameof(FileNamePreview));
    }

    /// <summary>キーの表示名。ComboBox の項目に使う。</summary>
    public static string DescribeKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture),
        Key.OemTilde => "` (半角/全角の左)",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.PrintScreen => "PrintScreen",
        Key.Enter => "Enter",
        Key.Space => "Space",
        _ => key.ToString(),
    };
}
