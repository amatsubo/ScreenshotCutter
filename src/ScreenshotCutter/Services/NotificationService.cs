using System.Media;
using ScreenshotCutter.Models;

namespace ScreenshotCutter.Services;

/// <summary>
/// 撮影結果の通知（確定仕様書 4.9）。
/// </summary>
/// <remarks>
/// Windows 11 本来のトースト通知は AppUserModelID の登録
/// （＝スタートメニューへのショートカット作成）を必要とし、
/// 「コピーするだけで動く」というポータブル要件と衝突する。
/// そのため通知はトレイアイコンのバルーン通知で行う。
/// </remarks>
public sealed class NotificationService
{
    private readonly TrayIcon _trayIcon;
    private readonly Func<NotificationSettings> _settingsProvider;

    public NotificationService(TrayIcon trayIcon, Func<NotificationSettings> settingsProvider)
    {
        _trayIcon = trayIcon;
        _settingsProvider = settingsProvider;
    }

    /// <summary>撮影が成功したときの通知。設定に応じて表示と音を出す。</summary>
    public void NotifySuccess(string message)
    {
        var settings = _settingsProvider();

        if (settings.Toast)
        {
            _trayIcon.ShowBalloon(AppPaths.AppName, message, isError: false);
        }

        if (settings.ShutterSound)
        {
            PlayShutterSound();
        }
    }

    /// <summary>
    /// エラー通知。ユーザーが対処すべき事象なので、
    /// トースト設定が OFF でも必ず表示する。
    /// </summary>
    public void NotifyError(string message)
    {
        _trayIcon.ShowBalloon(AppPaths.AppName, message, isError: true);
    }

    /// <summary>
    /// シャッター音。Windows のシステム音を使い、音声ファイルは同梱しない
    /// （確定仕様書 4.9.5）。
    /// </summary>
    private static void PlayShutterSound()
    {
        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            // 音が鳴らないだけでは撮影を失敗扱いにしない。
            Logger.Error("シャッター音の再生に失敗しました。", ex);
        }
    }
}
