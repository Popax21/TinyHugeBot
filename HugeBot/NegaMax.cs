using System;
using ChessChallenge.API;

namespace HugeBot;

public partial class MyBot : IChessBot {
    public const int MaxDepth = 63; //Limited by TT
    public const int MaxPlies = 256;

    private int[] plyMoveButterflies = new int[MaxPlies];
    private int[] plyStaticEvals = new int[MaxPlies];

    private int timeoutCheckNodeCounter = 0;
    public int NegaMax(int alpha, int beta, int remDepth, int ply, out ushort bestMove, int searchExtensions=0) {
        bestMove = 0;
        if(remDepth < 0) remDepth = 0;

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
        bool isQSearch = remDepth <= 0;
        bool isPvCandidateNode = !isQSearch && alpha+1 < beta; //Because of PVS, all nodes without a zero window are considered candidate nodes

#if VALIDATE
        if(ply == 0) {
            if(remDepth <= 0) throw new Exception("Root node can't immediately enter Q-search");
            if(!isPvCandidateNode) throw new Exception("Root node can't be searched with a ZW");
        }
#endif

#if STATS
        STAT_NewNode_I(isPvCandidateNode, isQSearch);
#endif

        //Handle repetition
        if(ply > 0 && searchBoard.IsRepeatedPosition()) return 0;

        //Check if the position is in the TT
        ulong boardHash = searchBoard.ZobristKey;
        if(CheckTTEntry_I(boardHash, alpha, beta, remDepth, out ulong ttIdx, out bool ttEntryValid, out ulong ttEntry)) {
            bestMove = transposMoveTable[ttIdx];
#if VALIDATE
            if(ply == 0 && bestMove == 0) throw new Exception("Root TT entry has no best move");
#endif
            return unchecked((short) ttEntry); //The evaluation is stored in the lower 16 bits of the entry
        }
        short ttScore = unchecked((short) ttEntry);

        //Reset any pruning special move values, as they might screw up future move ordering if not cleared
        ResetThreatMove_I(ply);

        //Determine the static evaluation of the position
        int staticEval = Eval.Evaluate(searchBoard);
        plyStaticEvals[ply] = staticEval;
        if(ttEntryValid && Eval.MinMate < ttScore && ttScore < Eval.MaxMate) staticEval = ttScore;

        bool isImproving = ply >= 2 && staticEval > plyStaticEvals[ply-2];
            
        //Apply pruning to non-PV candidates (otherwise we duplicate our work on researches I think?)
        bool canFutilityPrune = false;
        int lmpSearchCount = -1;
        if(!isQSearch && !isInCheck && !isPvCandidateNode && beta > Eval.MinMate) {
            int prunedScore = 0;
#if FSTATS
            STAT_Pruning_CheckNonPVNode_I();
#endif

            //Apply Reverse Futility Pruning
            if(ApplyReverseFutilityPruning_I(staticEval, beta, remDepth, ref prunedScore)) {
#if FSTATS
                STAT_ReverseFutilityPruning_PrunedNode_I();
#endif
                return prunedScore; //TODO Does returning staticEval here help things?
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

            //Determine if we can futility prune
            canFutilityPrune = CanFutilityPrune_I(staticEval, alpha, remDepth);
#if FSTATS
            if(canFutilityPrune) STAT_FutilityPruning_AbleNode();
#endif

            //Determine number of moves to search before LMP kicks in
            lmpSearchCount = GetLMPMoveSearchCount_I(remDepth, isImproving);
        }

        //Apply the standing pat score if in Q-search
        if(isQSearch && staticEval > alpha) {
            if(staticEval >= beta) return staticEval;

            //Don't change the TT bound!
            alpha = staticEval;
        }

        //Generate legal moves
        Span<Move> moves = stackalloc Move[256];
        searchBoard.GetLegalMovesNonAlloc(ref moves, capturesOnly: isQSearch);

        if(moves.Length == 0) {
#if VALIDATE
            if(ply == 0) throw new Exception("Root node has no valid moves");
#endif

            //Fail low when in Q-search
            if(isQSearch) return staticEval;

            //Handle checkmate / stalemate
            return isInCheck ? Eval.MinEval + ply : 0;
        }

        //Order moves
        Span<ulong> moveScores = stackalloc ulong[256];
        ushort firstMove = DetermineFirstMove_I(alpha, beta, remDepth, ply, searchExtensions, isPvCandidateNode, ttEntryValid, ttIdx, ttEntry);
        ScoreMoves(moves, moveScores, ply, isWhite, firstMove, false);

        ushort poppedFirstMove = firstMove;

#if VALIDATE
        bool searchedNoisyMoves = false;
#endif

#if STATS
        //Report that we are starting to search a new node
        STAT_AlphaBeta_SearchNode_I(isPvCandidateNode, isQSearch, moves.Length);
#endif
#if FSTATS
        if(canFutilityPrune) STAT_FutilityPruning_ReportMoves(moves.Length);
#endif

        //Search for the best move
        int bestScore = Eval.MinSentinel;
        TTBoundType ttBound = TTBoundType.Upper; //Until we raise alpha we only have an upper bound
#if STATS
        bool hasPvMove = false;
#endif
        for(int i = 0; i < moves.Length; i++) {
            //Pop the next move to search
            if(!PopMove_I(moves, moveScores, i, ref poppedFirstMove, out Move move)) {
#if VALIDATE
                //We mustn't run out of moves in Q-search
                if(isQSearch) throw new Exception("Ran out of moves to search in Q-search");

                //Check if we finished searching quiet moves
                if(searchedNoisyMoves) throw new Exception("Ran out of moves to search");
                searchedNoisyMoves = true;
#endif

                //Score quiet moves
                ScoreMoves(moves, moveScores, ply, isWhite, firstMove, true);

                //Pop a quiet move now
                if(!PopMove_I(moves, moveScores, i, ref poppedFirstMove, out move))
#if VALIDATE
                    throw new Exception("Ran out of moves to search");
#else
                    break;
#endif
            }

            bool isQuietMove = IsMoveQuiet_I(move);

            //LMP: Check if we can prune all following moves
            if(isQuietMove && lmpSearchCount-- == 0) break;

            //FP: Check if we can futility-prune this move
            if(canFutilityPrune && isQuietMove && bestMove != 0) {
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
            switch(bestMove != 0) {
                case true:
                    int moveExts = 0;
                    if(!isQSearch) {
                        //Determine extensions / reductions for this move
                        moveExts = DetermineMoveZWExtensions_I(move, gaveCheck, remDepth-1);
                        if(searchExtensions + moveExts > MaxExtension) moveExts = MaxExtension - searchExtensions;

                        //LMR: Check if we allow Late Move Reduction for this move
                        if(isQuietMove && !isInCheck && !gaveCheck && moveExts <= 0 && IsLMRAllowedForMove_I(move, i, remDepth, ply)) {
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
                    }

                    score = -NegaMax(-alpha - 1, -alpha, remDepth-1 + moveExts, ply+1, out _, searchExtensions + moveExts);
                    if(score <= alpha || score >= beta) break; //We check the beta bound as well as we can fail-high because of our fail-soft search

                    //Research with the full window
#if STATS
                    STAT_PVS_Research_I();
#endif

                    goto default;
                default:
                    //Extend by one ply if in check
                    int checkExt = 0;
                    if(!isQSearch && gaveCheck && searchExtensions < MaxExtension) checkExt = 1;
                    score = -NegaMax(-beta, -alpha, remDepth-1 + checkExt, ply+1, out _, searchExtensions + checkExt);
                    break;
            }

            searchBoard.UndoMove(move); //This gets skipped on timeout - we don't care, as the board gets recreated every time a bot thinks

#if STATS
            //Report that we searched a move
            STAT_AlphaBeta_SearchedMove_I(isPvCandidateNode, isQSearch);
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
                    STAT_AlphaBeta_FailHigh_I(isPvCandidateNode, isQSearch, i);
#endif

                    //Check if the move is quiet
                    if(isQuietMove) {
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
                hasPvMove = true;
#endif
            }
        }

#if VALIDATE
        if(bestScore < Eval.MinEval) throw new Exception($"Found no best move in node search: best score {bestScore} best move {bestMove}");
#endif

#if STATS
        //Check if we failed low
        if(ttBound == TTBoundType.Upper) STAT_AlphaBeta_FailLow_I(isPvCandidateNode, isQSearch);
#endif

        //Insert the best move found into the transposition table
        //Only store in Q-search if we managed to improve over the standing pat
        if(!isQSearch || bestScore > staticEval) StoreTTEntry_I(boardHash, (short) bestScore, ttBound, remDepth, bestMove);

        if(isQSearch && staticEval > bestScore) return staticEval;
        else return bestScore;
    }
}