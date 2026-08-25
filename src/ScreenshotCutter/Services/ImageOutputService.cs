using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using ScreenshotCutter.Models;

namespace ScreenshotCutter.Services;

/// <summary>
/// 撮影した画像の PNG 保存（確定仕様書 4.7）。
/// </summary>
public sealed class ImageOutputService
{
    /// <summary>
    /// 空きファイル名を探す試行回数の上限。
    /// これを超えるのは、同名ファイルが大量にあるか設定が不正な場合。
    /// </summary>
    private const int MaxSequence = 9999;

    public const string Extension = ".png";

    /// <summary>
    /// 画像を PNG として保存し、保存先のフルパスを返す。
    /// 失敗した場合は例外を投げる（呼び出し側で通知する）。
    /// </summary>
    public string Save(BitmapSource image, OutputSettings output, DateTime timestamp)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);

        var folder = output.EffectiveFolder;

        // 保存先が無ければ作る（確定仕様書 4.7.3）。
        Directory.CreateDirectory(folder);

        var path = ResolveAvailablePath(folder, output.FileNameTemplate, timestamp);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        // 書き込み途中で失敗した中途半端なファイルを残さないよう、
        // 一度メモリ上で PNG に変換してから一気に書き出す。
        using var buffer = new MemoryStream();
        encoder.Save(buffer);

        File.WriteAllBytes(path, buffer.ToArray());

        return path;
    }

    /// <summary>
    /// テンプレートを展開し、まだ存在しないファイル名を決める
    /// （確定仕様書 4.7.6）。
    /// </summary>
    internal static string ResolveAvailablePath(string folder, string template, DateTime timestamp)
    {
        if (FileNameTemplate.ContainsSequence(template))
        {
            // テンプレートが連番を含む場合は、その連番を進めて空きを探す。
            for (var sequence = 1; sequence <= MaxSequence; sequence++)
            {
                var candidate = BuildPath(folder, template, timestamp, sequence);
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        else
        {
            var baseName = FileNameTemplate.Sanitize(
                FileNameTemplate.Expand(template, timestamp, sequence: 1));

            var first = Path.Combine(folder, baseName + Extension);
            if (!File.Exists(first))
            {
                return first;
            }

            // 連番トークンが無い場合は末尾に _001 から付与する（確定仕様書 4.7.6）。
            for (var sequence = 1; sequence <= MaxSequence; sequence++)
            {
                var suffixed = $"{baseName}_{sequence.ToString("000", CultureInfo.InvariantCulture)}";
                var candidate = Path.Combine(folder, suffixed + Extension);
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new IOException(
            $"空きファイル名が見つかりませんでした。同名のファイルが {MaxSequence} 個以上あります。: {folder}");
    }

    private static string BuildPath(string folder, string template, DateTime timestamp, int sequence)
    {
        var name = FileNameTemplate.Sanitize(FileNameTemplate.Expand(template, timestamp, sequence));
        return Path.Combine(folder, name + Extension);
    }
}
