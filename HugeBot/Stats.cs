using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using ChessChallenge.API;

#if STATS
public partial class MyBot {
    private const MethodImplOptions MImpl = MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization;

    private struct StatsTracker {
        public readonly Stopwatch SearchTimer = new Stopwatch();
        public readonly long ElapsedMs => SearchTimer.ElapsedMilliseconds;

        public int NumNodes = 0;
        private int prevNumNodes = 0, prevPrevNumNodes = 0;

        private int numNestedEbfP1s = 0, numNestedEbfP2s = 0;
        private double nestedEbfP1Sum = 0, nestedEbfP2Sum = 0;
        public double EBF_P1 => prevNumNodes > 0 ? (double) NumNodes / prevNumNodes : 0;
        public double EBF_P2 => prevPrevNumNodes > 0 ? Math.Sqrt((double) NumNodes / prevPrevNumNodes) : 0;

        public int TTMisses = 0, TTDepthMisses = 0, TTBoundMisses = 0, TTHits = 0;

        public StatsTracker() {}

        public void ResetHistory() => NumNodes = prevNumNodes = prevPrevNumNodes = 0;

        public void StartSearch(bool resetHistory = false) {
            if(resetHistory) ResetHistory();

            //Reset counters
            prevPrevNumNodes = prevNumNodes;
            prevNumNodes = NumNodes;
            NumNodes = 0;

            numNestedEbfP1s = numNestedEbfP2s = 0;
            nestedEbfP1Sum = nestedEbfP2Sum = 0;

            TTMisses = TTDepthMisses = TTBoundMisses = TTHits = 0;

            //Start the timer
            SearchTimer.Restart();
        }

        public void EndSearch() => SearchTimer.Stop();

        public void UpdateGlobalStats(in StatsTracker nestedTracker) {
            //Accumulate counters
            NumNodes += nestedTracker.NumNodes;

            TTMisses += nestedTracker.TTMisses;
            TTDepthMisses += nestedTracker.TTDepthMisses;
            TTBoundMisses += nestedTracker.TTBoundMisses;
            TTHits += nestedTracker.TTHits;

            //Update average EBFs
            if(nestedTracker.EBF_P1 > 0) {
                numNestedEbfP1s++;
                nestedEbfP1Sum += nestedTracker.EBF_P1;
            }
            if(nestedTracker.EBF_P2 > 0) {
                numNestedEbfP2s++;
                nestedEbfP2Sum += nestedTracker.EBF_P2;
            }
        }

        public void DumpStats(string prefix = "") {
            //Node stats
            Console.WriteLine(prefix + $" - nodes searched: {NumNodes}");
            Console.WriteLine(prefix + $" - NPS: {(NumNodes / SearchTimer.Elapsed.TotalSeconds).ToString("F4", CultureInfo.InvariantCulture)}");

            //EBF stats
            if(prevNumNodes != 0) {
                Console.Write(prefix + $" - EBF: p1 {EBF_P1.ToString("F8", CultureInfo.InvariantCulture)}");
                if(prevPrevNumNodes != 0) Console.Write($" p2 {EBF_P2.ToString("F8", CultureInfo.InvariantCulture)}");
                Console.WriteLine();
            } else if(numNestedEbfP1s > 0) {
                Console.Write(prefix + $" - average EBF: p1 {(nestedEbfP1Sum / numNestedEbfP1s).ToString("F8", CultureInfo.InvariantCulture)}");
                if(numNestedEbfP2s != 0) Console.Write($" p2 {(nestedEbfP2Sum / numNestedEbfP2s).ToString("F8", CultureInfo.InvariantCulture)}");
                Console.WriteLine();
            }

            //TT stats
            int numTTAccesses = TTMisses + TTDepthMisses + TTBoundMisses + TTHits;
            string FormatTTStat(int stat) => $"{stat} ({((double) stat / numTTAccesses * 100.0).ToString("F3", CultureInfo.InvariantCulture)}%)";
            Console.WriteLine(prefix + $" - TT: accesses {numTTAccesses} misses {FormatTTStat(TTMisses)} depth misses {FormatTTStat(TTDepthMisses)} bound misses {FormatTTStat(TTBoundMisses)} hits {FormatTTStat(TTHits)}");
        }
    };

    private StatsTracker globalStats = new StatsTracker(), depthStats = new StatsTracker();

    private void STAT_StartGlobalSearch() => globalStats.StartSearch(resetHistory: true);
    private void STAT_EndGlobalSearch(Move bestMove, int bestMoveEval, int maxDepth) {
        globalStats.EndSearch();

        Console.WriteLine($"Finished global search in {globalStats.ElapsedMs}ms, reached depth {maxDepth}");
        Console.WriteLine($" - best move: {bestMove.ToString()[7..^1]} ({bestMoveEval})");
        globalStats.DumpStats();
    }

    private void STAT_StartDepthSearch(int depth) => depthStats.StartSearch(resetHistory: depth <= 1);
    private void STAT_EndDepthSearch(Move bestMove, int bestMoveEval, int depth, bool didTimeOut) {
        depthStats.EndSearch();
        globalStats.UpdateGlobalStats(in depthStats);

        Console.WriteLine($"> Finished search to depth {depth} in {depthStats.ElapsedMs}ms{(didTimeOut ? " (timeout)" : "")}");
        if(!didTimeOut) Console.WriteLine($"   - best move: {bestMove.ToString()[7..^1]} ({bestMoveEval})");
        depthStats.DumpStats("  ");
    }

    [MethodImpl(MImpl)] private void STAT_NewNode_I() => depthStats.NumNodes++;

    [MethodImpl(MImpl)] private void STAT_TT_Miss_I() => depthStats.TTMisses++;
    [MethodImpl(MImpl)] private void STAT_TT_DepthMiss_I() => depthStats.TTDepthMisses++;
    [MethodImpl(MImpl)] private void STAT_TT_BoundMiss_I() => depthStats.TTBoundMisses++;
    [MethodImpl(MImpl)] private void STAT_TT_Hit_I() => depthStats.TTHits++;
}
#endif