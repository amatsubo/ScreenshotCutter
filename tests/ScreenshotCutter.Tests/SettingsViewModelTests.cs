using ScreenshotCutter.Models;
using ScreenshotCutter.Services;
using ScreenshotCutter.ViewModels;

namespace ScreenshotCutter.Tests;

/// <summary>
/// 設定ウィンドウの状態管理（確定仕様書 4.3・4.8.3）。
/// </summary>
/// <remarks>
/// <see cref="SettingsViewModel"/> は生成時にモニターを列挙するため、
/// 実行環境のモニター構成に依存する。ここではモニターの内容に依存しない
/// 振る舞いだけを検証する。
/// </remarks>
public class SettingsViewModelTests
{
    private static SettingsViewModel Create(AppSettings settings) =>
        new(settings, new MonitorService(), hotkeyError: null);

    private static SettingsViewModel CreateDefault() => Create(new AppSettings());

    private static AppSettings SettingsWithCrop(bool enabled, PixelRect rect)
    {
        var settings = new AppSettings();
        settings.Capture.Crop.Enabled = enabled;
        settings.Capture.Crop.Rect = rect;
        return settings;
    }

    // ------------------------------------------------------- 切り出しの状態

    [Fact]
    public void 矩形を保存済みでも切り出し無効の設定はそのまま保たれる()
    {
        // 回帰テスト: 読み込み時に「矩形があれば有効化する」補助動作が働くと、
        // 設定画面を開くたびに無効設定が勝手に有効へ戻ってしまう。
        var settings = SettingsWithCrop(enabled: false, new PixelRect(243, 32, 2208, 1344));

        var viewModel = Create(settings);

        Assert.False(viewModel.CropEnabled);
        Assert.Equal(new PixelRect(243, 32, 2208, 1344), viewModel.CropRect);
    }

    [Fact]
    public void 保存済みの有効設定はそのまま読み込まれる()
    {
        var settings = SettingsWithCrop(enabled: true, new PixelRect(10, 20, 300, 200));

        var viewModel = Create(settings);

        Assert.True(viewModel.CropEnabled);
        Assert.Equal(new PixelRect(10, 20, 300, 200), viewModel.CropRect);
    }

    [Fact]
    public void 読み込み直後に再読込しても無効設定は維持される()
    {
        var viewModel = CreateDefault();

        viewModel.LoadFrom(SettingsWithCrop(enabled: false, new PixelRect(1, 2, 300, 200)));

        Assert.False(viewModel.CropEnabled);
    }

    [Fact]
    public void 操作で矩形を設定したときは切り出しが有効になる()
    {
        // オーバーレイで確定した直後は、そのまま使える状態にしたい。
        var viewModel = CreateDefault();

        Assert.False(viewModel.CropEnabled);

        viewModel.CropRect = new PixelRect(100, 100, 800, 600);

        Assert.True(viewModel.CropEnabled);
    }

    [Fact]
    public void 矩形が無効なままでは切り出しを有効にして保存できない()
    {
        var viewModel = CreateDefault();
        viewModel.CropEnabled = true;

        var built = viewModel.BuildSettings(new AppSettings());

        Assert.False(built.Capture.Crop.Enabled);
    }

    // --------------------------------------------- 保存とクリップボードの排他

    [Fact]
    public void 両方有効ならどちらも切り替えられる()
    {
        var viewModel = CreateDefault();

        Assert.True(viewModel.SaveToFile);
        Assert.True(viewModel.CopyToClipboard);
        Assert.True(viewModel.CanToggleSaveToFile);
        Assert.True(viewModel.CanToggleClipboard);
    }

    [Fact]
    public void クリップボードが無効ならファイル保存は切れない()
    {
        var viewModel = CreateDefault();
        viewModel.CopyToClipboard = false;

        Assert.False(viewModel.CanToggleSaveToFile);
        Assert.True(viewModel.CanToggleClipboard);
    }

    [Fact]
    public void ファイル保存が無効ならクリップボードは切れない()
    {
        var settings = new AppSettings();
        settings.Output.SaveToFile = false;

        var viewModel = Create(settings);

        Assert.False(viewModel.CanToggleClipboard);
        Assert.True(viewModel.CanToggleSaveToFile);
    }

    [Fact]
    public void 両方を無効にしようとしても片方は有効に戻る()
    {
        // UI 側でも無効化しているが、二重に守る（確定仕様書 4.8.3）。
        var viewModel = CreateDefault();
        viewModel.CopyToClipboard = false;

        viewModel.SaveToFile = false;

        Assert.True(viewModel.SaveToFile || viewModel.CopyToClipboard);
    }

    // ------------------------------------------------------------ 検証

    [Fact]
    public void 保存先が空なら検証で弾く()
    {
        var viewModel = CreateDefault();
        viewModel.OutputFolder = "   ";

        Assert.NotNull(viewModel.Validate());
    }

    [Fact]
    public void 修飾キーが無ければ検証で弾く()
    {
        var viewModel = CreateDefault();
        viewModel.ModifierCtrl = false;
        viewModel.ModifierAlt = false;
        viewModel.ModifierShift = false;
        viewModel.ModifierWin = false;

        Assert.NotNull(viewModel.Validate());
    }

    // -------------------------------------------------------- BuildSettings

    [Fact]
    public void ホットキーの内容が設定へ反映される()
    {
        var viewModel = CreateDefault();
        viewModel.ModifierCtrl = true;
        viewModel.ModifierAlt = false;
        viewModel.ModifierShift = true;
        viewModel.ModifierWin = false;
        viewModel.SelectedKey = System.Windows.Input.Key.F9;

        var built = viewModel.BuildSettings(new AppSettings());

        Assert.Equal(["Ctrl", "Shift"], built.Hotkey.Modifiers);
        Assert.Equal("F9", built.Hotkey.Key);
    }

    [Fact]
    public void ファイル名が空なら既定のテンプレートで保存される()
    {
        var viewModel = CreateDefault();
        viewModel.FileNameTemplate = "   ";

        var built = viewModel.BuildSettings(new AppSettings());

        Assert.Equal(OutputSettings.DefaultFileNameTemplate, built.Output.FileNameTemplate);
    }

    [Fact]
    public void 通知と自動起動の設定が反映される()
    {
        var viewModel = CreateDefault();
        viewModel.ToastEnabled = false;
        viewModel.ShutterSoundEnabled = true;
        viewModel.RunAtLogon = true;

        var built = viewModel.BuildSettings(new AppSettings());

        Assert.False(built.Notification.Toast);
        Assert.True(built.Notification.ShutterSound);
        Assert.True(built.Startup.RunAtLogon);
    }

    [Fact]
    public void BuildSettingsは元の設定を書き換えない()
    {
        var original = new AppSettings();
        var viewModel = CreateDefault();
        viewModel.FileNameTemplate = "changed-{seq}";

        var built = viewModel.BuildSettings(original);

        Assert.Equal(OutputSettings.DefaultFileNameTemplate, original.Output.FileNameTemplate);
        Assert.Equal("changed-{seq}", built.Output.FileNameTemplate);
    }

    // ---------------------------------------------------------- 表示用の値

    [Fact]
    public void ファイル名プレビューに拡張子が付く()
    {
        var viewModel = CreateDefault();

        Assert.EndsWith(".png", viewModel.FileNamePreview, StringComparison.Ordinal);
    }

    [Fact]
    public void 使用できない文字を含むファイル名は警告される()
    {
        var viewModel = CreateDefault();
        viewModel.FileNameTemplate = "a:b";

        Assert.True(viewModel.HasFileNameWarning);
        Assert.NotNull(viewModel.FileNameWarning);
    }

    [Fact]
    public void 正しいファイル名は警告されない()
    {
        var viewModel = CreateDefault();

        Assert.False(viewModel.HasFileNameWarning);
        Assert.Null(viewModel.FileNameWarning);
    }

    [Fact]
    public void ホットキーの表示文字列が更新される()
    {
        var viewModel = CreateDefault();

        Assert.Equal("Ctrl + Alt + S", viewModel.HotkeyDisplay);

        viewModel.ModifierShift = true;

        Assert.Equal("Ctrl + Alt + Shift + S", viewModel.HotkeyDisplay);
    }

    [Fact]
    public void 矩形が未設定なら未設定と表示する()
    {
        var viewModel = CreateDefault();

        Assert.Contains("未設定", viewModel.CropSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void 矩形の概要に座標とサイズが出る()
    {
        var viewModel = CreateDefault();
        viewModel.CropRect = new PixelRect(243, 32, 2208, 1344);

        Assert.Contains("243", viewModel.CropSummary, StringComparison.Ordinal);
        Assert.Contains("2208", viewModel.CropSummary, StringComparison.Ordinal);
    }
}
