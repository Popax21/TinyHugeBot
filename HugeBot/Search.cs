using System;
using ChessChallenge.API;
using HugeBot;

public partial class MyBot : IChessBot {
    public const int MaxDepth = 63; //Limited by TT
    public const int MaxPlies = 256;

    private Board searchBoard = null!;
    private Timer searchTimer = null!;
    private int searchAbortTime;

    private int maxExtension;

    public Move Think(Board board, Timer timer) {
        //Determine search times
        searchBoard = board;
        searchTimer = timer;
        searchAbortTime = timer.MillisecondsRemaining / 20;

        int deepeningSearchTime = timer.MillisecondsRemaining / 50;

#if STATS
        //Notify the stats tracker that the search starts
        STAT_StartGlobalSearch();
#endif

        //Reset move order tables
        MoveOrder_Reset_I();

        //Generate all legal moves for later lookup
        Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves);

        static Move FindMove_I(Span<Move> moves, ushort val) {
            for(int i = 0; i < moves.Length; i++) {
                if(moves[i].RawValue == val) return moves[i];
            }
#if DEBUG
            throw new Exception($"Search returned invalid root move: 0x{val:x4}");
#else
            return default;
#endif
        }

#if DEBUG
        string boardFen = board.GetFenString();
#endif

        //Do a NegaMax search with iterative deepening
        //TODO Look into aspiration windows (maybe even MTD(f))
        int curBestEval = 0;
        ushort curBestMove = 0;
        for(int depth = 1;; depth++) {
#if STATS
            //Notify the stats tracker that the depth search starts
            STAT_StartDepthSearch(depth);
#endif
#if BESTMOVE || STATS
            bool didTimeOut = false;
#endif

            //Update the maximum extension amount
            maxExtension = MaxExtension - depth*MaxExtensionReductionPerIter;

            //Do a NegaMax search with the current depth
            ushort iterBestMove = 0;
            try {
                curBestEval = NegaMax(Eval.MinSentinel, Eval.MaxSentinel, depth, 0, out iterBestMove);
                curBestMove = iterBestMove;

#if DEBUG
                //Check that the board has been properly reset
                if(board.GetFenString() != boardFen) throw new Exception($"Board has not been properly reset after search to depth {depth}: '{boardFen}' != '{board.GetFenString()}'");
            } catch(TimeoutException) {
#else
            } catch(Exception) {
#endif
#if BESTMOVE || STATS
                didTimeOut = true;
#endif

                //Recycle partial search results
                //Note that we can't recycle our evaluation, but that's fine (except for some log output)
                if(iterBestMove != 0) curBestMove = iterBestMove;
            }

#if STATS
            //Notify the stats tracker that the depth search ended
            STAT_EndDepthSearch(iterBestMove != 0 ? FindMove_I(moves, iterBestMove) : default, curBestEval, depth, didTimeOut);
#endif

            //Check if time is up, if we found a checkmate, if we reached our max depth
            if(timer.MillisecondsElapsedThisTurn >= deepeningSearchTime || curBestEval <= Eval.MinMate || curBestEval >= Eval.MaxMate || depth >= MaxDepth) {
                Move bestMove = FindMove_I(moves, curBestMove);

#if STATS
                //Notify the stats tracker that the search ended
                STAT_EndGlobalSearch(bestMove, curBestEval, depth - (didTimeOut && iterBestMove == 0 ? 1 : 0));
#endif
#if BESTMOVE
                //Log the best move
                Console.WriteLine($"Searched to depth {depth - (didTimeOut && iterBestMove == 0 ? 1 : 0)} in {timer.MillisecondsElapsedThisTurn:d5}ms: best move {bestMove.ToString()[7..^1]} eval {(!didTimeOut ? curBestEval.ToString() : "????")}");
#endif
                return bestMove;
            }
        }
    }

    private int timeoutCheckNodeCounter = 0;
    public int NegaMax(int alpha, int beta, int remDepth, int ply, out ushort bestMove, int prevExtensions = 0, int lmrIdx = -1) {
        bestMove = 0;

#if DEBUG
        if(remDepth < 0 || remDepth > MaxDepth) throw new Exception($"Out-of-range depth: {remDepth}");
        if(ply < 0 || ply >= MaxPlies) throw new Exception($"Out-of-range ply: {ply}");
#endif

        //Check if time is up
        if((++timeoutCheckNodeCounter & 0xfff) == 0 && searchTimer.MillisecondsElapsedThisTurn >= searchAbortTime)
#if DEBUG
            throw new TimeoutException();
#else
            throw new Exception();
#endif

        //Handle repetition
        if(searchBoard.IsRepeatedPosition()) return 0;

        //Start a new node
        bool isInCheck = searchBoard.IsInCheck();
        bool isPvCandidateNode = alpha+1 < beta; //Because of PVS, all nodes without a zero window are considered candidate nodes

#if DEBUG
        if(ply == 0) {
            if(remDepth <= 0) throw new Exception("Root node can't immediately enter Q-search");
            if(!isPvCandidateNode) throw new Exception("Root node can't be searched with a ZW");
            if(lmrIdx >= 0) throw new Exception("Root node can't have an LMR applied");
        }
#endif

#if STATS
        STAT_NewNode_I(isPvCandidateNode, false);
#endif

        //Check if we reached the bottom of the search tree
        if(remDepth <= 0) return QSearch(alpha, beta, ply);

        //Check if the position is in the TT
        ulong boardHash = searchBoard.ZobristKey;
        ref ulong ttSlot = ref transposTable[boardHash & TTIdxMask];
        if(CheckTTEntry_I(ttSlot, boardHash, alpha, beta, remDepth)) {
            //The evaluation is stored in the lower 16 bits of the entry
            bestMove = transposMoveTable[boardHash & TTIdxMask];

#if DEBUG
            if(ply == 0 && bestMove == 0) throw new Exception("Root TT entry has no best move");
#endif

            return unchecked((short) ttSlot);
        }

        //Reset any pruning special move values, as they might screw up future move ordering if not cleared
        ResetThreatMove_I(ply);

        //Apply pruning to non-PV candidates (otherwise we duplicate our work on researches I think?)
        bool canFutilityPrune = false;
        if(!isInCheck && !isPvCandidateNode && Eval.MinMate < alpha && beta < Eval.MaxMate) {
            int prunedScore = 0;
#if FSTATS
            STAT_Pruning_CheckNonPVNode_I();
#endif

            //Determine the static evaluation of the position
            int staticEval = Eval.Evaluate(searchBoard);

            //Determine if we can futility prune
            canFutilityPrune = CanFutilityPrune_I(staticEval, alpha, remDepth);
#if FSTATS
            if(canFutilityPrune) STAT_FutilityPruning_AbleNode();
#endif

            //Apply Reverse Futility Pruning
            if(ApplyReverseFutilityPruning_I(staticEval, beta, remDepth, ref prunedScore)) {
#if FSTATS
                STAT_ReverseFutilityPruning_PrunedNode_I();
#endif
                return prunedScore;
            }

            //Apply Null Move Pruning
            if(ApplyNullMovePruning_I(alpha, beta, remDepth, ply, prevExtensions, ref prunedScore)) {
                int extension = 0;
                if(prunedScore >= beta) { 
#if FSTATS
                    STAT_NullMovePruning_PrunedNode_I();
#endif
                    return prunedScore;
                } else if(prunedScore <= Eval.MinMate) {
                    //Mate threat extension
                    extension = OnePlyExtension / 2;

#if FSTATS
                    STAT_MateThreatExtension_I();
#endif
                } else if(threatMove != 0 && ApplyBotvinnikMarkoffExtension_I(threatMove, ply)) {
                    extension = OnePlyExtension / 3;

#if FSTATS
                    STAT_BotvinnikMarkoffExtension_I();
#endif
                }
                if(extension != 0) ApplyFractExtension_I(extension, ref remDepth, ref prevExtensions);
            }
        }

        //Re-check if we reached the bottom of the search tree because of reductions
        if(remDepth <= 0) {
            //TODO Our current Q-search is not check aware
            if(!isInCheck) return QSearch(alpha, beta, ply);
            remDepth = 1;
        }

        //Generate legal moves
        Span<Move> moves = stackalloc Move[256];
        searchBoard.GetLegalMovesNonAlloc(ref moves);

        if(moves.Length == 0) {
#if DEBUG
            if(ply == 0) throw new Exception("Root node has no valid moves");
#endif
            //Handle checkmate / stalemate
            return searchBoard.IsInCheck() ? Eval.MinEval + ply : 0;
        }

#if STATS
        //Report that we are starting to search a new node
        STAT_AlphaBeta_SearchNode_I(isPvCandidateNode, false, moves.Length);
#endif
#if FSTATS
        if(canFutilityPrune) STAT_FutilityPruning_ReportMoves(moves.Length);
#endif

        //Order moves
        Span<Move> toBeOrderedMoves = PlaceBestMoveFirst_I(alpha, beta, remDepth, ply, moves, ttSlot, boardHash);
        SortMoves(toBeOrderedMoves, ply);

        //Search for the best move
        int bestScore = Eval.MinSentinel;
        bool hasPvMove = false;
        TTBoundType ttBound = TTBoundType.Upper; //Until we surpass alpha we only have an upper bound

        for(int i = 0; i < moves.Length; i++) {
            Move move = moves[i];

            //FP: Check if we can futility-prune this move
            if(canFutilityPrune && bestMove != 0 && IsMoveQuiet_I(move)) {
#if FSTATS
                STAT_FutilityPruning_PrunedMove();
#endif
                continue;
            }

            searchBoard.MakeMove(move);
            bool gaveCheck = searchBoard.IsInCheck();

            //PVS: If we already have a PV move (which should be early because of move ordering), do a ZWS on alpha first to ensure that this move doesn't fail low
            int score;
            switch(hasPvMove) {
                case true:
                    //Determine extensions / reductions for this move
                    int moveExts = DetermineMoveZWExtensions_I(move, gaveCheck, remDepth-1);

                    //LMR: Check if we allow Late Move Reduction for this move
                    if(!isInCheck && !gaveCheck && moveExts <= 0 && IsLMRAllowedForMove_I(move, i, remDepth)) {
#if FSTATS
                        STAT_LMR_ApplyReduction_I();
#endif

                        //Apply the LMR reduction
                        int lmrDepth = remDepth-1, lmrPrevExts = prevExtensions;
                        moveExts -= DetermineLMRReduction_I(i, lmrDepth);
                        ApplyFractExtension_I(moveExts, ref lmrDepth, ref lmrPrevExts);

                        //Do a ZWS search with reduced depth
                        score = -NegaMax(-alpha - 1, -alpha, lmrDepth, ply+1, out _, lmrPrevExts);
                        if(score <= alpha) break; //If we reach alpha research with full depth
                        moveExts = 0;
                    }

                    int moveDepth = remDepth-1, movePrevExts = prevExtensions;
                    ApplyFractExtension_I(moveExts, ref moveDepth, ref movePrevExts);
                    score = -NegaMax(-alpha - 1, -alpha, moveDepth, ply+1, out _, moveExts);
                    if(score <= alpha || score >= beta) break; //We check the beta bound as well as we can fail-high because of our fail-soft search

                    //Research with the full window
#if STATS
                    STAT_PVS_Research_I();
#endif

                    goto default;
                default:
                    //Don't apply any extensions / reductions except for a full ply when in check
                    score = -NegaMax(-beta, -alpha, remDepth - (gaveCheck ? 0 : 1), ply+1, out _, prevExtensions);
                    break;
            }

            searchBoard.UndoMove(move); //This gets skipped on timeout - we don't care, as the board gets recreated every time a bot thinks

#if STATS
            //Report that we searched a move
            STAT_AlphaBeta_SearchedMove_I(isPvCandidateNode, false);
#endif

            //Update the best score
            if(score > bestScore) {
                bestScore = score;
                bestMove = move.RawValue;
            }
#if DEBUG
            else if(i == 0) throw new Exception($"First move failed to raise best score: {move} {score}");
#endif

            //Update alpha/beta bounds
            //Do this after the best score update to implement a fail-soft alpha-beta search
            if(score > alpha) {
                if(score >= beta) {
                    //We failed high; our score is only a lower bound
#if STATS
                    STAT_AlphaBeta_FailHigh_I(isPvCandidateNode, false, i);
#endif

                    //Check if the move is quiet
                    if(IsMoveQuiet_I(move)) {
                        //Insert into the killer table
                        InsertIntoKillerTable_I(ply, move);

                        //Update the butterfly and history tables
                        bool isWhite = searchBoard.IsWhiteToMove;
                        for(int pi = 0; pi < i; pi++) {
                            Move prevMove = moves[pi];
                            UpdateButterflyTable_I(prevMove, isWhite, remDepth);
                        }
                        UpdateHistoryTable_I(move, isWhite, remDepth);
                    }

                    ttBound = TTBoundType.Lower;
                    break;
                }

                //We raised alpha and became a PV-node; our score is exact
                alpha = score;
                ttBound = TTBoundType.Exact;

#if STATS
                STAT_PVS_FoundPVMove_I(i, hasPvMove);
#endif
                hasPvMove = true;
            }
        }

#if STATS
        //Check if we failed low
        if(ttBound == TTBoundType.Upper) STAT_AlphaBeta_FailLow_I(isPvCandidateNode, false);
#endif

        //Insert the best move found into the transposition table (except when in Q-Search)
        //TODO Currently always replaces, investigate potential other strategies
        StoreTTEntry_I(ref ttSlot, (short) bestScore, ttBound, remDepth, boardHash);
        transposMoveTable[boardHash & TTIdxMask] = bestMove;

#if DEBUG
        if(bestScore < Eval.MinEval) throw new Exception($"Found no best move in node search: best score {bestScore} best move {bestMove} num moves {moves.Length}");
#endif

        return bestScore;
    }
}