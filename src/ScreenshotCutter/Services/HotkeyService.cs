using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using ScreenshotCutter.Interop;
using ScreenshotCutter.Models;

namespace ScreenshotCutter.Services;

/// <summary>
/// グローバルホットキーの登録（確定仕様書 4.4）。
/// </summary>
/// <remarks>
/// <see cref="NativeMethods.MOD_NOREPEAT"/> を必ず付けることで、
/// 押しっぱなしでも 1 度しか発火しない。RegisterHotKey は KeyUp を
/// 通知しないため、この挙動はフラグで実現するしかない。
/// </remarks>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 1;

    private readonly IntPtr _windowHandle;
    private bool _registered;
    private bool _disposed;

    public HotkeyService(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    /// <summary>ホットキーが押されたときに発火する。</summary>
    public event Action? Pressed;

    /// <summary>直近の登録失敗の理由。成功している場合は null。</summary>
    public string? LastError { get; private set; }

    public bool IsRegistered => _registered;

    /// <summary>
    /// 現在の登録を解除し、指定の設定で登録し直す。
    /// 失敗した場合は false を返し、理由を <see cref="LastError"/> に格納する。
    /// </summary>
    public bool Register(HotkeySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Unregister();

        if (!HotkeyParser.TryParse(settings, out var modifiers, out var virtualKey, out var parseError))
        {
            LastError = parseError;
            return false;
        }

        if (!NativeMethods.RegisterHotKey(
                _windowHandle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
        {
            var errorCode = Marshal.GetLastWin32Error();
            LastError = errorCode == ErrorHotkeyAlreadyRegistered
                ? $"ホットキー {HotkeyParser.Format(settings)} は、ほかのアプリが既に使用しています。別の組み合わせを指定してください。"
                : $"ホットキー {HotkeyParser.Format(settings)} の登録に失敗しました。（エラー {errorCode}）";

            Logger.Error(LastError, new Win32Exception(errorCode));
            return false;
        }

        _registered = true;
        LastError = null;
        return true;
    }

    private const int ErrorHotkeyAlreadyRegistered = 1409; // ERROR_HOTKEY_ALREADY_REGISTERED

    /// <summary>
    /// ウィンドウメッセージを処理する。処理した場合は true。
    /// </summary>
    public bool HandleMessage(uint message, IntPtr wParam)
    {
        if (message != NativeMethods.WM_HOTKEY || wParam.ToInt32() != HotkeyId)
        {
            return false;
        }

        Pressed?.Invoke();
        return true;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        Pressed = null;
    }
}

/// <summary>
/// <see cref="HotkeySettings"/> と Win32 のホットキー表現との相互変換。
/// </summary>
public static class HotkeyParser
{
    private const string Ctrl = "Ctrl";
    private const string Alt = "Alt";
    private const string Shift = "Shift";
    private const string Win = "Win";

    /// <summary>設定画面で選べる修飾キー。表示順を兼ねる。</summary>
    public static IReadOnlyList<string> AvailableModifiers { get; } = [Ctrl, Alt, Shift, Win];

    /// <summary>
    /// 設定を Win32 の修飾キーと仮想キーコードへ変換する。
    /// </summary>
    public static bool TryParse(
        HotkeySettings settings,
        out uint modifiers,
        out uint virtualKey,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(settings);

        modifiers = 0;
        virtualKey = 0;
        error = null;

        foreach (var modifier in settings.Modifiers)
        {
            switch (modifier?.Trim())
            {
                case Ctrl: modifiers |= NativeMethods.MOD_CONTROL; break;
                case Alt: modifiers |= NativeMethods.MOD_ALT; break;
                case Shift: modifiers |= NativeMethods.MOD_SHIFT; break;
                case Win: modifiers |= NativeMethods.MOD_WIN; break;
                default:
                    error = $"不明な修飾キーです。: {modifier}";
                    return false;
            }
        }

        // 修飾キーなしの単独割り当ては誤爆しやすいため禁止（確定仕様書 4.4.5）。
        if (modifiers == 0)
        {
            error = "修飾キー（Ctrl / Alt / Shift / Win）を 1 つ以上選んでください。";
            return false;
        }

        if (!Enum.TryParse<Key>(settings.Key, ignoreCase: true, out var key) || key == Key.None)
        {
            error = $"キー '{settings.Key}' を解釈できません。";
            return false;
        }

        if (IsModifierKey(key))
        {
            error = "修飾キー単体は主キーに指定できません。";
            return false;
        }

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            error = $"キー '{settings.Key}' に対応する仮想キーコードがありません。";
            return false;
        }

        return true;
    }

    /// <summary>"Ctrl + Alt + S" のような表示用文字列を作る。</summary>
    public static string Format(HotkeySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 設定ファイル上の並び順に依存せず、常に同じ順序で表示する。
        var parts = AvailableModifiers
            .Where(m => settings.Modifiers.Any(x => string.Equals(x?.Trim(), m, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        parts.Add(string.IsNullOrWhiteSpace(settings.Key) ? "(未設定)" : settings.Key);

        return string.Join(" + ", parts);
    }

    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;

    /// <summary>
    /// 設定画面のキー選択に出す候補。実用的な範囲に絞る。
    /// </summary>
    public static IReadOnlyList<Key> SelectableKeys { get; } = BuildSelectableKeys();

    private static List<Key> BuildSelectableKeys()
    {
        var keys = new List<Key>();

        for (var key = Key.A; key <= Key.Z; key++)
        {
            keys.Add(key);
        }

        for (var key = Key.D0; key <= Key.D9; key++)
        {
            keys.Add(key);
        }

        for (var key = Key.F1; key <= Key.F12; key++)
        {
            keys.Add(key);
        }

        keys.AddRange(
        [
            Key.PrintScreen, Key.Insert, Key.Delete, Key.Home, Key.End,
            Key.PageUp, Key.PageDown, Key.Space, Key.Enter, Key.Tab,
            Key.OemTilde, Key.OemMinus, Key.OemPlus,
            Key.OemOpenBrackets, Key.OemCloseBrackets,
            Key.OemSemicolon, Key.OemQuotes, Key.OemComma, Key.OemPeriod, Key.OemQuestion,
        ]);

        return keys;
    }
}
