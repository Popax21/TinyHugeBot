using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;

namespace HugeBot;

class Search
{
    // TODO: history
    public static Move SearchMoves(Board board)
    {
        // TODO: dynamic depth and timer
        var moves = new List<(Move, int)>();
        foreach (Move move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            moves.Add((move, AlphaBeta(board, 7, Eval.MinEval, Eval.MaxEval))); // setting depth to 7 just for testing
            board.UndoMove(move);
        }
        Func<(Move, int), int> selector = item => item.Item2;
        return board.IsWhiteToMove ? moves.MaxBy(selector).Item1 : moves.MinBy(selector).Item1;
    }

    public static int AlphaBeta(Board board, uint depth, int alpha, int beta)
    {
        bool turn = board.IsWhiteToMove;
        if (board.IsInCheckmate())
        {
            return turn ? Eval.MinEval : Eval.MaxEval;
        }
        else if (depth == 0)
        {
            return Evaluator.Evaluate(board);
        }
        else if (board.IsInStalemate() || board.IsDraw())
        {
            return 0;
        }
        if (board.IsInCheck())
        {
            depth++;
        }
        if (turn)
        {
            int value = Eval.MinEval;
            foreach (Move move in board.GetLegalMoves())
            {
                board.MakeMove(move);
                value = Math.Max(value, AlphaBeta(board, depth - 1, alpha, beta));
                board.UndoMove(move);
                alpha = Math.Max(alpha, value);
                if (value >= beta)
                {
                    break;
                }
            }
            return value;
        }
        else
        {
            int value = Eval.MaxEval;
            foreach (Move move in board.GetLegalMoves())
            {
                board.MakeMove(move);
                value = Math.Min(value, AlphaBeta(board, depth - 1, alpha, beta));
                board.UndoMove(move);
                beta = Math.Min(beta, value);
                if (value <= alpha)
                {
                    break;
                }
            }
            return value;
        }
    }
}
