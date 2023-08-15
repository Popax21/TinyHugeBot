using ChessChallenge.API;
using System;

namespace HugeBot;

public static class Search {
    //Use these to prevent integer overlow
    public const int MinEval = -int.MaxValue, MaxEval = +int.MaxValue;

    // TODO: history
    public static Move SearchMoves(Board board) {
        // TODO: dynamic depth and timer
        Move bestMove = Move.NullMove;
        int bestEval = MinEval;

        foreach(Move move in board.GetLegalMoves()) {
            //Use alpha-beta pruning to evaluate the move
            board.MakeMove(move);
            int moveEval = -AlphaBeta(board, 3, MinEval, MaxEval);
            board.UndoMove(move);

            //Check if this move is better
            if(bestEval < moveEval) (bestMove, bestEval) = (move, moveEval);
        }

        return bestMove;
    }

    public static int AlphaBeta(Board board, uint depth, int alpha, int beta) {
        //Check if we're in checkmate / stalemate / 50 move role
        if(board.IsInCheckmate()) return MinEval;
        if(board.IsInStalemate() || board.IsFiftyMoveDraw()) return 0;

        //Search one move more if we're in check
        if(board.IsInCheck()) depth++;

        //Check if we reached the bottom of our search
        if(depth == 0) return Evaluator.Evaluate(board);

        //Search moves we could make from here
        int maxEval = int.MinValue;
        foreach(Move move in board.GetLegalMoves()) {
            //Evaluate the move recursively
            board.MakeMove(move);
            int moveEval = -AlphaBeta(board, depth - 1, -beta, -alpha);
            board.UndoMove(move);

            //Update search variables
            maxEval = Math.Max(maxEval, moveEval);
            alpha = Math.Max(alpha, moveEval);
            if(moveEval >= beta) break;
        }
        return maxEval;
    }
}
