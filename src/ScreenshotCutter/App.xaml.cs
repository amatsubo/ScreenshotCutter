using System.Windows;
using System.Windows.Threading;
using ScreenshotCutter.Interop;
using ScreenshotCutter.Services;

namespace ScreenshotCutter;

/// <summary>
/// アプリのエントリーポイント。多重起動を防ぎ、常駐を開始する。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 多重起動の判定に使う名前。トレイ常駐はセッション単位なので、
    /// Global ではなくセッションローカルにする。
    /// </summary>
    private const string SingleInstanceMutexName = "ScreenshotCutter.SingleInstance";

    private Mutex? _instanceMutex;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            // 既存インスタンスに設定画面を出させて、自分は静かに終了する
            // （確定仕様書 4.1.2）。
            NotifyExistingInstance();

            _instanceMutex.Dispose();
            _instanceMutex = null;

            Shutdown();
            return;
        }

        base.OnStartup(e);

        // UI スレッドで拾えなかった例外でプロセスごと落ちるのを防ぐ。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        try
        {
            _controller = new AppController();
            _controller.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("アプリの初期化に失敗しました。", ex);

            MessageBox.Show(
                $"ScreenshotCutter を起動できませんでした。\n\n{ex.Message}",
                "ScreenshotCutter",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }

    private static void NotifyExistingInstance()
    {
        var message = NativeMethods.RegisterWindowMessage(AppController.ShowSettingsMessageName);

        if (message != 0)
        {
            NativeMethods.PostMessage(
                NativeMethods.HWND_BROADCAST, message, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("未処理の例外が発生しました。", e.Exception);

        // 常駐を続けられる見込みがあるため、ここでは終了させない。
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // こちらは復帰できないが、原因をログに残す。
        Logger.Error("復帰できない例外が発生しました。", e.ExceptionObject as Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _controller = null;

        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 所有していない場合は解放不要。
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }
}
