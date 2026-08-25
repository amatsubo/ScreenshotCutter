using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScreenshotCutter.Interop;

/// <summary>
/// ホットキーとトレイアイコンのコールバックを受け取るための非表示ウィンドウ。
/// </summary>
/// <remarks>
/// メッセージ専用ウィンドウ（HWND_MESSAGE）ではなく通常のトップレベルウィンドウとして
/// 作る。エクスプローラー再起動時の <c>TaskbarCreated</c> や、多重起動時の
/// ブロードキャストメッセージは HWND_BROADCAST 経由で送られるため、
/// メッセージ専用ウィンドウには届かないため。
/// ウィンドウは一度も表示しないので、画面上には現れない。
/// </remarks>
internal sealed class MessageWindow : IDisposable
{
    private const int WS_OVERLAPPED = 0x00000000;

    // WndProc はアンマネージ側から呼ばれるため、GC されないようフィールドで保持する。
    private readonly NativeMethods.WndProcDelegate _wndProc;
    private readonly string _className;
    private readonly IntPtr _instanceHandle;

    private IntPtr _handle;
    private bool _classRegistered;
    private bool _disposed;

    /// <summary>
    /// ウィンドウメッセージのハンドラー。処理した場合は戻り値を返し、
    /// 既定処理に委ねる場合は null を返す。
    /// </summary>
    public Func<uint, IntPtr, IntPtr, IntPtr?>? MessageHandler { get; set; }

    public IntPtr Handle => _handle;

    public MessageWindow(string classNamePrefix)
    {
        // 同一プロセス内で複数生成されても衝突しないようにユニーク化する。
        _className = $"{classNamePrefix}_{Guid.NewGuid():N}";
        _wndProc = WndProc;
        _instanceHandle = NativeMethods.GetModuleHandle(null);

        var wndClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _instanceHandle,
            lpszClassName = _className,
        };

        if (NativeMethods.RegisterClassEx(ref wndClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ウィンドウクラスの登録に失敗しました。");
        }

        _classRegistered = true;

        _handle = NativeMethods.CreateWindowEx(
            dwExStyle: 0,
            lpClassName: _className,
            lpWindowName: classNamePrefix,
            dwStyle: WS_OVERLAPPED,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent: IntPtr.Zero,
            hMenu: IntPtr.Zero,
            hInstance: _instanceHandle,
            lpParam: IntPtr.Zero);

        if (_handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnregisterClass(_className, _instanceHandle);
            _classRegistered = false;
            throw new Win32Exception(error, "メッセージ受信用ウィンドウの作成に失敗しました。");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // ハンドラー内の例外がアンマネージ境界を越えるとプロセスが即死するため、
        // ここで捕捉してログに残し、既定処理へ落とす。
        try
        {
            var result = MessageHandler?.Invoke(msg, wParam, lParam);
            if (result.HasValue)
            {
                return result.Value;
            }
        }
        catch (Exception ex)
        {
            Services.Logger.Error("ウィンドウメッセージの処理に失敗しました。", ex);
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        MessageHandler = null;

        if (_handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_handle);
            _handle = IntPtr.Zero;
        }

        if (_classRegistered)
        {
            NativeMethods.UnregisterClass(_className, _instanceHandle);
            _classRegistered = false;
        }
    }
}
