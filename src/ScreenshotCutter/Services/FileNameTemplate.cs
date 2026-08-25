using System.IO;
using System.Globalization;
using System.Text;

namespace ScreenshotCutter.Services;

/// <summary>
/// ファイル名テンプレートの展開（確定仕様書 4.7.6）。
/// </summary>
/// <remarks>
/// <c>{}</c> の中身を .NET の日付書式指定子として解釈し、撮影日時で展開する。
/// <c>{seq}</c> のみ特別扱いで連番に展開する（<c>{seq:000}</c> でゼロ埋め）。
/// </remarks>
public static class FileNameTemplate
{
    /// <summary>連番トークンの名前。</summary>
    public const string SequenceToken = "seq";

    /// <summary>展開結果が空になった場合のフォールバック名。</summary>
    public const string FallbackName = "ScreenShot";

    private const char PlaceholderChar = '_';

    /// <summary>Windows が予約しているデバイス名。</summary>
    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>テンプレートに連番トークンが含まれているか。</summary>
    public static bool ContainsSequence(string template)
    {
        foreach (var token in EnumerateTokens(template))
        {
            if (IsSequenceToken(token))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// テンプレートを展開する。拡張子は付けない。
    /// 不正な書式のトークンは、そのままの文字列として出力する
    /// （撮影自体は失敗させない）。
    /// </summary>
    public static string Expand(string template, DateTime timestamp, int sequence)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var result = new StringBuilder(template.Length + 16);
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            if (open < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            result.Append(template, index, open - index);

            var close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                // 閉じ括弧がない場合は残り全部をリテラルとして扱う。
                result.Append(template, open, template.Length - open);
                break;
            }

            var content = template.Substring(open + 1, close - open - 1);
            result.Append(ExpandToken(content, timestamp, sequence));
            index = close + 1;
        }

        return result.ToString();
    }

    private static string ExpandToken(string content, DateTime timestamp, int sequence)
    {
        if (content.Length == 0)
        {
            // 中身が無いトークンは意味を持たないためリテラルとして残す。
            return "{}";
        }

        if (IsSequenceToken(content))
        {
            var separator = content.IndexOf(':');
            if (separator < 0)
            {
                return sequence.ToString(CultureInfo.InvariantCulture);
            }

            var format = content[(separator + 1)..];
            try
            {
                return sequence.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return sequence.ToString(CultureInfo.InvariantCulture);
            }
        }

        try
        {
            // 1 文字だけの指定は「標準書式指定子」と解釈されてしまう
            // （例: "d" は 2026/08/25 のようにスラッシュを含む短い日付）。
            // カスタム書式として扱うため "%" を前置する。
            var format = content.Length == 1 ? "%" + content : content;
            return timestamp.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return "{" + content + "}";
        }
    }

    private static bool IsSequenceToken(string content)
    {
        if (!content.StartsWith(SequenceToken, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // "seq" ちょうど、または "seq:書式" のみを連番として扱う。
        // "seqX" のような別トークンを誤って拾わないようにする。
        return content.Length == SequenceToken.Length
               || content[SequenceToken.Length] == ':';
    }

    /// <summary>
    /// ファイル名として使えない文字を <c>_</c> に置換し、
    /// 末尾の空白とピリオドを取り除く（確定仕様書 4.7.6）。
    /// </summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return FallbackName;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? PlaceholderChar : c);
        }

        // Windows は末尾の空白・ピリオドを持つ名前を作れない。
        var sanitized = builder.ToString().TrimEnd(' ', '.');

        if (sanitized.Length == 0)
        {
            return FallbackName;
        }

        // CON や LPT1 などの予約名はファイル名にできないため退避させる。
        if (Array.Exists(
                ReservedNames,
                reserved => string.Equals(sanitized, reserved, StringComparison.OrdinalIgnoreCase)))
        {
            sanitized = PlaceholderChar + sanitized;
        }

        return sanitized;
    }

    /// <summary>
    /// 設定画面での入力時警告に使う検証（確定仕様書 4.7.6）。
    /// 保存自体は置換して続行するため、ここでの結果はあくまで警告用。
    /// </summary>
    public static bool TryValidate(string template, out string? warning)
    {
        warning = null;

        if (string.IsNullOrWhiteSpace(template))
        {
            warning = "ファイル名が空です。既定の名前が使われます。";
            return false;
        }

        foreach (var token in EnumerateTokens(template))
        {
            if (token.Length == 0)
            {
                warning = "中身が空の {} があります。";
                return false;
            }

            if (IsSequenceToken(token))
            {
                continue;
            }

            // .NET はほとんどの文字列を日付書式として受け入れるため、
            // ここで弾けるのは末尾の \ や閉じていない引用符などに限られる。
            try
            {
                var format = token.Length == 1 ? "%" + token : token;
                _ = DateTime.Now.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                warning = $"日付書式として解釈できないトークンがあります（{{{token}}}）。";
                return false;
            }
        }

        // 禁止文字はテンプレートそのものではなく展開結果で判定する。
        // {seq:000} や {HH:mm} の ':' はトークン構文の一部であって
        // ファイル名に出るわけではないため、テンプレートを直接見ると誤検出する。
        // 逆に {yyyy/MM} のように展開してはじめて現れる禁止文字も拾える。
        var expanded = Expand(template, DateTime.Now, sequence: 1);

        var invalidChars = new SortedSet<char>();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            if (!char.IsControl(c) && expanded.Contains(c))
            {
                invalidChars.Add(c);
            }
        }

        if (invalidChars.Count > 0)
        {
            warning = $"ファイル名に使えない文字が含まれています（{string.Join(' ', invalidChars)}）。保存時は _ に置き換えられます。";
            return false;
        }

        return true;
    }

    /// <summary>テンプレート中の <c>{...}</c> の中身を順に返す。</summary>
    private static IEnumerable<string> EnumerateTokens(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            yield break;
        }

        var index = 0;
        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            if (open < 0)
            {
                yield break;
            }

            var close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                yield break;
            }

            yield return template.Substring(open + 1, close - open - 1);
            index = close + 1;
        }
    }
}
