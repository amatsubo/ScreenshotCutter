using System.IO;
using ScreenshotCutter.Models;

namespace ScreenshotCutter.Services;

/// <summary>
/// 「撮影 → 切り出し → 保存 / クリップボード → 通知」の一連の流れ。
/// ホットキー押下とトレイメニューの「今すぐ撮影」から呼ばれる。
/// </summary>
/// <remarks>
/// クリップボード操作が STA を要求するため、UI スレッドから呼ぶこと。
/// </remarks>
public sealed class ScreenshotWorkflow
{
    private readonly MonitorService _monitorService;
    private readonly ScreenCaptureService _captureService;
    private readonly ImageOutputService _outputService;
    private readonly NotificationService _notificationService;
    private readonly Func<AppSettings> _settingsProvider;

    public ScreenshotWorkflow(
        MonitorService monitorService,
        ScreenCaptureService captureService,
        ImageOutputService outputService,
        NotificationService notificationService,
        Func<AppSettings> settingsProvider)
    {
        _monitorService = monitorService;
        _captureService = captureService;
        _outputService = outputService;
        _notificationService = notificationService;
        _settingsProvider = settingsProvider;
    }

    /// <summary>
    /// 撮影を実行する。失敗時はバルーン通知で知らせ、例外は投げない。
    /// ホットキー経由で呼ばれるため、ここで例外を漏らすとアプリごと落ちる。
    /// </summary>
    public void Execute()
    {
        try
        {
            ExecuteCore();
        }
        catch (Exception ex)
        {
            Logger.Error("撮影処理で予期しないエラーが発生しました。", ex);
            _notificationService.NotifyError($"撮影に失敗しました。: {ex.Message}");
        }
        finally
        {
            // 撮影 1 回あたり数十 MB のビットマップを確保するため、
            // 待機状態へ戻る前に解放しておく（確定仕様書 6.3）。
            MemoryTrimmer.Trim();
        }
    }

    private void ExecuteCore()
    {
        var settings = _settingsProvider();
        var monitors = _monitorService.Enumerate();
        var monitor = MonitorService.Resolve(settings.Capture, monitors);

        // 対象モニターが見つからない場合はプライマリへ切り替えず中止する
        // （確定仕様書 4.11.3）。意図しない画面を撮ってしまう方が困るため。
        if (monitor is null)
        {
            var name = string.IsNullOrWhiteSpace(settings.Capture.MonitorFriendlyName)
                ? settings.Capture.MonitorDeviceName
                : settings.Capture.MonitorFriendlyName;

            _notificationService.NotifyError(
                $"対象のモニター（{name}）が見つからないため撮影を中止しました。設定を確認してください。");
            return;
        }

        var region = ResolveCaptureRegion(settings.Capture.Crop, monitor);
        var timestamp = DateTime.Now;

        var image = _captureService.Capture(monitor, region);

        string? savedPath = null;

        if (settings.Output.SaveToFile)
        {
            try
            {
                savedPath = _outputService.Save(image, settings.Output, timestamp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Logger.Error($"画像の保存に失敗しました。: {settings.Output.EffectiveFolder}", ex);
                _notificationService.NotifyError($"保存に失敗しました。: {ex.Message}");
                return;
            }
        }

        var clipboardFailed = false;

        if (settings.Output.CopyToClipboard)
        {
            try
            {
                ClipboardService.CopyImage(image);
            }
            catch (Exception ex)
            {
                // ファイル保存が成功しているなら撮影自体は成功とみなす
                // （確定仕様書 8章）。
                clipboardFailed = true;
                Logger.Error("クリップボードへのコピーに失敗しました。", ex);

                if (savedPath is null)
                {
                    _notificationService.NotifyError($"クリップボードへのコピーに失敗しました。: {ex.Message}");
                    return;
                }
            }
        }

        _notificationService.NotifySuccess(
            BuildSuccessMessage(image.PixelWidth, image.PixelHeight, savedPath, settings.Output.CopyToClipboard, clipboardFailed));
    }

    /// <summary>
    /// 実際に取り込む範囲を決める。切り出しが無効、または矩形が壊れている
    /// 場合はモニター全体を返す（確定仕様書 4.6.1）。
    /// </summary>
    private static PixelRect? ResolveCaptureRegion(CropSettings crop, MonitorInfo monitor)
    {
        if (!crop.IsEffective)
        {
            return null;
        }

        // 解像度が変わっていた場合に備えて毎回クランプする（確定仕様書 4.6.2.4）。
        var clamped = CropCalculator.Clamp(crop.Rect, monitor.Width, monitor.Height);

        return clamped.IsValid ? clamped : null;
    }

    private static string BuildSuccessMessage(
        int width,
        int height,
        string? savedPath,
        bool clipboardRequested,
        bool clipboardFailed)
    {
        var size = $"{width}x{height}";

        if (savedPath is null)
        {
            return $"クリップボードにコピーしました。（{size}）";
        }

        var fileName = Path.GetFileName(savedPath);

        if (!clipboardRequested)
        {
            return $"{fileName} を保存しました。（{size}）";
        }

        return clipboardFailed
            ? $"{fileName} を保存しました。（{size}）※クリップボードへのコピーは失敗しました"
            : $"{fileName} を保存し、クリップボードにコピーしました。（{size}）";
    }
}
