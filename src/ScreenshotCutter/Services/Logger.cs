using System.IO;
using System.Globalization;
using System.Text;

namespace ScreenshotCutter.Services;

/// <summary>
/// エラー時のみファイルへ書き出す最小限のロガー（確定仕様書 7章）。
/// </summary>
/// <remarks>
/// 常駐アプリのため、通常操作ではファイル I/O を一切発生させない。
/// ログ出力自体の失敗はアプリの動作に影響させず握りつぶす
/// （書き込み不可のパスに置かれている場合を想定）。
/// </remarks>
public static class Logger
{
    private const int RetentionDays = 7;

    private static readonly Lock Gate = new();

    // 古いログの掃除はプロセスにつき 1 回で足りる。
    private static bool _cleanupDone;

    public static void Error(string message, Exception? exception = null)
    {
        var builder = new StringBuilder();
        builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        builder.Append(" [ERROR] ");
        builder.AppendLine(message);

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        Write(builder.ToString());
    }

    private static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);

                if (!_cleanupDone)
                {
                    _cleanupDone = true;
                    RemoveExpiredLogs();
                }

                var path = Path.Combine(
                    AppPaths.LogDirectory,
                    $"{DateTime.Now:yyyy-MM-dd}.log");

                File.AppendAllText(path, text, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // ログが書けないこと自体は致命的ではないため、意図的に無視する。
        }
    }

    private static void RemoveExpiredLogs()
    {
        try
        {
            var threshold = DateTime.Now.Date.AddDays(-RetentionDays);

            foreach (var file in Directory.EnumerateFiles(AppPaths.LogDirectory, "*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);

                // 日付として解釈できないファイルは本アプリのログではないため触らない。
                if (DateTime.TryParseExact(
                        name,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date)
                    && date < threshold)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception)
        {
            // 掃除に失敗してもログ出力自体は続行する。
        }
    }
}
