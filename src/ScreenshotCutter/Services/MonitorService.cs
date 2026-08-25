using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using ScreenshotCutter.Interop;
using ScreenshotCutter.Models;

namespace ScreenshotCutter.Services;

/// <summary>
/// モニターの列挙と、設定に保存された対象モニターの解決
/// （確定仕様書 4.11）。
/// </summary>
public sealed class MonitorService
{
    /// <summary>
    /// 接続中のモニターを列挙する。座標・サイズは物理ピクセル。
    /// </summary>
    public IReadOnlyList<MonitorInfo> Enumerate()
    {
        var descriptors = QueryDisplayDescriptors();
        var monitors = new List<MonitorInfo>();

        // デリゲートは EnumDisplayMonitors の呼び出し中のみ使われるが、
        // 明示的にローカルへ保持して GC されないようにする。
        NativeMethods.MonitorEnumProc callback = (
            IntPtr hMonitor,
            IntPtr hdc,
            ref NativeMethods.RECT clipRect,
            IntPtr data) =>
        {
            try
            {
                var info = BuildMonitorInfo(hMonitor, descriptors);
                if (info is not null)
                {
                    monitors.Add(info);
                }
            }
            catch (Exception ex)
            {
                // 1 台の取得に失敗しても、ほかのモニターの列挙は続ける。
                Logger.Error("モニター情報の取得に失敗しました。", ex);
            }

            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            Logger.Error(
                "モニターの列挙に失敗しました。",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        GC.KeepAlive(callback);

        // Windows の画面設定と同じ並び（DISPLAY1, DISPLAY2, ...）に揃える。
        monitors.Sort((a, b) => DeviceNumber(a.DeviceName).CompareTo(DeviceNumber(b.DeviceName)));

        // 表示番号は並べ替え後に振り直す。
        var result = new List<MonitorInfo>(monitors.Count);
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            result.Add(new MonitorInfo
            {
                Index = i + 1,
                Id = m.Id,
                DeviceName = m.DeviceName,
                FriendlyName = m.FriendlyName,
                VirtualBounds = m.VirtualBounds,
                IsPrimary = m.IsPrimary,
                Dpi = m.Dpi,
            });
        }

        return result;
    }

    /// <summary>
    /// 設定に保存された対象モニターを解決する。
    /// 見つからない場合は null を返し、呼び出し側で撮影を中止する
    /// （確定仕様書 4.11.3）。未設定の場合はプライマリを返す。
    /// </summary>
    public static MonitorInfo? Resolve(CaptureSettings capture, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            return null;
        }

        var hasId = !string.IsNullOrWhiteSpace(capture.MonitorId);
        var hasDeviceName = !string.IsNullOrWhiteSpace(capture.MonitorDeviceName);

        // 一度も設定されていない場合はプライマリを対象にする（初回起動）。
        if (!hasId && !hasDeviceName)
        {
            return FindPrimary(monitors);
        }

        // 主キー: EDID 由来のデバイスパス
        if (hasId)
        {
            var byId = monitors.FirstOrDefault(
                m => string.Equals(m.Id, capture.MonitorId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        // 補助キー: GDI デバイス名
        if (hasDeviceName)
        {
            var byDevice = monitors.FirstOrDefault(
                m => string.Equals(m.ShortDeviceName, capture.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (byDevice is not null)
            {
                return byDevice;
            }
        }

        // 補助キー: フレンドリ名（同じモニターを別ポートへ挿し替えた場合）
        if (!string.IsNullOrWhiteSpace(capture.MonitorFriendlyName))
        {
            var byFriendly = monitors.FirstOrDefault(
                m => string.Equals(m.FriendlyName, capture.MonitorFriendlyName, StringComparison.OrdinalIgnoreCase));
            if (byFriendly is not null)
            {
                return byFriendly;
            }
        }

        return null;
    }

    public static MonitorInfo? FindPrimary(IReadOnlyList<MonitorInfo> monitors) =>
        monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();

    private static MonitorInfo? BuildMonitorInfo(
        IntPtr hMonitor,
        IReadOnlyDictionary<string, DisplayDescriptor> descriptors)
    {
        var monitorInfo = new NativeMethods.MONITORINFOEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            szDevice = string.Empty,
        };

        if (!NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            return null;
        }

        var deviceName = monitorInfo.szDevice ?? string.Empty;
        var bounds = monitorInfo.rcMonitor;

        descriptors.TryGetValue(deviceName, out var descriptor);

        return new MonitorInfo
        {
            // 並べ替え後に振り直すため、ここでは仮の番号を入れる。
            Index = 0,
            Id = descriptor?.StableId ?? string.Empty,
            DeviceName = deviceName,
            FriendlyName = descriptor?.FriendlyName ?? string.Empty,
            VirtualBounds = new PixelRect(
                bounds.Left,
                bounds.Top,
                bounds.Right - bounds.Left,
                bounds.Bottom - bounds.Top),
            IsPrimary = (monitorInfo.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
            Dpi = GetDpi(hMonitor),
        };
    }

    private static uint GetDpi(IntPtr hMonitor)
    {
        // 取得に失敗した場合は 100%（96 DPI）とみなす。
        return NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0
            ? dpiX
            : 96u;
    }

    /// <summary><c>\\.\DISPLAY3</c> から 3 を取り出す。取れない場合は大きな値。</summary>
    private static int DeviceNumber(string deviceName)
    {
        var digits = new string(deviceName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : int.MaxValue;
    }

    private sealed record DisplayDescriptor(string StableId, string FriendlyName);

    /// <summary>
    /// GDI デバイス名 → (EDID 由来の識別子, フレンドリ名) の対応表を作る。
    /// 失敗した場合は空の辞書を返し、識別子なしで動作を継続する。
    /// </summary>
    private static IReadOnlyDictionary<string, DisplayDescriptor> QueryDisplayDescriptors()
    {
        var map = new Dictionary<string, DisplayDescriptor>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var status = DisplayConfigInterop.GetDisplayConfigBufferSizes(
                DisplayConfigInterop.QDC_ONLY_ACTIVE_PATHS,
                out var pathCount,
                out var modeCount);

            if (status != DisplayConfigInterop.ERROR_SUCCESS || pathCount == 0)
            {
                return map;
            }

            var paths = new DisplayConfigInterop.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DisplayConfigInterop.DISPLAYCONFIG_MODE_INFO[modeCount];

            status = DisplayConfigInterop.QueryDisplayConfig(
                DisplayConfigInterop.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero);

            if (status != DisplayConfigInterop.ERROR_SUCCESS)
            {
                return map;
            }

            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];

                var sourceName = new DisplayConfigInterop.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DisplayConfigInterop.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DisplayConfigInterop.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DisplayConfigInterop.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id,
                    },
                    viewGdiDeviceName = string.Empty,
                };

                if (DisplayConfigInterop.DisplayConfigGetDeviceInfo(ref sourceName) != DisplayConfigInterop.ERROR_SUCCESS)
                {
                    continue;
                }

                var targetName = new DisplayConfigInterop.DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DisplayConfigInterop.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DisplayConfigInterop.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<DisplayConfigInterop.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id,
                    },
                    monitorFriendlyDeviceName = string.Empty,
                    monitorDevicePath = string.Empty,
                };

                if (DisplayConfigInterop.DisplayConfigGetDeviceInfo(ref targetName) != DisplayConfigInterop.ERROR_SUCCESS)
                {
                    continue;
                }

                var gdiName = sourceName.viewGdiDeviceName ?? string.Empty;
                if (gdiName.Length == 0)
                {
                    continue;
                }

                map[gdiName] = new DisplayDescriptor(
                    NormalizeDevicePath(targetName.monitorDevicePath),
                    targetName.monitorFriendlyDeviceName ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            // 識別子が取れなくても、デバイス名による照合で動作は継続できる。
            Logger.Error("ディスプレイ構成の取得に失敗しました。", ex);
        }

        return map;
    }

    /// <summary>
    /// <c>\\?\DISPLAY#ACM1234#5&amp;1a2b3c4d&amp;0&amp;UID256#{guid}</c> のようなデバイスパスから、
    /// 保存用の安定した識別子 <c>DISPLAY#ACM1234#5&amp;1a2b3c4d&amp;0&amp;UID256</c> を作る。
    /// 先頭の <c>\\?\</c> と末尾のインターフェース GUID は環境によって表記が
    /// 揺れるため取り除く。
    /// </summary>
    internal static string NormalizeDevicePath(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return string.Empty;
        }

        var value = devicePath.Trim();

        if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            value = value[4..];
        }

        var guidStart = value.IndexOf("#{", StringComparison.Ordinal);
        if (guidStart >= 0)
        {
            value = value[..guidStart];
        }

        return value;
    }
}
