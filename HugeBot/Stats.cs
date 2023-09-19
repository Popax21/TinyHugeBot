using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

#if STATS
public partial class MyBot {
    private const MethodImplOptions MImpl = MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization;

    private struct StatsTracker {
        public readonly Stopwatch SearchTimer = new Stopwatch();
        public int NumNodes = 0;
        private int prevNumNodes = 0, prevPrevNumNodes = 0;

        public readonly long ElapsedMs => SearchTimer.ElapsedMilliseconds;

        public StatsTracker() {}

        public void ResetHistory() => NumNodes = prevNumNodes = prevPrevNumNodes = 0;

        public void StartSearch(bool resetHistory = false) {
            if(resetHistory) ResetHistory();

            //Reset node counter
            prevPrevNumNodes = prevNumNodes;
            prevNumNodes = NumNodes;
            NumNodes = 0;

            //Start the timer
            SearchTimer.Restart();
        }

        public void EndSearch() => SearchTimer.Stop();

        public void DumpStats(string prefix = "") {
            Console.WriteLine(prefix + $" - nodes: {NumNodes}");
            Console.WriteLine(prefix + $" - NPS: {(NumNodes / SearchTimer.Elapsed.TotalSeconds).ToString("F4", CultureInfo.InvariantCulture)}");
            if(prevNumNodes != 0) {
                Console.Write(prefix + $" - EBF: p1 {((double) NumNodes / prevNumNodes).ToString("F8", CultureInfo.InvariantCulture)}");
                if(prevPrevNumNodes != 0) Console.Write($" p2 {((double) Math.Sqrt(NumNodes / prevPrevNumNodes)).ToString("F8", CultureInfo.InvariantCulture)}");
                Console.WriteLine();
            }
        }
    };

    private StatsTracker globalStats = new StatsTracker(), depthStats = new StatsTracker();

    private void STAT_StartGlobalSearch() => globalStats.StartSearch(resetHistory: true);
    private void STAT_EndGlobalSearch(int maxDepth) {
        globalStats.EndSearch();
        Console.WriteLine($"Finished global search in {globalStats.ElapsedMs}ms, reached depth {maxDepth}");
        globalStats.DumpStats();
    }

    private void STAT_StartDepthSearch(int depth) => depthStats.StartSearch(resetHistory: depth <= 1);
    private void STAT_EndDepthSearch(int depth, bool didTimeOut) {
        depthStats.EndSearch();
        Console.WriteLine($"> Finished search to depth {depth} in {depthStats.ElapsedMs}ms{(didTimeOut ? " (timeout)" : "")}");
        depthStats.DumpStats("  ");
    }

    [MethodImpl(MImpl)] private void STAT_NewNode_I() {
        globalStats.NumNodes++;
        depthStats.NumNodes++;
    }

}
#endif