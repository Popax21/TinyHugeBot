using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using ChessChallenge.API;

#if STATS
public partial class MyBot {
    private const MethodImplOptions StatMImpl = MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization;

    private delegate void StatPrinter(string stat, int indentLevel = 0);
    private static StatPrinter IncrIndent(StatPrinter printer) => (stat, indentLvl) => printer(stat, indentLvl+1);

    private static string FormatFloat(double val, int decs) => val.ToString($"F{decs}", CultureInfo.InvariantCulture);
    private static string FormatPercentageI(int num, int total) => $"{num} ({FormatFloat((double) num / total * 100.0, 3)}%)";
    private static string FormatPercentageF(double num, double total, int decs) => $"{FormatFloat(num, decs)} ({FormatFloat((double) num / total * 100.0, 3)}%)";

    private struct StatsTracker {
        public long ElapsedMs;

        //Node stats
        public int NumNodes;
        private int prevNumNodes, prevPrevNumNodes;

        //EBF stats
        private int numNestedEbfP1s, numNestedEbfP2s;
        private double nestedEbfP1Sum, nestedEbfP2Sum;
        public double EBF_P1 => prevNumNodes > 0 ? (double) NumNodes / prevNumNodes : 0;
        public double EBF_P2 => prevPrevNumNodes > 0 ? Math.Sqrt((double) NumNodes / prevPrevNumNodes) : 0;

        //Window-type stats
        public struct WindowStatCounters {
            public int NumNodes;

            //Alpha-Beta stats
            public int AlphaBeta_NumSearchedNodes, AlphaBeta_NumGeneratedMoves, AlphaBeta_NumSearchedMoves;
            public int AlphaBeta_NumFailLows, AlphaBeta_NumFailHighs;

            public void UpdateGlobalStats(in WindowStatCounters nestedTracker) {
                NumNodes += nestedTracker.NumNodes;

                AlphaBeta_NumSearchedNodes += nestedTracker.AlphaBeta_NumSearchedNodes;
                AlphaBeta_NumGeneratedMoves += nestedTracker.AlphaBeta_NumGeneratedMoves;
                AlphaBeta_NumSearchedMoves += nestedTracker.AlphaBeta_NumSearchedMoves;
                AlphaBeta_NumFailLows += nestedTracker.AlphaBeta_NumFailLows;
                AlphaBeta_NumFailHighs += nestedTracker.AlphaBeta_NumFailHighs;
            }

            public void DumpStats(in StatsTracker stats, StatPrinter printStat) {
                printStat($"nodes: {FormatPercentageI(NumNodes, stats.NumNodes)}");

                //Alpha-Beta stats
                double avgGeneratedMoves = (double) AlphaBeta_NumGeneratedMoves / AlphaBeta_NumSearchedNodes, avgSearchedMoves = (double) AlphaBeta_NumSearchedMoves / AlphaBeta_NumSearchedNodes;
                printStat($"alpha-beta: nodes {FormatPercentageI(AlphaBeta_NumSearchedNodes, NumNodes)} avg. generated moves {FormatFloat(avgGeneratedMoves, 4)} avg. searched moves {FormatPercentageF(avgSearchedMoves, avgGeneratedMoves, 4)}");
                printStat($"cutoffs: fail-lows {FormatPercentageI(AlphaBeta_NumFailLows, AlphaBeta_NumSearchedNodes)} fail-highs {FormatPercentageI(AlphaBeta_NumFailHighs, AlphaBeta_NumSearchedNodes)}");
            }
        }
        public WindowStatCounters OpenWindowStats, ZeroWindowStats;

        //TT stats
        public int TTRead_Misses, TTRead_DepthMisses, TTRead_BoundMisses, TTRead_Hits;
        public int TTWrite_NewSlots, TTWrite_SlotUpdates, TTWrite_IdxCollisions;

        //PVS stats
        public int PVS_NumPVMoves, PVS_PVMoveIdxSum, PVS_NumResearches, PVS_NumCorrections;

        public void Reset(bool resetHistory = false) {
            if(resetHistory) NumNodes = prevNumNodes = prevPrevNumNodes = 0;

            //Reset counters
            prevPrevNumNodes = prevNumNodes;
            prevNumNodes = NumNodes;
            NumNodes = 0;

            OpenWindowStats = ZeroWindowStats = default;

            numNestedEbfP1s = numNestedEbfP2s = 0;
            nestedEbfP1Sum = nestedEbfP2Sum = 0;

            TTRead_Misses = TTRead_DepthMisses = TTRead_BoundMisses = TTRead_Hits = 0;
            TTWrite_NewSlots = TTWrite_SlotUpdates = TTWrite_IdxCollisions = 0;

            PVS_NumPVMoves = PVS_PVMoveIdxSum = PVS_NumResearches = PVS_NumCorrections = 0;
        }

        public void UpdateGlobalStats(in StatsTracker nestedTracker) {
            //Accumulate counters
            ElapsedMs += nestedTracker.ElapsedMs;
            NumNodes += nestedTracker.NumNodes;

            OpenWindowStats.UpdateGlobalStats(in nestedTracker.OpenWindowStats);
            ZeroWindowStats.UpdateGlobalStats(in nestedTracker.ZeroWindowStats);

            TTRead_Misses += nestedTracker.TTRead_Misses;
            TTRead_DepthMisses += nestedTracker.TTRead_DepthMisses;
            TTRead_BoundMisses += nestedTracker.TTRead_BoundMisses;
            TTRead_Hits += nestedTracker.TTRead_Hits;

            TTWrite_NewSlots += nestedTracker.TTWrite_NewSlots;
            TTWrite_SlotUpdates += nestedTracker.TTWrite_SlotUpdates;
            TTWrite_IdxCollisions += nestedTracker.TTWrite_IdxCollisions;

            PVS_NumPVMoves += nestedTracker.PVS_NumPVMoves;
            PVS_PVMoveIdxSum += nestedTracker.PVS_PVMoveIdxSum;
            PVS_NumResearches += nestedTracker.PVS_NumResearches;
            PVS_NumCorrections += nestedTracker.PVS_NumCorrections;

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

        public void DumpStats(StatPrinter printStat) {
            //Node stats
            printStat($"total nodes: {NumNodes}");
            printStat($"NPS: {FormatFloat(NumNodes * 1000.0 / ElapsedMs, 4)}");

            //EBF stats
            if(prevNumNodes != 0) {
                if(prevPrevNumNodes != 0) printStat($"EBF: p1 {FormatFloat(EBF_P1, 8)} p2 {FormatFloat(EBF_P2, 8)}");
                else printStat($"EBF: p1 {FormatFloat(EBF_P1, 8)}");
            } else if(numNestedEbfP1s > 0) {
                if(numNestedEbfP2s != 0) printStat($"average EBF: p1 {FormatFloat(nestedEbfP1Sum / numNestedEbfP1s, 8)} p2 {FormatFloat(nestedEbfP2Sum / numNestedEbfP2s, 8)}");
                else printStat($"average EBF: p1 {FormatFloat(nestedEbfP1Sum / numNestedEbfP1s, 8)}");
            }

            //Window-type stats
            printStat("open-window stats:");
            OpenWindowStats.DumpStats(in this, IncrIndent(printStat));
            printStat("zero-window stats:");
            ZeroWindowStats.DumpStats(in this, IncrIndent(printStat));

            //TT stats
            int numTTReads = TTRead_Misses + TTRead_DepthMisses + TTRead_BoundMisses + TTRead_Hits;
            printStat($"TT reads: total {numTTReads} misses {FormatPercentageI(TTRead_Misses, numTTReads)} depth misses {FormatPercentageI(TTRead_DepthMisses, numTTReads)} bound misses {FormatPercentageI(TTRead_BoundMisses, numTTReads)} hits {FormatPercentageI(TTRead_Hits, numTTReads)}");

            int numTTWrites = TTWrite_NewSlots + TTWrite_SlotUpdates + TTWrite_IdxCollisions;
            printStat($"TT writes: total {numTTWrites} new slots {FormatPercentageI(TTWrite_NewSlots, numTTWrites)} slot updates {FormatPercentageI(TTWrite_SlotUpdates, numTTWrites)} idx collisions {FormatPercentageI(TTWrite_IdxCollisions, numTTWrites)}");

            //PVS stats
            printStat($"PVS: PV moves {FormatPercentageI(PVS_NumPVMoves, OpenWindowStats.AlphaBeta_NumSearchedMoves)} avg. PV move idx {FormatFloat((double) PVS_PVMoveIdxSum / PVS_NumPVMoves, 4)} researches {FormatPercentageI(PVS_NumResearches, PVS_NumPVMoves)} anomalies {FormatPercentageI(PVS_NumResearches - PVS_NumCorrections, PVS_NumResearches)}");
        }
    };

    private int depthSearchStartMs;
    private StatsTracker globalStats = new StatsTracker(), depthStats = new StatsTracker();

    private static void PrintStat(string stat, int indentLvl = 0) => Console.WriteLine(new string(' ', indentLvl * 2) + " - " + stat);

    private void STAT_StartGlobalSearch() => globalStats.Reset(resetHistory: true);
    private void STAT_EndGlobalSearch(Move bestMove, int bestMoveEval, int maxDepth) {
        Console.WriteLine($"Finished global search in {depthStats.ElapsedMs}ms (total: {searchTimer.MillisecondsElapsedThisTurn}ms), reached depth {maxDepth}");
        PrintStat($"best move: {bestMove.ToString()[7..^1]} ({bestMoveEval})");
        globalStats.DumpStats(PrintStat);
    }

    private void STAT_StartDepthSearch(int depth) {
        depthSearchStartMs = searchTimer.MillisecondsElapsedThisTurn;
        depthStats.Reset(resetHistory: depth <= 1);
    }

    private void STAT_EndDepthSearch(Move bestMove, int bestMoveEval, int depth, bool didTimeOut) {
        depthStats.ElapsedMs = searchTimer.MillisecondsElapsedThisTurn - depthSearchStartMs;
        if(!didTimeOut) globalStats.UpdateGlobalStats(in depthStats);

        Console.WriteLine($"> Finished search to depth {depth} in {depthStats.ElapsedMs}ms{(didTimeOut ? " (timeout)" : "")}");
        if(!didTimeOut) PrintStat($"best move: {bestMove.ToString()[7..^1]} ({bestMoveEval})", 1);
        depthStats.DumpStats(IncrIndent(PrintStat));
    }

    [MethodImpl(StatMImpl)] private void STAT_NewNode_I(bool isZw) {
        depthStats.NumNodes++;
        (!isZw ? ref depthStats.OpenWindowStats : ref depthStats.ZeroWindowStats).NumNodes++;
    }

    [MethodImpl(StatMImpl)] private void STAT_TTRead_Miss_I() => depthStats.TTRead_Misses++;
    [MethodImpl(StatMImpl)] private void STAT_TTRead_DepthMiss_I() => depthStats.TTRead_DepthMisses++;
    [MethodImpl(StatMImpl)] private void STAT_TTRead_BoundMiss_I() => depthStats.TTRead_BoundMisses++;
    [MethodImpl(StatMImpl)] private void STAT_TTRead_Hit_I() => depthStats.TTRead_Hits++;

    [MethodImpl(StatMImpl)] private void STAT_TTWrite_NewSlot_I() => depthStats.TTWrite_NewSlots++;
    [MethodImpl(StatMImpl)] private void STAT_TTWrite_SlotUpdate_I() => depthStats.TTWrite_SlotUpdates++;
    [MethodImpl(StatMImpl)] private void STAT_TTWrite_IdxCollision_I() => depthStats.TTWrite_IdxCollisions++;

    [MethodImpl(StatMImpl)] private void STAT_AlphaBeta_SearchNode_I(bool isZw, int numMoves) {
        ref StatsTracker.WindowStatCounters tracker = ref !isZw ? ref depthStats.OpenWindowStats : ref depthStats.ZeroWindowStats;
        tracker.AlphaBeta_NumSearchedNodes++;
        tracker.AlphaBeta_NumGeneratedMoves += numMoves;
    }
    [MethodImpl(StatMImpl)] private void STAT_AlphaBeta_SearchedMove_I(bool isZw) => (!isZw ? ref depthStats.OpenWindowStats : ref depthStats.ZeroWindowStats).AlphaBeta_NumSearchedMoves++;
    [MethodImpl(StatMImpl)] private void STAT_AlphaBeta_FailLow_I(bool isZw) => (!isZw ? ref depthStats.OpenWindowStats : ref depthStats.ZeroWindowStats).AlphaBeta_NumFailLows++;
    [MethodImpl(StatMImpl)] private void STAT_AlphaBeta_FailHigh_I(bool isZw) => (!isZw ? ref depthStats.OpenWindowStats : ref depthStats.ZeroWindowStats).AlphaBeta_NumFailHighs++;

    [MethodImpl(StatMImpl)] private void STAT_PVS_Research_I() => depthStats.PVS_NumResearches++;
    [MethodImpl(StatMImpl)] private void STAT_PVS_FoundPVMove_I(int moveIdx, bool hadPVMove) {
        depthStats.PVS_NumPVMoves++;
        depthStats.PVS_PVMoveIdxSum += moveIdx;
        if(hadPVMove) depthStats.PVS_NumCorrections++;
    }
}
#endif