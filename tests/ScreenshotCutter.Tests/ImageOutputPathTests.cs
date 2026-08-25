using ScreenshotCutter.Services;

namespace ScreenshotCutter.Tests;

/// <summary>
/// 保存先ファイル名の決定と、名前衝突時の連番付与（確定仕様書 4.7.6）。
/// </summary>
public sealed class ImageOutputPathTests : IDisposable
{
    private static readonly DateTime Sample = new(2026, 8, 25, 14, 30, 52, DateTimeKind.Local);

    private readonly string _folder;

    public ImageOutputPathTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "ScreenshotCutterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // 一時フォルダーが消せなくてもテスト結果には影響しない。
        }
    }

    private void CreateFile(string fileName) =>
        File.WriteAllText(Path.Combine(_folder, fileName), string.Empty);

    [Fact]
    public void 衝突が無ければテンプレートどおりの名前になる()
    {
        var path = ImageOutputService.ResolveAvailablePath(_folder, "ScreenShot_{yyyyMMdd}_{HHmmss}", Sample);

        Assert.Equal(Path.Combine(_folder, "ScreenShot_20260825_143052.png"), path);
    }

    [Fact]
    public void 拡張子はpngが自動で付く()
    {
        var path = ImageOutputService.ResolveAvailablePath(_folder, "shot", Sample);

        Assert.EndsWith(".png", path, StringComparison.Ordinal);
    }

    [Fact]
    public void 連番トークンが無い場合は末尾に_001から付与する()
    {
        CreateFile("shot.png");

        var path = ImageOutputService.ResolveAvailablePath(_folder, "shot", Sample);

        Assert.Equal(Path.Combine(_folder, "shot_001.png"), path);
    }

    [Fact]
    public void 連番トークンが無い場合は空きが見つかるまで番号を進める()
    {
        CreateFile("shot.png");
        CreateFile("shot_001.png");
        CreateFile("shot_002.png");

        var path = ImageOutputService.ResolveAvailablePath(_folder, "shot", Sample);

        Assert.Equal(Path.Combine(_folder, "shot_003.png"), path);
    }

    [Fact]
    public void 連番トークンがある場合はそのトークンを進める()
    {
        CreateFile("shot-001.png");
        CreateFile("shot-002.png");

        var path = ImageOutputService.ResolveAvailablePath(_folder, "shot-{seq:000}", Sample);

        Assert.Equal(Path.Combine(_folder, "shot-003.png"), path);
    }

    [Fact]
    public void 連番トークンがある場合は1から始まる()
    {
        var path = ImageOutputService.ResolveAvailablePath(_folder, "shot-{seq}", Sample);

        Assert.Equal(Path.Combine(_folder, "shot-1.png"), path);
    }

    [Fact]
    public void 使用できない文字はアンダースコアに置き換えて保存する()
    {
        // 撮影を失敗させず、置換して続行する。
        var path = ImageOutputService.ResolveAvailablePath(_folder, "a:b*c", Sample);

        Assert.Equal(Path.Combine(_folder, "a_b_c.png"), path);
    }

    [Fact]
    public void 日時が異なれば別のファイル名になる()
    {
        const string template = "ScreenShot_{yyyyMMdd}_{HHmmss}";

        var first = ImageOutputService.ResolveAvailablePath(_folder, template, Sample);
        var second = ImageOutputService.ResolveAvailablePath(_folder, template, Sample.AddSeconds(1));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void テンプレートが空でも保存先を決められる()
    {
        var path = ImageOutputService.ResolveAvailablePath(_folder, string.Empty, Sample);

        Assert.Equal(Path.Combine(_folder, FileNameTemplate.FallbackName + ".png"), path);
    }
}
