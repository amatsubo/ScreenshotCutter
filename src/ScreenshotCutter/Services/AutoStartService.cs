using Microsoft.Win32;

namespace ScreenshotCutter.Services;

/// <summary>
/// ログオン時の自動起動（確定仕様書 4.10）。
/// </summary>
/// <remarks>
/// 登録先は HKCU の Run キー。自動起動が OFF のあいだはレジストリに
/// 一切書き込まないため、ポータブル性を損なわない。
/// </remarks>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = AppPaths.AppName;

    /// <summary>自動起動が登録されているか。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex)
        {
            Logger.Error("自動起動の設定を読み取れませんでした。", ex);
            return false;
        }
    }

    /// <summary>
    /// 自動起動を登録／解除する。失敗した場合は例外を投げる
    /// （設定画面でユーザーへ提示する）。
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("スタートアップ用のレジストリキーを開けませんでした。");

        if (enabled)
        {
            key.SetValue(ValueName, BuildCommandLine(), RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// 登録済みのパスが現在の exe と食い違っていたら登録し直す
    /// （確定仕様書 4.10.3）。exe を別フォルダーへ移動しても
    /// 自動起動が壊れたままにならないようにする。
    /// </summary>
    public static void SyncRegisteredPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);

            // 未登録なら何もしない。自動起動 OFF の状態を勝手に ON にはしない。
            if (key?.GetValue(ValueName) is not string registered || registered.Length == 0)
            {
                return;
            }

            var expected = BuildCommandLine();
            if (!string.Equals(registered, expected, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, expected, RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            // 自動起動の修復に失敗しても、今回の起動自体は続行できる。
            Logger.Error("自動起動の登録パスの同期に失敗しました。", ex);
        }
    }

    /// <summary>パスに空白が含まれても正しく起動するよう引用符で囲む。</summary>
    private static string BuildCommandLine() => $"\"{AppPaths.ExecutablePath}\"";
}
