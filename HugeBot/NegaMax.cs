using System;
using ChessChallenge.API;

namespace HugeBot;

public partial class MyBot : IChessBot {
    public const int MaxDepth = 63; //Limited by TT
    public const int MaxPlies = 256;

    private int[] plyMoveButterflies = new int[MaxPlies];

    private int timeoutCheckNodeCounter = 0;
    public int NegaMax(int alpha, int beta, int remDepth, int ply, out ushort bestMove, int searchExtensions=0) {
        bestMove = 0;

#if VALIDATE
        if(remDepth > MaxDepth) throw new Exception($"Out-of-range depth: {remDepth}");
        if(ply < 0 || ply >= MaxPlies) throw new Exception($"Out-of-range ply: {ply}");
#endif

        //Check if time is up
        if((++timeoutCheckNodeCounter & 0xfff) == 0 && searchTimer.MillisecondsElapsedThisTurn >= searchAbortTime)
#if VALIDATE
            throw new TimeoutException();
#else
            throw new Exception();
#endif

        //Start a new node
        bool isWhite = searchBoard.IsWhiteToMove;
        bool isInCheck = searchBoard.IsInCheck();
        bool isPvCandidateNode = alpha+1 < beta; //Because of PVS, all nodes without a zero window are considered candidate nodes

#if VALIDATE
        if(ply == 0) {
            if(remDepth <= 0) throw new Exception("Root node can't immediately enter Q-search");
            if(!isPvCandidateNode) throw new Exception("Root node can't be searched with a ZW");
        }
#endif

#if STATS
        STAT_NewNode_I(isPvCandidateNode, false);
#endif

        //Check if we reached the bottom of the search tree
        if(remDepth <= 0) return QSearch(alpha, beta, ply);

        //Handle repetition
        if(ply > 0 && searchBoard.IsRepeatedPosition()) return 0;

        //Check if the position is in the TT
        ulong boardHash = searchBoard.ZobristKey;
        ulong ttEntry = transposTable[boardHash & TTIdxMask];
        if(CheckTTEntry_I(ttEntry, boardHash, alpha, beta, remDepth)) {
            //The evaluation is stored in the lower 16 bits of the entry
            bestMove = transposMoveTable[boardHash & TTIdxMask];

#if VALIDATE
            if(ply == 0 && bestMove == 0) throw new Exception("Root TT entry has no best move");
#endif

            return unchecked((short) ttEntry);
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
            if(ApplyNullMovePruning_I(alpha, beta, remDepth, ply, staticEval, searchExtensions, ref prunedScore)) {
                if(prunedScore >= beta) { 
#if FSTATS
                    STAT_NullMovePruning_PrunedNode_I();
#endif
                    return prunedScore;
                }

                //Mate threat extension
                if(remDepth <= 3 && prunedScore <= Eval.MinMate && staticEval >= beta) {
                    ApplyExtension_I(1, ref searchExtensions, ref remDepth);

#if FSTATS
                    STAT_MateThreatExtension_I();
#endif
                }
            }
        }

        //Generate legal moves
        Span<Move> moves = stackalloc Move[256];
        searchBoard.GetLegalMovesNonAlloc(ref moves);

        if(moves.Length == 0) {
#if VALIDATE
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
        int sortedMovesStartIdx = PlaceBestMoveFirst_I(alpha, beta, remDepth, ply, searchExtensions, moves, ttEntry, boardHash);
        SortMoves(moves.Slice(sortedMovesStartIdx), ply);

        //Search for the best move
        int bestScore = Eval.MinSentinel;
        bool hasPvMove = false;
        TTBoundType ttBound = TTBoundType.Upper; //Until we surpass alpha we only have an upper bound

        bool sortedQuietMoves = false;
        for(int i = 0; i < moves.Length; i++) {
            Move move = moves[i];

            //Sort quiet moves if we haven't already
            if(i >= sortedMovesStartIdx && IsMoveQuiet_I(move) && !sortedQuietMoves) {
                SortMoves(moves.Slice(i), ply);
                sortedQuietMoves = true;
                move = moves[i];
            }

            //FP: Check if we can futility-prune this move
            if(canFutilityPrune && bestMove != 0 && IsMoveQuiet_I(move) && !IsThreatEscapeMove_I(move, ply)) {
#if FSTATS
                STAT_FutilityPruning_PrunedMove();
#endif
                continue;
            }

            plyMoveButterflies[ply] = GetMoveButterflyIndex_I(move, isWhite);
            searchBoard.MakeMove(move);
            bool gaveCheck = searchBoard.IsInCheck();

            //PVS: If we already have a PV move (which should be early because of move ordering), do a ZWS on alpha first to ensure that this move doesn't fail low
            int score;
            switch(hasPvMove) {
                case true:
                    //Determine extensions / reductions for this move
                    int moveExts = DetermineMoveZWExtensions_I(move, gaveCheck, remDepth-1);
                    if(searchExtensions + moveExts > MaxExtension) moveExts = MaxExtension - searchExtensions;

                    //LMR: Check if we allow Late Move Reduction for this move
                    if(!isInCheck && !gaveCheck && moveExts <= 0 && IsLMRAllowedForMove_I(move, i, remDepth, ply)) {
#if FSTATS
                        STAT_LMR_ApplyReduction_I();
#endif

                        //Do a ZWS search with reduced depth
                        moveExts -= DetermineLMRReduction_I(i, remDepth-1);
                        score = -NegaMax(-alpha - 1, -alpha, remDepth-1 + moveExts, ply+1, out _, searchExtensions);
                        if(score <= alpha) break; //If we reach alpha research with full depth

                        moveExts = 0;
#if FSTATS
                        STAT_LMR_Research_I();
#endif
                    }

                    score = -NegaMax(-alpha - 1, -alpha, remDepth-1 + moveExts, ply+1, out _, searchExtensions + moveExts);
                    if(score <= alpha || score >= beta) break; //We check the beta bound as well as we can fail-high because of our fail-soft search

                    //Research with the full window
#if STATS
                    STAT_PVS_Research_I();
#endif

                    goto default;
                default:
                    //Don't apply any extensions / reductions except for a full ply when in check
                    score = -NegaMax(-beta, -alpha, remDepth - (gaveCheck ? 0 : 1), ply+1, out _, searchExtensions);
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
#if VALIDATE
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
                        for(int pi = 0; pi < i; pi++) {
                            Move prevMove = moves[pi];
                            UpdateButterflyTable_I(prevMove, isWhite, remDepth, ply);
                        }
                        UpdateHistoryTable_I(move, isWhite, remDepth, ply);
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
            } else if(i == 0 && ply == 0) {
                //If the first move already failed low on the root node, then immediately bail out to update the aspiration window
                break;
            }
        }

#if STATS
        //Check if we failed low
        if(ttBound == TTBoundType.Upper) STAT_AlphaBeta_FailLow_I(isPvCandidateNode, false);
#endif

        //Insert the best move found into the transposition table (except when in Q-Search)
        StoreTTEntry_I(boardHash, (short) bestScore, ttBound, remDepth, bestMove);

#if VALIDATE
        if(bestScore < Eval.MinEval) throw new Exception($"Found no best move in node search: best score {bestScore} best move {bestMove} num moves {moves.Length}");
#endif

        return bestScore;
    }
}