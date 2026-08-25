using System.Text;
using ScreenshotCutter.Models;
using ScreenshotCutter.Services;

namespace ScreenshotCutter.Tests;

/// <summary>
/// settings.json の読み書きと異常系（確定仕様書 5章）。
/// </summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _folder;
    private readonly string _settingsPath;
    private readonly string _backupPath;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "ScreenshotCutterSettings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        _settingsPath = Path.Combine(_folder, "settings.json");
        _backupPath = Path.Combine(_folder, "settings.json.bak");
        _service = new SettingsService(_settingsPath, _backupPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ------------------------------------------------------------ 初回起動

    [Fact]
    public void ファイルが無い場合は既定値を作成する()
    {
        var settings = _service.Load();

        Assert.Equal(SettingsLoadStatus.CreatedDefault, _service.Status);
        Assert.True(File.Exists(_settingsPath));
        Assert.Equal(1, settings.Version);
    }

    [Fact]
    public void 既定値は仕様どおりの内容になっている()
    {
        var settings = _service.Load();

        Assert.Equal(["Ctrl", "Alt"], settings.Hotkey.Modifiers);
        Assert.Equal("S", settings.Hotkey.Key);
        Assert.True(settings.Output.SaveToFile);
        Assert.True(settings.Output.CopyToClipboard);
        Assert.Equal(OutputSettings.DefaultFileNameTemplate, settings.Output.FileNameTemplate);
        Assert.True(settings.Notification.Toast);
        Assert.False(settings.Notification.ShutterSound);
        Assert.False(settings.Startup.RunAtLogon);

        // 矩形が未設定のため、初期状態では切り出しは無効。
        Assert.False(settings.Capture.Crop.Enabled);
        Assert.False(settings.Capture.Crop.IsEffective);
    }

    // -------------------------------------------------------- 保存と再読込

    [Fact]
    public void 保存した内容を読み戻せる()
    {
        var settings = _service.Load();
        settings.Capture.MonitorId = "DISPLAY#ACM1234#5&1a2b3c4d&0&UID256";
        settings.Capture.MonitorDeviceName = "DISPLAY1";
        settings.Capture.MonitorFriendlyName = "Sample Monitor A";
        settings.Capture.Crop.Enabled = true;
        settings.Capture.Crop.Rect = new PixelRect(243, 32, 2208, 1344);
        settings.Output.FileNameTemplate = "cap-{seq:000}";
        settings.Notification.ShutterSound = true;

        _service.Save(settings);

        var reloaded = new SettingsService(_settingsPath, _backupPath).Load();

        Assert.Equal("DISPLAY#ACM1234#5&1a2b3c4d&0&UID256", reloaded.Capture.MonitorId);
        Assert.Equal("DISPLAY1", reloaded.Capture.MonitorDeviceName);
        Assert.Equal("Sample Monitor A", reloaded.Capture.MonitorFriendlyName);
        Assert.Equal(new PixelRect(243, 32, 2208, 1344), reloaded.Capture.Crop.Rect);
        Assert.True(reloaded.Capture.Crop.IsEffective);
        Assert.Equal("cap-{seq:000}", reloaded.Output.FileNameTemplate);
        Assert.True(reloaded.Notification.ShutterSound);
    }

    [Fact]
    public void 保存されるJSONはBOM無しのUTF8になる()
    {
        _service.Save(_service.Load());

        var bytes = File.ReadAllBytes(_settingsPath);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void 保存されるJSONのキーはキャメルケースになる()
    {
        _service.Save(_service.Load());

        var json = File.ReadAllText(_settingsPath, Encoding.UTF8);

        Assert.Contains("\"fileNameTemplate\"", json, StringComparison.Ordinal);
        Assert.Contains("\"copyToClipboard\"", json, StringComparison.Ordinal);
        Assert.Contains("\"runAtLogon\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 一時ファイルは保存後に残らない()
    {
        _service.Save(_service.Load());

        Assert.False(File.Exists(_settingsPath + ".tmp"));
    }

    // ---------------------------------------------------------- 破損時の復旧

    [Fact]
    public void 壊れたJSONはbakへ退避して既定値で復旧する()
    {
        File.WriteAllText(_settingsPath, "{ this is not valid json", Encoding.UTF8);

        var settings = _service.Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruption, _service.Status);
        Assert.True(File.Exists(_backupPath));
        Assert.Equal("S", settings.Hotkey.Key);
    }

    [Fact]
    public void 退避したbakには元の内容が残る()
    {
        const string broken = "{ broken";
        File.WriteAllText(_settingsPath, broken, Encoding.UTF8);

        _service.Load();

        Assert.Equal(broken, File.ReadAllText(_backupPath, Encoding.UTF8));
    }

    [Fact]
    public void 空のファイルも破損として扱う()
    {
        File.WriteAllText(_settingsPath, string.Empty, Encoding.UTF8);

        _service.Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruption, _service.Status);
    }

    [Fact]
    public void nullだけのファイルも破損として扱う()
    {
        File.WriteAllText(_settingsPath, "null", Encoding.UTF8);

        _service.Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruption, _service.Status);
    }

    // ------------------------------------------------------- 手編集への耐性

    [Fact]
    public void セクションがnullでも既定値で補われる()
    {
        File.WriteAllText(
            _settingsPath,
            """
            {
              "version": 1,
              "hotkey": null,
              "capture": null,
              "output": null,
              "notification": null,
              "startup": null
            }
            """,
            Encoding.UTF8);

        var settings = _service.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, _service.Status);
        Assert.NotNull(settings.Hotkey);
        Assert.NotNull(settings.Capture);
        Assert.NotNull(settings.Output);
        Assert.NotNull(settings.Notification);
        Assert.NotNull(settings.Startup);
        Assert.Equal("S", settings.Hotkey.Key);
    }

    [Fact]
    public void 負のサイズの矩形は無効化される()
    {
        File.WriteAllText(
            _settingsPath,
            """
            {
              "version": 1,
              "capture": { "crop": { "enabled": true, "x": 0, "y": 0, "width": -100, "height": -50 } }
            }
            """,
            Encoding.UTF8);

        var settings = _service.Load();

        Assert.False(settings.Capture.Crop.Enabled);
        Assert.False(settings.Capture.Crop.IsEffective);
    }

    [Fact]
    public void 保存とクリップボードが両方falseなら保存を有効に戻す()
    {
        // 撮影しても何も残らない設定は成立しない（確定仕様書 4.8.3）。
        File.WriteAllText(
            _settingsPath,
            """
            {
              "version": 1,
              "output": { "saveToFile": false, "copyToClipboard": false }
            }
            """,
            Encoding.UTF8);

        var settings = _service.Load();

        Assert.True(settings.Output.SaveToFile);
    }

    [Fact]
    public void ファイル名テンプレートが空なら既定値に戻す()
    {
        File.WriteAllText(
            _settingsPath,
            """
            { "version": 1, "output": { "fileNameTemplate": "  " } }
            """,
            Encoding.UTF8);

        var settings = _service.Load();

        Assert.Equal(OutputSettings.DefaultFileNameTemplate, settings.Output.FileNameTemplate);
    }

    [Fact]
    public void 保存先が空なら既定フォルダーを使う()
    {
        File.WriteAllText(
            _settingsPath,
            """
            { "version": 1, "output": { "folder": "" } }
            """,
            Encoding.UTF8);

        var settings = _service.Load();

        Assert.Equal(OutputSettings.DefaultFolder, settings.Output.EffectiveFolder);
    }

    // -------------------------------------------------------------- Clone

    [Fact]
    public void Cloneは元の設定と独立している()
    {
        var original = _service.Load();
        var clone = original.Clone();

        clone.Output.FileNameTemplate = "changed";
        clone.Capture.Crop.Rect = new PixelRect(1, 2, 3, 4);
        clone.Hotkey.Modifiers.Add("Shift");

        Assert.Equal(OutputSettings.DefaultFileNameTemplate, original.Output.FileNameTemplate);
        Assert.Equal(PixelRect.Empty, original.Capture.Crop.Rect);
        Assert.Equal(["Ctrl", "Alt"], original.Hotkey.Modifiers);
    }

    [Fact]
    public void 書き込みできないパスでは保存が例外になる()
    {
        // 確定仕様書 5.3: 起動はするが、保存時にエラーを出す。
        var invalidPath = Path.Combine(_folder, "no-such-dir\0invalid", "settings.json");
        var service = new SettingsService(invalidPath, invalidPath + ".bak");

        Assert.ThrowsAny<Exception>(() => service.Save(new AppSettings()));
    }
}
