using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

#if STATS
public partial class MyBot {
    private const MethodImplOptions MImpl = MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization;

    private struct StatsTracker {
        private readonly Stopwatch searchSw = new Stopwatch();
        private int numNodes = 0;

        public readonly long ElapsedMs => searchSw.ElapsedMilliseconds;

        public StatsTracker() {}

        public void StartSearch() {
            numNodes = 0;
            searchSw.Restart();
        }

        public void EndSearch() {
            searchSw.Stop();
        }

        [MethodImpl(MImpl)] public void NewNode_I() => numNodes++;

        public void DumpStats(string prefix = "") {
            Console.WriteLine(prefix + $" - NPS: {(numNodes / searchSw.Elapsed.TotalSeconds).ToString("F4", CultureInfo.InvariantCulture)}");
        }
    };

    private StatsTracker globalStats = new StatsTracker(), depthStats = new StatsTracker();

    private void STAT_StartGlobalSearch() => globalStats.StartSearch();
    private void STAT_EndGlobalSearch(int maxDepth) {
        globalStats.EndSearch();
        Console.WriteLine($"Finished global search in {globalStats.ElapsedMs}ms, reached depth {maxDepth}");
        globalStats.DumpStats();
    }

    private void STAT_StartDepthSearch(int depth) => depthStats.StartSearch();
    private void STAT_EndDepthSearch(int depth, bool didTimeOut) {
        depthStats.EndSearch();
        Console.WriteLine($"> Finished search to depth {depth} in {depthStats.ElapsedMs}ms{(didTimeOut ? " (timeout)" : "")}");
        depthStats.DumpStats("  ");
    }

    [MethodImpl(MImpl)] private void STAT_NewNode_I() {
        globalStats.NewNode_I();
        depthStats.NewNode_I();
    }

}
#endif