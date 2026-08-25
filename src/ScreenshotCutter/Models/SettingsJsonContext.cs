using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenshotCutter.Models;

/// <summary>
/// System.Text.Json のソース生成コンテキスト。
/// 常駐アプリのため、起動時のリフレクション初期化コストを避ける目的で使う
/// （確定仕様書 5.1）。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
