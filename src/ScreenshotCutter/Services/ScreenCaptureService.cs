using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotCutter.Interop;
using ScreenshotCutter.Models;

namespace ScreenshotCutter.Services;

/// <summary>
/// GDI の BitBlt による画面キャプチャ（確定仕様書 4.5.2）。
/// </summary>
/// <remarks>
/// 常駐アプリのため、GDI ハンドルは 1 つも取りこぼさずに解放する。
/// 撮影のたびにリークすると、いずれ描画不能に陥る。
/// </remarks>
public sealed class ScreenCaptureService
{
    /// <summary>
    /// 指定モニターの内容を取得する。
    /// </summary>
    /// <param name="monitor">対象モニター。</param>
    /// <param name="region">
    /// モニター相対の取得範囲。null の場合はモニター全体。
    /// </param>
    /// <returns>凍結済みの <see cref="BitmapSource"/>。</returns>
    public BitmapSource Capture(MonitorInfo monitor, PixelRect? region = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var area = region ?? new PixelRect(0, 0, monitor.Width, monitor.Height);

        if (!area.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region), area, "取得範囲の幅と高さは 1 以上である必要があります。");
        }

        // 対象モニター専用の DC を作ると、座標がモニター相対になり
        // 仮想デスクトップの負座標を意識せずに済む。
        var screenDc = NativeMethods.CreateDC("DISPLAY", monitor.DeviceName, null, IntPtr.Zero);
        var usingDesktopDc = false;
        var sourceOrigin = new PixelRect(area.X, area.Y, area.Width, area.Height);

        if (screenDc == IntPtr.Zero)
        {
            // モニター専用 DC が作れない場合はデスクトップ全体の DC で代用する。
            // こちらは仮想デスクトップ座標系なのでオフセットを足す。
            screenDc = NativeMethods.GetDC(IntPtr.Zero);
            usingDesktopDc = true;
            sourceOrigin = sourceOrigin with
            {
                X = monitor.VirtualBounds.X + area.X,
                Y = monitor.VirtualBounds.Y + area.Y,
            };
        }

        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "画面のデバイスコンテキストを取得できませんでした。");
        }

        var memoryDc = IntPtr.Zero;
        var dibSection = IntPtr.Zero;
        var previousBitmap = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "メモリデバイスコンテキストを作成できませんでした。");
            }

            var bitmapInfo = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = area.Width,

                    // 負値でトップダウン DIB になり、行の並べ替えが不要になる。
                    biHeight = -area.Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0, // BI_RGB
                },
            };

            dibSection = NativeMethods.CreateDIBSection(
                memoryDc, ref bitmapInfo, NativeMethods.DIB_RGB_COLORS, out var pixels, IntPtr.Zero, 0);

            if (dibSection == IntPtr.Zero || pixels == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "キャプチャ用のビットマップを作成できませんでした。");
            }

            previousBitmap = NativeMethods.SelectObject(memoryDc, dibSection);

            var copied = NativeMethods.BitBlt(
                memoryDc, 0, 0, area.Width, area.Height,
                screenDc, sourceOrigin.X, sourceOrigin.Y,
                NativeMethods.SRCCOPY);

            if (!copied)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "画面の取り込みに失敗しました。");
            }

            var stride = area.Width * 4;

            // BitBlt はアルファチャンネルを埋めないため、Bgra32 で解釈すると
            // 全画素が透明になってしまう。アルファを無視する Bgr32 を使う。
            var bitmap = BitmapSource.Create(
                area.Width,
                area.Height,
                96,
                96,
                PixelFormats.Bgr32,
                palette: null,
                buffer: pixels,
                bufferSize: stride * area.Height,
                stride: stride);

            // UI スレッド以外からも触れるように凍結する。
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            if (memoryDc != IntPtr.Zero)
            {
                if (previousBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memoryDc, previousBitmap);
                }

                NativeMethods.DeleteDC(memoryDc);
            }

            if (dibSection != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(dibSection);
            }

            if (usingDesktopDc)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
            else
            {
                NativeMethods.DeleteDC(screenDc);
            }
        }
    }
}
