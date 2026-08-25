using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ScreenshotCutter.Services;

/// <summary>
/// クリップボードへの画像コピー（確定仕様書 4.8）。
/// </summary>
/// <remarks>
/// DIB だけで渡すと、一部のアプリ（ブラウザーやチャットクライアント）で
/// 貼り付け時に色化けや透過崩れが起きる。PNG 形式も同時に載せて、
/// 貼り付け側が扱いやすい方を選べるようにする（確定仕様書 4.8.2）。
/// </remarks>
public static class ClipboardService
{
    /// <summary>PNG バイト列を載せるときのクリップボード形式名。</summary>
    private const string PngFormat = "PNG";

    /// <summary>他プロセスがクリップボードを掴んでいるときの再試行回数。</summary>
    private const int MaxAttempts = 5;

    private const int RetryDelayMilliseconds = 60;

    /// <summary>
    /// 画像をクリップボードへ載せる。STA スレッドから呼ぶこと。
    /// 失敗した場合は例外を投げる。
    /// </summary>
    public static void CopyImage(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var dataObject = new DataObject();

        // CF_DIB / CF_BITMAP 相当。ほとんどのアプリはこちらを見る。
        dataObject.SetImage(image);

        using var pngStream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(pngStream);
        dataObject.SetData(PngFormat, pngStream, autoConvert: false);

        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                // copy: true で OleFlushClipboard 相当が走り、
                // 本アプリを終了しても貼り付けられる状態が残る。
                Clipboard.SetDataObject(dataObject, copy: true);
                return;
            }
            catch (ExternalException ex)
            {
                // 別プロセスがクリップボードを開いている間は失敗する。
                lastError = ex;

                if (attempt < MaxAttempts)
                {
                    Thread.Sleep(RetryDelayMilliseconds);
                }
            }
        }

        throw new InvalidOperationException(
            "クリップボードを開けませんでした。ほかのアプリが使用中の可能性があります。", lastError);
    }
}
