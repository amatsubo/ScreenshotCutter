using System.Windows.Input;
using ScreenshotCutter.Models;
using ScreenshotCutter.Services;

namespace ScreenshotCutter.Tests;

/// <summary>
/// ホットキー設定の解釈（確定仕様書 4.4）。
/// </summary>
public class HotkeyParserTests
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    [Fact]
    public void 既定のCtrlAltSを解釈できる()
    {
        var settings = new HotkeySettings();

        var ok = HotkeyParser.TryParse(settings, out var modifiers, out var virtualKey, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ModControl | ModAlt, modifiers);
        Assert.Equal((uint)KeyInterop.VirtualKeyFromKey(Key.S), virtualKey);
    }

    [Fact]
    public void 修飾キー4種をすべて解釈できる()
    {
        var settings = new HotkeySettings
        {
            Modifiers = ["Ctrl", "Alt", "Shift", "Win"],
            Key = "F5",
        };

        Assert.True(HotkeyParser.TryParse(settings, out var modifiers, out _, out _));
        Assert.Equal(ModControl | ModAlt | ModShift | ModWin, modifiers);
    }

    [Fact]
    public void 修飾キーが無い設定は拒否する()
    {
        // 単独キーの割り当ては誤爆しやすいため禁止（確定仕様書 4.4.5）。
        var settings = new HotkeySettings { Modifiers = [], Key = "S" };

        Assert.False(HotkeyParser.TryParse(settings, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void 不明な修飾キーは拒否する()
    {
        var settings = new HotkeySettings { Modifiers = ["Hyper"], Key = "S" };

        Assert.False(HotkeyParser.TryParse(settings, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NotAKey")]
    [InlineData("None")]
    public void 解釈できないキーは拒否する(string key)
    {
        var settings = new HotkeySettings { Modifiers = ["Ctrl"], Key = key };

        Assert.False(HotkeyParser.TryParse(settings, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("LeftCtrl")]
    [InlineData("LeftShift")]
    [InlineData("LWin")]
    public void 修飾キー単体を主キーにはできない(string key)
    {
        var settings = new HotkeySettings { Modifiers = ["Ctrl"], Key = key };

        Assert.False(HotkeyParser.TryParse(settings, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void キー名の大文字小文字は区別しない()
    {
        var settings = new HotkeySettings { Modifiers = ["Ctrl"], Key = "f1" };

        Assert.True(HotkeyParser.TryParse(settings, out _, out var virtualKey, out _));
        Assert.Equal((uint)KeyInterop.VirtualKeyFromKey(Key.F1), virtualKey);
    }

    [Fact]
    public void PrintScreenも指定できる()
    {
        var settings = new HotkeySettings { Modifiers = ["Ctrl", "Shift"], Key = "PrintScreen" };

        Assert.True(HotkeyParser.TryParse(settings, out _, out var virtualKey, out _));
        Assert.Equal((uint)KeyInterop.VirtualKeyFromKey(Key.PrintScreen), virtualKey);
    }

    // ------------------------------------------------------------ Format

    [Fact]
    public void 表示文字列は決まった順序になる()
    {
        // 設定ファイル上の並び順に関わらず Ctrl → Alt → Shift → Win の順。
        var settings = new HotkeySettings
        {
            Modifiers = ["Win", "Shift", "Alt", "Ctrl"],
            Key = "S",
        };

        Assert.Equal("Ctrl + Alt + Shift + Win + S", HotkeyParser.Format(settings));
    }

    [Fact]
    public void 既定設定の表示文字列()
    {
        Assert.Equal("Ctrl + Alt + S", HotkeyParser.Format(new HotkeySettings()));
    }

    [Fact]
    public void キーが未設定なら未設定と表示する()
    {
        var settings = new HotkeySettings { Modifiers = ["Ctrl"], Key = string.Empty };

        Assert.Equal("Ctrl + (未設定)", HotkeyParser.Format(settings));
    }

    // ------------------------------------------------------ SelectableKeys

    [Fact]
    public void 選択候補に修飾キーは含まれない()
    {
        Assert.DoesNotContain(HotkeyParser.SelectableKeys, HotkeyParser.IsModifierKey);
    }

    [Fact]
    public void 選択候補はすべて解釈できる()
    {
        foreach (var key in HotkeyParser.SelectableKeys)
        {
            var settings = new HotkeySettings { Modifiers = ["Ctrl"], Key = key.ToString() };

            Assert.True(
                HotkeyParser.TryParse(settings, out _, out _, out var error),
                $"{key} を解釈できませんでした: {error}");
        }
    }

    [Fact]
    public void 選択候補に重複が無い()
    {
        Assert.Equal(HotkeyParser.SelectableKeys.Count, HotkeyParser.SelectableKeys.Distinct().Count());
    }
}
