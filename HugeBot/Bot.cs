﻿using System;
using ChessChallenge.API;
using HugeBot;

public class MyBot : IChessBot {
    public const int MinEval = -30000, MaxEval = +30000;

    private Timer searchTimer = null!;
    private int searchAbortTime;

    private Move bestMove;

    public Move Think(Board board, Timer timer) {
        //Determine search times
        searchTimer = timer;
        searchAbortTime = timer.MillisecondsRemaining / 2;

        int deepeningSearchTime = timer.MillisecondsRemaining / 20;

        //Do a NegaMax search with iterative deepening
        Move curBestMove = default;
        for(int depth = 1;; depth++) {
            //Do a NegaMax search with the current depth
            try {
                NegaMax(board, MinEval, MaxEval, depth, 0);
                curBestMove = bestMove; //Update the best move
            } catch {}

            //Check if time is up
            if(timer.MillisecondsElapsedThisTurn >= deepeningSearchTime) {
#if DEBUG
                Console.WriteLine($"Searched to depth {depth} in {timer.MillisecondsElapsedThisTurn:d5}ms: best {curBestMove.ToString().ToLower()}");
#endif
                return curBestMove;
            }
        }
    }

    public int NegaMax(Board board, int alpha, int beta, int remDepth, int ply) {
        //Check if time is up
        if(searchTimer.MillisecondsElapsedThisTurn >= searchAbortTime) throw new Exception();

        //Check if we reached the bottom of the search tree
        //TODO Quiescence search
        if(remDepth <= 0) return 0; //Evaluate(board);

        //Generate legal moves
        Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves);

        //Search for the best move
        int bestScore = MinEval;
        for(int i = 0; i < moves.Length; i++) {
            //Recursively evaluate the move
            board.MakeMove(moves[i]);
            int score = -NegaMax(board, -beta, -alpha, remDepth-1, ply+1);
            board.UndoMove(moves[i]);

            //Update the best score
            if(score > bestScore) {
                bestScore = score;
                if(ply == 0) bestMove = moves[i];
            }

            //Update alpha/beta bounds
            if(score >= beta) break; //Do this after the best score update to implement a fail-soft alpha-beta search
            if(score > alpha) alpha = score;
        }

        return bestScore;
    }
}