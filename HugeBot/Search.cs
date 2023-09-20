﻿using System;
using ChessChallenge.API;
using HugeBot;

public partial class MyBot : IChessBot {
    private Timer searchTimer = null!;
    private int searchAbortTime;

    private Move rootBestMove;

    public Move Think(Board board, Timer timer) {
        //Determine search times
        searchTimer = timer;
        searchAbortTime = timer.MillisecondsRemaining / 8;

        int deepeningSearchTime = timer.MillisecondsRemaining / 20;

#if STATS
        //Notify the stats tracker that the search starts
        STAT_StartGlobalSearch();
#endif

        //Do a NegaMax search with iterative deepening
        Move curBestMove = default;
        int curBestEval = 0;
        for(int depth = 1;; depth++) {
            //Do a NegaMax search with the current depth

#if STATS
            //Notify the stats tracker that the depth search starts
            STAT_StartDepthSearch(depth);
#endif
#if DEBUG || STATS
            bool didTimeOut = false;
#endif

            try {
                curBestEval = NegaMax(board, Eval.MinEval, Eval.MaxEval, depth, 0);
                curBestMove = rootBestMove; //Update the best move
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
            STAT_EndDepthSearch(curBestMove, curBestEval, depth, didTimeOut);
#endif
            //Check if time is up
            if(timer.MillisecondsElapsedThisTurn >= deepeningSearchTime) {
#if STATS
                //Notify the stats tracker that the search ended
                STAT_EndGlobalSearch(curBestMove, curBestEval, depth - (didTimeOut ? 1 : 0));
#endif
#if DEBUG
                //Log the best move
                Console.WriteLine($"Searched to depth {depth - (didTimeOut ? 1 : 0)} in {timer.MillisecondsElapsedThisTurn:d5}ms: best {curBestMove.ToString().ToLower()} eval {curBestEval}");
#endif
                return curBestMove;
            }
        }
    }

    public int NegaMax(Board board, int alpha, int beta, int remDepth, int ply) {
        //Check if time is up
        if(searchTimer.MillisecondsElapsedThisTurn >= searchAbortTime)
#if DEBUG
            throw new TimeoutException();
#else
            throw new Exception();
#endif

#if STATS
        STAT_NewNode_I();
#endif

        //Handle repetition
        if(board.IsRepeatedPosition()) return 0;

        //Check if the position is in the TT
        //We can't use the TT for the root node, as we don't store the best move in the table to save space
        ulong boardHash = board.ZobristKey;
        ref ulong ttSlot = ref transposTable[boardHash & TTIdxMask];
        if(ply > 0 && CheckTTEntry_I(ttSlot, boardHash, alpha, beta, remDepth)) {
            //The evaluation is stored in the lower 16 bits of the entry
            return unchecked((short) ttSlot);
        }

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

        //Search for the best move
        int bestScore = Eval.MinEval;
        TTBoundType ttBound = TTBoundType.Upper; //Until we become a PV node we only have a lower-bound

        for(int i = 0; i < moves.Length; i++) {
            //Recursively evaluate the move
            board.MakeMove(moves[i]);
            int score = -NegaMax(board, -beta, -alpha, remDepth-1, ply+1);
            board.UndoMove(moves[i]);

            //Update the best score
            if(score > bestScore) {
                bestScore = score;
                if(ply == 0) rootBestMove = moves[i];
            }

            //Update alpha/beta bounds
            //Do this after the best score update to implement a fail-soft alpha-beta search
            if(score >= beta) {
                ttBound = TTBoundType.Lower; //We failed high; our score now only is a lower bound
                break;
            }

            if(score >= alpha) {
                alpha = score;
                ttBound = TTBoundType.Exact; //We raised alpha and became a PV-node; our score is now exact
            }
        }

        //Insert the move into the transposition table
        //TODO: Currently always replaces, investigate potential other strategies
#if STATS
        STAT_CheckForTTCollision_I(ttSlot, boardHash);
#endif
        ttSlot = EncodeTTEntry_I((short) bestScore, ttBound, remDepth, boardHash);

        return bestScore;
    }
}