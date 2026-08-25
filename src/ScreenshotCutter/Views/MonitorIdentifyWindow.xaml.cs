using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenshotCutter.Interop;
using ScreenshotCutter.Models;
using ScreenshotCutter.Services;

namespace ScreenshotCutter.Views;

/// <summary>
/// 「識別」ボタンで各モニターの中央に番号を表示するオーバーレイ
/// （確定仕様書 4.11.6）。
/// </summary>
public partial class MonitorIdentifyWindow : Window
{
    /// <summary>ウィンドウの見かけ上のサイズ（物理ピクセル）。</summary>
    private const int OverlayWidth = 340;
    private const int OverlayHeight = 280;

    private readonly MonitorInfo _monitor;

    private MonitorIdentifyWindow(MonitorInfo monitor)
    {
        _monitor = monitor;

        InitializeComponent();

        NumberText.Text = monitor.Index.ToString(CultureInfo.InvariantCulture);
        NameText.Text = string.IsNullOrWhiteSpace(monitor.FriendlyName)
            ? monitor.ShortDeviceName
            : monitor.FriendlyName;
        ResolutionText.Text = $"{monitor.Width} x {monitor.Height}";

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // DPI 倍率に関係なく実寸で中央に置くため、物理ピクセルで直接配置する。
        var handle = new WindowInteropHelper(this).Handle;

        // WPF の IsHitTestVisible だけではウィンドウ自体がマウスを受け取ってしまう。
        // 表示中の数秒間、下のアプリを操作できなくなるのを防ぐため、
        // 拡張スタイルでクリックを透過させる。
        var exStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(
            handle,
            NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        var x = _monitor.VirtualBounds.X + ((_monitor.Width - OverlayWidth) / 2);
        var y = _monitor.VirtualBounds.Y + ((_monitor.Height - OverlayHeight) / 2);

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            OverlayWidth,
            OverlayHeight,

            // 操作を奪わないようアクティブ化はしない。
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// すべてのモニターに番号を表示し、指定時間後に自動で閉じる。
    /// </summary>
    public static void ShowAll(IReadOnlyList<MonitorInfo> monitors, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        var windows = new List<MonitorIdentifyWindow>(monitors.Count);

        foreach (var monitor in monitors)
        {
            try
            {
                var window = new MonitorIdentifyWindow(monitor);
                window.Show();
                windows.Add(window);
            }
            catch (Exception ex)
            {
                Logger.Error($"モニター {monitor.Index} の識別表示に失敗しました。", ex);
            }
        }

        if (windows.Count == 0)
        {
            return;
        }

        // 常駐時に動き続けないよう、1 回だけ発火して自身を止める。
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            foreach (var window in windows)
            {
                window.Close();
            }
        };

        timer.Start();
    }
}
