using ScreenshotCutter.Interop;

namespace ScreenshotCutter.Services;

/// <summary>
/// exe に埋め込まれたアプリアイコンの読み込み。
/// </summary>
/// <remarks>
/// アイコンは exe のリソースから取り出すため、配布物に .ico を同梱しなくてよい。
/// </remarks>
public static class AppIcon
{
    /// <summary>
    /// .NET SDK が <c>ApplicationIcon</c> を埋め込むときのリソース ID。
    /// </summary>
    private const string MainIconResource = "#32512";

    /// <summary>
    /// 通知領域向けのサイズでアイコンを読み込む。
    /// 呼び出し側が <see cref="NativeMethods.DestroyIcon"/> で解放すること。
    /// 取得できない場合は <see cref="IntPtr.Zero"/>。
    /// </summary>
    public static IntPtr LoadSmallIcon()
    {
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON);

        if (width <= 0 || height <= 0)
        {
            width = 16;
            height = 16;
        }

        // まずはリソース ID 指定で読む。マルチサイズ ico から要求サイズに
        // 最も近いエントリが選ばれるため、拡大縮小によるにじみが出ない。
        var moduleHandle = NativeMethods.GetModuleHandle(null);
        var icon = NativeMethods.LoadImage(
            moduleHandle, MainIconResource, NativeMethods.IMAGE_ICON, width, height, 0);

        if (icon != IntPtr.Zero)
        {
            return icon;
        }

        // リソース ID が異なるビルド構成に備えた代替経路。
        // ExtractIconEx はサイズを選べないが、確実に先頭のアイコンを取れる。
        try
        {
            if (NativeMethods.ExtractIconEx(AppPaths.ExecutablePath, 0, out var large, out var small, 1) > 0)
            {
                if (large != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(large);
                }

                if (small != IntPtr.Zero)
                {
                    return small;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("アプリアイコンの読み込みに失敗しました。", ex);
        }

        return IntPtr.Zero;
    }
}
