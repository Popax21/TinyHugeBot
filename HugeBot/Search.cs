using System;
using ChessChallenge.API;
using HugeBot;

public partial class MyBot : IChessBot {
    private Timer searchTimer = null!;
    private int searchAbortTime;

    public Move Think(Board board, Timer timer) {
        //Determine search times
        searchTimer = timer;
        searchAbortTime = timer.MillisecondsRemaining / 8;

        int deepeningSearchTime = timer.MillisecondsRemaining / 20;

#if STATS
        //Notify the stats tracker that the search starts
        STAT_StartGlobalSearch();
#endif

        //Generate all legal moves for later lookup
        Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves);

        //Do a NegaMax search with iterative deepening
        //TODO Look into aspiration windows (maybe even MTD(f))
        int curBestEval = 0, curBestMoveIdx = 0;
        for(int depth = 1;; depth++) {
#if STATS
            //Notify the stats tracker that the depth search starts
            STAT_StartDepthSearch(depth);
#endif
#if DEBUG || STATS
            bool didTimeOut = false;
#endif

            //Do a NegaMax search with the current depth
            try {
                curBestEval = NegaMax(board, -int.MaxValue, int.MaxValue, depth, 0, out ushort bestMove);

                //Update the best move
#if DEBUG
                curBestMoveIdx = -1;
#endif
                for(int i = 0; i < moves.Length; i++) {
                    if(moves[i].RawValue == bestMove) {
                        curBestMoveIdx = i;
                        break;
                    }
                }
#if DEBUG
                if(curBestMoveIdx < 0) throw new Exception("Root node search returned no best move");
#endif
#if DEBUG
            } catch(TimeoutException) {
#else
            } catch(Exception) {
#endif
#if DEBUG || STATS
                didTimeOut = true;
#endif
            }

#if STATS
            //Notify the stats tracker that the depth search ended
            STAT_EndDepthSearch(moves[curBestMoveIdx], curBestEval, depth, didTimeOut);
#endif
            //Check if time is up
            if(timer.MillisecondsElapsedThisTurn >= deepeningSearchTime) {
#if STATS
                //Notify the stats tracker that the search ended
                STAT_EndGlobalSearch(moves[curBestMoveIdx], curBestEval, depth - (didTimeOut ? 1 : 0));
#endif
#if DEBUG
                //Log the best move
                Console.WriteLine($"Searched to depth {depth - (didTimeOut ? 1 : 0)} in {timer.MillisecondsElapsedThisTurn:d5}ms: best {moves[curBestMoveIdx].ToString().ToLower()} eval {curBestEval}");
#endif
                return moves[curBestMoveIdx];
            }
        }
    }

    //alpha / beta are exclusive lower / upper bounds
    public int NegaMax(Board board, int alpha, int beta, int remDepth, int ply, out ushort bestMove) {
        bool isZeroWindow = alpha == beta-1;
        bestMove = 0;

        //Check if time is up
        if(searchTimer.MillisecondsElapsedThisTurn >= searchAbortTime)
#if DEBUG
            throw new TimeoutException();
#else
            throw new Exception();
#endif

#if STATS
        STAT_NewNode_I(isZeroWindow);
#endif

        //Handle repetition
        if(board.IsRepeatedPosition()) return 0;

        //TODO Reductions / Extensions

        //Check if the position is in the TT
        ulong boardHash = board.ZobristKey;
        ref ulong ttSlot = ref transposTable[boardHash & TTIdxMask];
        if(CheckTTEntry_I(ttSlot, boardHash, alpha, beta, remDepth)) {
            //The evaluation is stored in the lower 16 bits of the entry
            bestMove = transposMoveTable[boardHash & TTIdxMask];
            return unchecked((short) ttSlot);
        }

        //TODO Pruning

        //Check if we reached the bottom of the search tree
        //TODO Quiescence search
        if(remDepth <= 0) return Eval.Evaluate_I(board);

        //Generate legal moves
        Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves);

        if(moves.Length == 0) {
            //Handle checkmate / stalemate
            return board.IsInCheck() ? Eval.MinEval + ply : 0;
        }

        //Order moves
        OrderMoves_I(board, alpha, beta, remDepth, ply, moves, ttSlot, boardHash);

#if STATS
        //Report that we are starting to search a new unpruned node
        STAT_AlphaBeta_SearchNode_I(isZeroWindow, moves.Length);
#endif

        //Search for the best move
        int bestScore = Eval.MinEval;
        bool hasPvMove = false;
        TTBoundType ttBound = TTBoundType.Upper; //Until we surpass alpha we only have an upper bound

        for(int i = 0; i < moves.Length; i++) {
            board.MakeMove(moves[i]);

            //PVS: If we already have a PV move (which should be early because of move ordering), do a ZWS on alpha first to ensure that this move doesn't fail low
            int score;
            switch(hasPvMove) {
                case true:
                    score = -NegaMax(board, -alpha - 1, -alpha, remDepth-1, ply+1, out _);
                    if(score <= alpha || score >= beta) break; //We check the beta bound as well as we can fail-high because of our fail-soft search

                    //Research with the full window
#if STATS
                    STAT_PVS_Research_I();
#endif

                    goto default;
                default:
                    score = -NegaMax(board, -beta, -alpha, remDepth-1, ply+1, out _);
                    break;
            }

            board.UndoMove(moves[i]); //This gets skipped on timeout - we don't care, as the board gets recreated every time a bot thinks

#if STATS
            //Report that we searched a move
            STAT_AlphaBeta_SearchedMove_I(isZeroWindow);
#endif

            //Update the best score
            if(score > bestScore) {
                bestScore = score;
                bestMove = moves[i].RawValue;
            }

            //Update alpha/beta bounds
            //Do this after the best score update to implement a fail-soft alpha-beta search
            if(score > alpha) {
                if(score >= beta) {
                    //We failed high; our score is only a lower bound
#if STATS
                    STAT_AlphaBeta_FailHigh_I(isZeroWindow, i);
#endif

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
        if(ttBound == TTBoundType.Upper) STAT_AlphaBeta_FailLow_I(isZeroWindow);
#endif

        //Insert the move into the transposition table
        //TODO Currently always replaces, investigate potential other strategies
        StoreTTEntry_I(ref ttSlot, (short) bestScore, ttBound, remDepth, boardHash);
        transposMoveTable[boardHash & TTIdxMask] = bestMove;

        return bestScore;
    }
}