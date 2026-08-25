using ScreenshotCutter.Services;

namespace ScreenshotCutter.Tests;

/// <summary>
/// ファイル名テンプレートの展開（確定仕様書 4.7.6）。
/// </summary>
public class FileNameTemplateTests
{
    private static readonly DateTime Sample = new(2026, 8, 25, 14, 30, 52, DateTimeKind.Local);

    // ------------------------------------------------------------ Expand

    [Fact]
    public void Expand_既定のテンプレートを日時で置き換える()
    {
        var result = FileNameTemplate.Expand("ScreenShot_{yyyyMMdd}_{HHmmss}", Sample, sequence: 1);

        Assert.Equal("ScreenShot_20260825_143052", result);
    }

    [Theory]
    [InlineData("{yyyy}", "2026")]
    [InlineData("{MM}", "08")]
    [InlineData("{dd}", "25")]
    [InlineData("{HH}", "14")]
    [InlineData("{mm}", "30")]
    [InlineData("{ss}", "52")]
    [InlineData("{yyyy-MM-dd}", "2026-08-25")]
    public void Expand_個々の日付トークン(string template, string expected)
    {
        Assert.Equal(expected, FileNameTemplate.Expand(template, Sample, sequence: 1));
    }

    [Fact]
    public void Expand_1文字の指定もカスタム書式として扱う()
    {
        // "%" を前置しないと標準書式指定子と解釈され、
        // "d" が 2026/08/25 のようにスラッシュ入りになってしまう。
        var result = FileNameTemplate.Expand("{d}", Sample, sequence: 1);

        Assert.Equal("25", result);
        Assert.DoesNotContain('/', result);
    }

    [Fact]
    public void Expand_トークン以外の文字はそのまま残る()
    {
        var result = FileNameTemplate.Expand("cap-{yyyy}-end", Sample, sequence: 1);

        Assert.Equal("cap-2026-end", result);
    }

    [Fact]
    public void Expand_トークンが無ければ入力をそのまま返す()
    {
        Assert.Equal("plain-name", FileNameTemplate.Expand("plain-name", Sample, sequence: 1));
    }

    [Fact]
    public void Expand_空文字は空文字を返す()
    {
        Assert.Equal(string.Empty, FileNameTemplate.Expand(string.Empty, Sample, sequence: 1));
    }

    // -------------------------------------------------------- 連番トークン

    [Fact]
    public void Expand_seqは桁数指定なしの連番になる()
    {
        Assert.Equal("shot-7", FileNameTemplate.Expand("shot-{seq}", Sample, sequence: 7));
    }

    [Fact]
    public void Expand_seqは書式指定でゼロ埋めできる()
    {
        Assert.Equal("shot-007", FileNameTemplate.Expand("shot-{seq:000}", Sample, sequence: 7));
    }

    [Fact]
    public void Expand_seqは大文字小文字を区別しない()
    {
        Assert.Equal("3", FileNameTemplate.Expand("{SEQ}", Sample, sequence: 3));
    }

    [Fact]
    public void Expand_seqXは連番として扱わない()
    {
        // "seq" で始まるだけの別トークンを誤って連番にしない。
        // 連番以外はすべて日付書式として解釈されるため、
        // 's'(秒) + "eqX" が展開された結果になる。
        var result = FileNameTemplate.Expand("{seqX}", Sample, sequence: 5);

        Assert.Equal("52eqX", result);
        Assert.NotEqual("5", result);
    }

    [Theory]
    [InlineData("{seq}", true)]
    [InlineData("{seq:0000}", true)]
    [InlineData("a{seq}b", true)]
    [InlineData("{yyyyMMdd}", false)]
    [InlineData("plain", false)]
    [InlineData("{seqX}", false)]
    [InlineData("", false)]
    public void ContainsSequence_連番トークンの有無を判定する(string template, bool expected)
    {
        Assert.Equal(expected, FileNameTemplate.ContainsSequence(template));
    }

    // -------------------------------------------------------- 不正な入力

    [Theory]
    [InlineData("{'unterminated}")]
    [InlineData("{\\}")]
    public void Expand_解釈できない書式のトークンはそのまま残す(string template)
    {
        // 撮影自体を失敗させないため、例外にせずリテラルとして出力する。
        var result = FileNameTemplate.Expand(template, Sample, sequence: 1);

        Assert.Equal(template, result);
    }

    [Fact]
    public void Expand_未知の1文字はリテラルとして出力される()
    {
        // 1 文字のトークンは "%" を前置してカスタム書式として扱う。
        // その副作用で、日付要素でない 1 文字はそのまま文字として出る。
        Assert.Equal("q", FileNameTemplate.Expand("{q}", Sample, sequence: 1));
    }

    [Fact]
    public void Expand_連番以外のトークンはすべて日付書式として解釈される()
    {
        // .NET はほとんどの文字列を日付書式として受け入れる。
        // 'n' 'o' 't' などが日付要素として置換される点に注意。
        var result = FileNameTemplate.Expand("{not-a-format}", Sample, sequence: 1);

        Assert.Equal("noP-a-0or30aP", result);
    }

    [Fact]
    public void Expand_閉じ括弧が無い場合は残りをリテラル扱いする()
    {
        var result = FileNameTemplate.Expand("shot-{yyyy", Sample, sequence: 1);

        Assert.Equal("shot-{yyyy", result);
    }

    [Fact]
    public void Expand_空のトークンはリテラルとして残る()
    {
        var result = FileNameTemplate.Expand("a{}b", Sample, sequence: 1);

        Assert.Equal("a{}b", result);
    }

    // ---------------------------------------------------------- Sanitize

    [Theory]
    [InlineData("a/b", "a_b")]
    [InlineData("a\\b", "a_b")]
    [InlineData("a:b", "a_b")]
    [InlineData("a*b", "a_b")]
    [InlineData("a?b", "a_b")]
    [InlineData("a\"b", "a_b")]
    [InlineData("a<b", "a_b")]
    [InlineData("a>b", "a_b")]
    [InlineData("a|b", "a_b")]
    public void Sanitize_使用できない文字をアンダースコアに置き換える(string input, string expected)
    {
        Assert.Equal(expected, FileNameTemplate.Sanitize(input));
    }

    [Fact]
    public void Sanitize_使用できる文字は変更しない()
    {
        const string name = "ScreenShot_20260825_143052";

        Assert.Equal(name, FileNameTemplate.Sanitize(name));
    }

    [Fact]
    public void Sanitize_日本語はそのまま通す()
    {
        Assert.Equal("スクリーンショット", FileNameTemplate.Sanitize("スクリーンショット"));
    }

    [Theory]
    [InlineData("name.", "name")]
    [InlineData("name ", "name")]
    [InlineData("name. . ", "name")]
    public void Sanitize_末尾の空白とピリオドを取り除く(string input, string expected)
    {
        // Windows は末尾に空白やピリオドを持つ名前を作れない。
        Assert.Equal(expected, FileNameTemplate.Sanitize(input));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("LPT1")]
    [InlineData("NUL")]
    public void Sanitize_予約デバイス名は退避させる(string reserved)
    {
        var result = FileNameTemplate.Sanitize(reserved);

        Assert.NotEqual(reserved, result);
        Assert.EndsWith(reserved, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_予約名を含むだけの名前は変更しない()
    {
        Assert.Equal("CONTROL", FileNameTemplate.Sanitize("CONTROL"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Sanitize_空になる入力は既定名にフォールバックする(string input)
    {
        Assert.Equal(FileNameTemplate.FallbackName, FileNameTemplate.Sanitize(input));
    }

    // ------------------------------------------------------- TryValidate

    [Theory]
    [InlineData("ScreenShot_{yyyyMMdd}_{HHmmss}")]
    [InlineData("shot-{seq:000}")]
    [InlineData("plain")]
    [InlineData("{yyyy}-{MM}-{dd}_{seq}")]
    public void TryValidate_正しいテンプレートは警告なし(string template)
    {
        var valid = FileNameTemplate.TryValidate(template, out var warning);

        Assert.True(valid);
        Assert.Null(warning);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("a?b")]
    public void TryValidate_使用できない文字を警告する(string template)
    {
        var valid = FileNameTemplate.TryValidate(template, out var warning);

        Assert.False(valid);
        Assert.NotNull(warning);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_空のテンプレートを警告する(string template)
    {
        Assert.False(FileNameTemplate.TryValidate(template, out var warning));
        Assert.NotNull(warning);
    }

    [Fact]
    public void TryValidate_中身が空のトークンを警告する()
    {
        Assert.False(FileNameTemplate.TryValidate("a{}b", out var warning));
        Assert.NotNull(warning);
    }

    [Fact]
    public void TryValidate_解釈できない日付書式を警告する()
    {
        Assert.False(FileNameTemplate.TryValidate("{'unterminated}", out var warning));
        Assert.NotNull(warning);
    }

    [Fact]
    public void TryValidate_連番トークンのコロンは禁止文字として扱わない()
    {
        // ':' はファイル名には使えないが、{seq:000} の ':' はトークン構文の
        // 一部であって展開結果には現れない。
        Assert.True(FileNameTemplate.TryValidate("shot-{seq:000}", out var warning));
        Assert.Null(warning);
    }

    [Fact]
    public void TryValidate_展開してはじめて現れる禁止文字も警告する()
    {
        // {yyyy/MM} はテンプレート上は問題なさそうに見えるが、
        // 展開すると 2026/08 となりファイル名に使えない。
        Assert.False(FileNameTemplate.TryValidate("{yyyy/MM}", out var warning));
        Assert.NotNull(warning);
    }

    [Fact]
    public void TryValidate_時刻書式のコロンは展開結果に現れるので警告する()
    {
        Assert.False(FileNameTemplate.TryValidate("{HH:mm}", out var warning));
        Assert.NotNull(warning);
    }
}
