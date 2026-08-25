using ScreenshotCutter.Interop;

namespace ScreenshotCutter.Services;

/// <summary>
/// 待機状態に戻るタイミングでメモリを解放する（確定仕様書 6.3）。
/// </summary>
/// <remarks>
/// 撮影 1 回で数十 MB のビットマップを扱うため、放置すると常駐中の
/// フットプリントがそのまま高止まりする。GC が回収したうえで
/// ワーキングセットを OS に返すことで、待機時のメモリを抑える。
/// </remarks>
public static class MemoryTrimmer
{
    /// <summary>
    /// 何もしていない状態に戻ったときに呼ぶ。
    /// 撮影直後の応答性を損なわないよう、呼び出し側で
    /// 通知を出し終えたあとに実行すること。
    /// </summary>
    public static void Trim()
    {
        try
        {
            // 大きなビットマップは LOH に載るため、圧縮まで指示して回収する。
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            NativeMethods.EmptyWorkingSet(NativeMethods.GetCurrentProcess());
        }
        catch (Exception ex)
        {
            // メモリ解放に失敗しても動作には影響しない。
            Logger.Error("メモリの解放に失敗しました。", ex);
        }
    }
}
