using System;
using ChessChallenge.API;
using HugeBot;

public partial class MyBot : IChessBot {
    public const int MaxDepth = 63; //Limited by TT
    public const int MaxPly = 256; //Limited by TT

    private Board searchBoard = null!;
    private Timer searchTimer = null!;
    private int searchAbortTime;

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

            //Do a NegaMax search with the current depth
            ushort iterBestMove = 0;
            try {
                curBestEval = NegaMax(-0x10_0000, +0x10_0000, depth, 0, out iterBestMove);
                curBestMove = iterBestMove;
#if DEBUG
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
    public int NegaMax(int alpha, int beta, int remDepth, int ply, out ushort bestMove) {
        bool isPvCandidateNode = alpha+1 < beta; //Because of PVS, all nodes without a zero window are considered candidate nodes
        bestMove = 0;

        //Check if time is up
        if((++timeoutCheckNodeCounter & 0xfff) == 0 && searchTimer.MillisecondsElapsedThisTurn >= searchAbortTime)
#if DEBUG
            throw new TimeoutException();
#else
            throw new Exception();
#endif

#if STATS
        STAT_NewNode_I(isPvCandidateNode, false);
#endif

        //Handle repetition
        if(searchBoard.IsRepeatedPosition()) return 0;

        //TODO Reductions / Extensions

        //Check if the position is in the TT
        ulong boardHash = searchBoard.ZobristKey;
        ref ulong ttSlot = ref transposTable[boardHash & TTIdxMask];
        if(CheckTTEntry_I(ttSlot, boardHash, alpha, beta, remDepth)) {
            //The evaluation is stored in the lower 16 bits of the entry
            bestMove = transposMoveTable[boardHash & TTIdxMask];
            return unchecked((short) ttSlot);
        }

        //Reset any pruning special move values, as they might screw up future move ordering if not cleared
        Pruning_ResetSpecialMove_I();

        //Check if we reached the bottom of the search tree
        if(remDepth <= 0) return QSearch(alpha, beta, ply);

        //Apply Null Move Pruning
        //Only apply to non-PV candidates, otherwise we duplicate our work on researches (I think?)
        int prunedScore = 0;
        if(!isPvCandidateNode && ApplyNullMovePruning_I(alpha, beta, remDepth, ply, ref prunedScore)) return prunedScore;

        //Generate legal moves
        Span<Move> moves = stackalloc Move[256];
        searchBoard.GetLegalMovesNonAlloc(ref moves);

        if(moves.Length == 0) {
            //Handle checkmate / stalemate
            return searchBoard.IsInCheck() ? Eval.MinEval + ply : 0;
        }

#if STATS
        //Report that we are starting to search a new node
        STAT_AlphaBeta_SearchNode_I(isPvCandidateNode, false, moves.Length);
#endif

        //Order moves
        Span<Move> toBeOrderedMoves = PlaceBestMoveFirst_I(alpha, beta, remDepth, ply, moves, ttSlot, boardHash);
        SortMoves(toBeOrderedMoves, ply);

        //Search for the best move
        int bestScore = Eval.MinEval;
        bool hasPvMove = false;
        TTBoundType ttBound = TTBoundType.Upper; //Until we surpass alpha we only have an upper bound

        for(int i = 0; i < moves.Length; i++) {
            Move move = moves[i];
            searchBoard.MakeMove(move);

            //PVS: If we already have a PV move (which should be early because of move ordering), do a ZWS on alpha first to ensure that this move doesn't fail low
            int score;
            switch(hasPvMove) {
                case true:
                    score = -NegaMax(-alpha - 1, -alpha, remDepth-1, ply+1, out _);
                    if(score <= alpha || score >= beta) break; //We check the beta bound as well as we can fail-high because of our fail-soft search

                    //Research with the full window
#if STATS
                    STAT_PVS_Research_I();
#endif

                    goto default;
                default:
                    score = -NegaMax(-beta, -alpha, remDepth-1, ply+1, out _);
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

        return bestScore;
    }
}