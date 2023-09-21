using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using ChessChallenge.API;

#if STATS
public partial class MyBot {
    private const MethodImplOptions StatMImpl = MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization;

    private struct StatsTracker {
        public readonly Stopwatch SearchTimer = new Stopwatch();
        public readonly long ElapsedMs => SearchTimer.ElapsedMilliseconds;

        //Node stats
        public int NumNodes = 0;
        private int prevNumNodes = 0, prevPrevNumNodes = 0;

        //EBF stats
        private int numNestedEbfP1s = 0, numNestedEbfP2s = 0;
        private double nestedEbfP1Sum = 0, nestedEbfP2Sum = 0;
        public double EBF_P1 => prevNumNodes > 0 ? (double) NumNodes / prevNumNodes : 0;
        public double EBF_P2 => prevPrevNumNodes > 0 ? Math.Sqrt((double) NumNodes / prevPrevNumNodes) : 0;

        //TT stats
        public int TTRead_Misses = 0, TTRead_DepthMisses = 0, TTRead_BoundMisses = 0, TTRead_Hits = 0;
        public int TTWrite_NewSlots = 0, TTWrite_SlotUpdates = 0, TTWrite_IdxCollisions = 0;

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

            TTRead_Misses = TTRead_DepthMisses = TTRead_BoundMisses = TTRead_Hits = 0;
            TTWrite_NewSlots = TTWrite_SlotUpdates = TTWrite_IdxCollisions = 0;

            //Start the timer
            SearchTimer.Restart();
        }

        public void EndSearch() => SearchTimer.Stop();

        public void UpdateGlobalStats(in StatsTracker nestedTracker) {
            //Accumulate counters
            NumNodes += nestedTracker.NumNodes;

            TTRead_Misses += nestedTracker.TTRead_Misses;
            TTRead_DepthMisses += nestedTracker.TTRead_DepthMisses;
            TTRead_BoundMisses += nestedTracker.TTRead_BoundMisses;
            TTRead_Hits += nestedTracker.TTRead_Hits;

            TTWrite_NewSlots += nestedTracker.TTWrite_NewSlots;
            TTWrite_SlotUpdates += nestedTracker.TTWrite_SlotUpdates;
            TTWrite_IdxCollisions += nestedTracker.TTWrite_IdxCollisions;

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
            static string FormatTTStat(int stat, int total) => $"{stat} ({((double) stat / total * 100.0).ToString("F3", CultureInfo.InvariantCulture)}%)";

            int numTTReads = TTRead_Misses + TTRead_DepthMisses + TTRead_BoundMisses + TTRead_Hits;
            Console.WriteLine(prefix + $" - TT reads: total {numTTReads} misses {FormatTTStat(TTRead_Misses, numTTReads)} depth misses {FormatTTStat(TTRead_DepthMisses, numTTReads)} bound misses {FormatTTStat(TTRead_BoundMisses, numTTReads)} hits {FormatTTStat(TTRead_Hits, numTTReads)}");

            int numTTWrites = TTWrite_NewSlots + TTWrite_SlotUpdates + TTWrite_IdxCollisions;
            Console.WriteLine(prefix + $" - TT writes: total {numTTWrites} new slots {FormatTTStat(TTWrite_NewSlots, numTTWrites)} slot updates {FormatTTStat(TTWrite_SlotUpdates, numTTWrites)} idx collisions {FormatTTStat(TTWrite_IdxCollisions, numTTWrites)}");
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

    [MethodImpl(StatMImpl)] private void STAT_NewNode_I() => depthStats.NumNodes++;

    [MethodImpl(StatMImpl)] private void STAT_TTRead_Miss_I() => depthStats.TTRead_Misses++;
    [MethodImpl(StatMImpl)] private void STAT_TTRead_DepthMiss_I() => depthStats.TTRead_DepthMisses++;
    [MethodImpl(StatMImpl)] private void STAT_TTRead_BoundMiss_I() => depthStats.TTRead_BoundMisses++;
    [MethodImpl(StatMImpl)] private void STAT_TTRead_Hit_I() => depthStats.TTRead_Hits++;

    [MethodImpl(StatMImpl)] private void STAT_TTWrite_NewSlot_I() => depthStats.TTWrite_NewSlots++;
    [MethodImpl(StatMImpl)] private void STAT_TTWrite_SlotUpdate_I() => depthStats.TTWrite_SlotUpdates++;
    [MethodImpl(StatMImpl)] private void STAT_TTWrite_IdxCollision_I() => depthStats.TTWrite_IdxCollisions++;
}
#endif