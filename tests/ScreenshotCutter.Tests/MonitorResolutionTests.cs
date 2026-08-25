using ScreenshotCutter.Models;
using ScreenshotCutter.Services;

namespace ScreenshotCutter.Tests;

/// <summary>
/// 対象モニターの照合（確定仕様書 4.11）。
/// </summary>
public class MonitorResolutionTests
{
    private static MonitorInfo Monitor(
        int index,
        string id,
        string deviceName,
        string friendlyName,
        bool isPrimary = false,
        int width = 2560,
        int height = 1440) => new()
    {
        Index = index,
        Id = id,
        DeviceName = deviceName,
        FriendlyName = friendlyName,
        VirtualBounds = new PixelRect(0, 0, width, height),
        IsPrimary = isPrimary,
        Dpi = 96,
    };

    /// <summary>実機の 4 画面構成を模したデータ。</summary>
    private static List<MonitorInfo> SampleMonitors() =>
    [
        Monitor(1, "DISPLAY#ACM1234#5&1a2b3c4d&0&UID256", @"\\.\DISPLAY1", "Sample Monitor A"),
        Monitor(2, "DISPLAY#BDM5678#5&1a2b3c4d&0&UID264", @"\\.\DISPLAY2", "Sample Monitor B", isPrimary: true),
        Monitor(3, "DISPLAY#CEM9012#5&1a2b3c4d&0&UID268", @"\\.\DISPLAY3", "Sample Monitor C", width: 1080, height: 1920),
        Monitor(4, "DISPLAY#DFM3456#5&1a2b3c4d&0&UID260", @"\\.\DISPLAY4", "Sample Monitor D"),
    ];

    // ------------------------------------------------------------ 主キー

    [Fact]
    public void 主キーの識別子で一致する()
    {
        var monitors = SampleMonitors();
        var capture = new CaptureSettings
        {
            MonitorId = "DISPLAY#CEM9012#5&1a2b3c4d&0&UID268",
            MonitorDeviceName = "DISPLAY3",
        };

        var result = MonitorService.Resolve(capture, monitors);

        Assert.Equal(3, result?.Index);
    }

    [Fact]
    public void 識別子の一致は大文字小文字を区別しない()
    {
        var monitors = SampleMonitors();
        var capture = new CaptureSettings { MonitorId = "display#cem9012#5&1a2b3c4d&0&uid268" };

        Assert.Equal(3, MonitorService.Resolve(capture, monitors)?.Index);
    }

    [Fact]
    public void 識別子が一致すればデバイス名がずれていても優先する()
    {
        // ケーブルを挿し替えて DISPLAY 番号が入れ替わったケース。
        var monitors = SampleMonitors();
        var capture = new CaptureSettings
        {
            MonitorId = "DISPLAY#DFM3456#5&1a2b3c4d&0&UID260",
            MonitorDeviceName = "DISPLAY1",
        };

        Assert.Equal(4, MonitorService.Resolve(capture, monitors)?.Index);
    }

    // ---------------------------------------------------------- 補助キー

    [Fact]
    public void 識別子が一致しなければデバイス名で照合する()
    {
        var monitors = SampleMonitors();
        var capture = new CaptureSettings
        {
            MonitorId = "DISPLAY#OLDMONITOR#0&0&0&UID999",
            MonitorDeviceName = "DISPLAY2",
        };

        Assert.Equal(2, MonitorService.Resolve(capture, monitors)?.Index);
    }

    [Fact]
    public void 識別子もデバイス名も一致しなければフレンドリ名で照合する()
    {
        var monitors = SampleMonitors();
        var capture = new CaptureSettings
        {
            MonitorId = "DISPLAY#OLDMONITOR#0&0&0&UID999",
            MonitorDeviceName = "DISPLAY9",
            MonitorFriendlyName = "Sample Monitor D",
        };

        Assert.Equal(4, MonitorService.Resolve(capture, monitors)?.Index);
    }

    // -------------------------------------------------------- 見つからない

    [Fact]
    public void どれにも一致しなければnullを返す()
    {
        // 確定仕様書 4.11.3: プライマリへフォールバックせず撮影を中止する。
        var monitors = SampleMonitors();
        var capture = new CaptureSettings
        {
            MonitorId = "DISPLAY#REMOVED#0&0&0&UID999",
            MonitorDeviceName = "DISPLAY9",
            MonitorFriendlyName = "取り外したモニター",
        };

        Assert.Null(MonitorService.Resolve(capture, monitors));
    }

    [Fact]
    public void モニターが1台も無ければnullを返す()
    {
        Assert.Null(MonitorService.Resolve(new CaptureSettings(), []));
    }

    // ------------------------------------------------------------ 未設定

    [Fact]
    public void 未設定の場合はプライマリを返す()
    {
        var monitors = SampleMonitors();

        var result = MonitorService.Resolve(new CaptureSettings(), monitors);

        Assert.Equal(2, result?.Index);
        Assert.True(result?.IsPrimary);
    }

    [Fact]
    public void プライマリが無い場合は先頭を返す()
    {
        var monitors = new List<MonitorInfo>
        {
            Monitor(1, "A", @"\\.\DISPLAY1", "A"),
            Monitor(2, "B", @"\\.\DISPLAY2", "B"),
        };

        Assert.Equal(1, MonitorService.FindPrimary(monitors)?.Index);
    }

    // -------------------------------------------------- ShortDeviceName

    [Theory]
    [InlineData(@"\\.\DISPLAY1", "DISPLAY1")]
    [InlineData(@"\\.\DISPLAY12", "DISPLAY12")]
    [InlineData("DISPLAY3", "DISPLAY3")]
    public void 短縮デバイス名はプレフィックスを取り除く(string deviceName, string expected)
    {
        var monitor = Monitor(1, "id", deviceName, "name");

        Assert.Equal(expected, monitor.ShortDeviceName);
    }

    // ------------------------------------------------ NormalizeDevicePath

    [Fact]
    public void デバイスパスから安定した識別子を作る()
    {
        var result = MonitorService.NormalizeDevicePath(
            @"\\?\DISPLAY#ACM1234#5&1a2b3c4d&0&UID256#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}");

        Assert.Equal("DISPLAY#ACM1234#5&1a2b3c4d&0&UID256", result);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void デバイスパスが空なら空文字を返す(string? input, string expected)
    {
        Assert.Equal(expected, MonitorService.NormalizeDevicePath(input));
    }

    [Fact]
    public void GUIDが無いデバイスパスもそのまま扱える()
    {
        Assert.Equal(
            "DISPLAY#ACM1234#5&1a2b3c4d&0&UID256",
            MonitorService.NormalizeDevicePath(@"\\?\DISPLAY#ACM1234#5&1a2b3c4d&0&UID256"));
    }

    // ------------------------------------------------------ DisplayLabel

    [Fact]
    public void 表示ラベルにプライマリの印が付く()
    {
        var monitor = Monitor(2, "id", @"\\.\DISPLAY2", "Sample Monitor B", isPrimary: true);

        Assert.Contains("プライマリ", monitor.DisplayLabel, StringComparison.Ordinal);
        Assert.Contains("2560x1440", monitor.DisplayLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void フレンドリ名が無ければデバイス名を表示する()
    {
        var monitor = Monitor(1, "id", @"\\.\DISPLAY1", string.Empty);

        Assert.Contains("DISPLAY1", monitor.DisplayLabel, StringComparison.Ordinal);
    }
}
