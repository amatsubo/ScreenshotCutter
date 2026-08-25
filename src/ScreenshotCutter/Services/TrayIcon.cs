using System.ComponentModel;
using System.Runtime.InteropServices;
using ScreenshotCutter.Interop;

namespace ScreenshotCutter.Services;

/// <summary>トレイの右クリックメニューの項目（確定仕様書 4.2.4）。</summary>
public enum TrayMenuCommand
{
    None = 0,
    CaptureNow = 1,
    ToggleCrop = 2,
    ToggleClipboard = 3,
    OpenOutputFolder = 4,
    OpenSettings = 5,
    Exit = 6,
}

/// <summary>右クリックメニューを組み立てるための現在状態。</summary>
/// <param name="CropEnabled">切り出しが有効か。</param>
/// <param name="ClipboardEnabled">クリップボードへのコピーが有効か。</param>
/// <param name="CanToggleClipboard">
/// クリップボードのトグルを操作できるか。
/// ファイル保存が無効なときは、両方 OFF になるのを防ぐため false にする。
/// </param>
public readonly record struct TrayMenuState(
    bool CropEnabled,
    bool ClipboardEnabled,
    bool CanToggleClipboard);

/// <summary>
/// Shell_NotifyIcon による通知領域アイコン（確定仕様書 4.2）。
/// バルーン通知も同じ API で扱う（確定仕様書 4.9.1）。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    // バルーン通知とツールチップの文字数上限。NOTIFYICONDATA の
    // 固定長フィールドを超えるとマーシャリングで例外になるため必ず切り詰める。
    private const int MaxTipLength = 127;
    private const int MaxInfoLength = 255;
    private const int MaxInfoTitleLength = 63;

    private readonly IntPtr _windowHandle;
    private readonly uint _taskbarCreatedMessage;

    private IntPtr _iconHandle;
    private bool _added;
    private bool _disposed;

    public TrayIcon(IntPtr windowHandle, string tooltip)
    {
        _windowHandle = windowHandle;
        Tooltip = tooltip;

        // エクスプローラーが再起動するとアイコンが消えるため、
        // 再登録の合図になるブロードキャストメッセージを購読する。
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    }

    /// <summary>アイコンがダブルクリックされたとき（確定仕様書 4.2.3）。</summary>
    public event Action? DoubleClicked;

    /// <summary>右クリックメニューの項目が選ばれたとき。</summary>
    public event Action<TrayMenuCommand>? MenuCommandInvoked;

    /// <summary>メニューを開く直前に、チェック状態を問い合わせる。</summary>
    public Func<TrayMenuState>? MenuStateProvider { get; set; }

    public string Tooltip { get; private set; }

    /// <summary>通知領域にアイコンを登録する。</summary>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_added)
        {
            return;
        }

        _iconHandle = AppIcon.LoadSmallIcon();

        var data = CreateData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP);

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), "通知領域へのアイコン登録に失敗しました。");
        }

        _added = true;

        // バージョン 4 にするとコールバックの座標が wParam で受け取れる。
        var versionData = CreateData(0);
        versionData.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref versionData);
    }

    public void SetTooltip(string tooltip)
    {
        Tooltip = tooltip;

        if (!_added)
        {
            return;
        }

        var data = CreateData(NativeMethods.NIF_TIP);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    /// <summary>
    /// バルーン通知を表示する（確定仕様書 4.9.1）。
    /// 表示できなくても呼び出し元の処理は続行させたいので、失敗は握りつぶす。
    /// </summary>
    public void ShowBalloon(string title, string message, bool isError)
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData(NativeMethods.NIF_INFO);
        data.szInfoTitle = Truncate(title, MaxInfoTitleLength);
        data.szInfo = Truncate(message, MaxInfoLength);

        // 音は設定に従って別途鳴らすため、通知側の既定音は抑止する。
        data.dwInfoFlags =
            (isError ? NativeMethods.NIIF_ERROR : NativeMethods.NIIF_INFO) | NativeMethods.NIIF_NOSOUND;

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data))
        {
            Logger.Error(
                $"バルーン通知の表示に失敗しました。: {title}",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    /// <summary>
    /// ウィンドウメッセージを処理する。処理した場合は true。
    /// </summary>
    public bool HandleMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (_disposed)
        {
            return false;
        }

        // エクスプローラー再起動後の再登録
        if (message == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            Readd();
            return true;
        }

        if (message != NativeMethods.WM_TRAYICON)
        {
            return false;
        }

        // バージョン 4 のコールバックでは lParam の下位ワードが通知コード、
        // wParam にアンカー座標が入る。
        var notification = (uint)(lParam.ToInt64() & 0xFFFF);
        var anchorX = (short)(wParam.ToInt64() & 0xFFFF);
        var anchorY = (short)((wParam.ToInt64() >> 16) & 0xFFFF);

        switch (notification)
        {
            case NativeMethods.WM_LBUTTONDBLCLK:
                DoubleClicked?.Invoke();
                return true;

            case NativeMethods.WM_CONTEXTMENU:
            case NativeMethods.WM_RBUTTONUP:
                ShowContextMenu(anchorX, anchorY);
                return true;

            default:
                return false;
        }
    }

    private void Readd()
    {
        _added = false;

        try
        {
            Show();
        }
        catch (Exception ex)
        {
            Logger.Error("エクスプローラー再起動後のアイコン再登録に失敗しました。", ex);
        }
    }

    private void ShowContextMenu(int x, int y)
    {
        var state = MenuStateProvider?.Invoke()
                    ?? new TrayMenuState(CropEnabled: false, ClipboardEnabled: true, CanToggleClipboard: true);

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            Logger.Error(
                "コンテキストメニューの作成に失敗しました。",
                new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }

        try
        {
            AppendItem(menu, TrayMenuCommand.CaptureNow, "今すぐ撮影(&C)");
            AppendSeparator(menu);
            AppendItem(menu, TrayMenuCommand.ToggleCrop, "切り出しを有効にする", checkedItem: state.CropEnabled);
            AppendItem(
                menu,
                TrayMenuCommand.ToggleClipboard,
                "クリップボードにコピー",
                checkedItem: state.ClipboardEnabled,
                enabled: state.CanToggleClipboard);
            AppendSeparator(menu);
            AppendItem(menu, TrayMenuCommand.OpenOutputFolder, "保存先フォルダーを開く(&O)");
            AppendItem(menu, TrayMenuCommand.OpenSettings, "設定を開く(&S)...");
            AppendSeparator(menu);
            AppendItem(menu, TrayMenuCommand.Exit, "終了(&X)");

            // メニューを開く前に前面化しないと、別ウィンドウをクリックしても
            // メニューが閉じずに残ってしまう（Win32 の既知の作法）。
            NativeMethods.SetForegroundWindow(_windowHandle);

            var selected = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                x,
                y,
                _windowHandle,
                IntPtr.Zero);

            // 同じく作法。これを送らないと次回メニューが開かないことがある。
            NativeMethods.PostMessage(_windowHandle, 0x0000 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);

            if (selected != 0 && Enum.IsDefined(typeof(TrayMenuCommand), selected))
            {
                MenuCommandInvoked?.Invoke((TrayMenuCommand)selected);
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static void AppendItem(
        IntPtr menu,
        TrayMenuCommand command,
        string text,
        bool checkedItem = false,
        bool enabled = true)
    {
        var flags = NativeMethods.MF_STRING;

        if (checkedItem)
        {
            flags |= NativeMethods.MF_CHECKED;
        }

        if (!enabled)
        {
            flags |= NativeMethods.MF_DISABLED | NativeMethods.MF_GRAYED;
        }

        NativeMethods.AppendMenu(menu, flags, (UIntPtr)(uint)command, text);
    }

    private static void AppendSeparator(IntPtr menu) =>
        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);

    private NativeMethods.NOTIFYICONDATA CreateData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _windowHandle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = NativeMethods.WM_TRAYICON,
        hIcon = _iconHandle,
        szTip = Truncate(Tooltip, MaxTipLength),
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DoubleClicked = null;
        MenuCommandInvoked = null;
        MenuStateProvider = null;

        if (_added)
        {
            var data = CreateData(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }
}
