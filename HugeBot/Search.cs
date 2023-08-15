using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace HugeBot;

static class Search
{
    // NOTE: default values shouldnt exist once we are called reset at the start of each match
    public static KillerTable[] ply = Enumerable.Repeat<KillerTable>(new KillerTable(), 6144).ToArray();
    public static HistoryTable[] history = Enumerable.Repeat<HistoryTable>(new HistoryTable(), 2).ToArray();

    public static void Reset()
    {
        ply = new KillerTable[6144];
        history = new HistoryTable[2];
    }

    public static Move SearchMoves(Board board)
    {
        // TODO: dynamic depth and timer
        (Move, int)? best = null;
        foreach (Move move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            (Move, int) found = (move, AlphaBeta(board, 3, Eval.MinEval, Eval.MaxEval, 0)); // setting depth to 5 just for testing
            board.UndoMove(move);
            if (best == null || (board.IsWhiteToMove && best?.Item2 < found.Item2) || (!board.IsWhiteToMove && best?.Item2 > found.Item2)) {
                best = found;
            }
        }
        return (Move)best?.Item1;
    }

    public static int AlphaBeta(Board board, uint depth, int alpha, int beta, int plyIndex)
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
        Move[] moves = board.GetLegalMoves();
        int noisy = MoveOrder.OrderNoisyMoves(board, ref moves, 0);
        MoveOrder.OrderQuietMoves(ref moves, noisy, ply[plyIndex], history[board.IsWhiteToMove ? 0 : 1]);
        int i = 0;
        if (turn)
        {
            int value = Eval.MinEval;
            foreach (Move move in moves)
            {
                board.MakeMove(move);
                value = Math.Max(value, AlphaBeta(board, depth - 1, alpha, beta, plyIndex + 1));
                board.UndoMove(move);
                alpha = Math.Max(alpha, value);
                if (value >= beta)
                {
                    ply[plyIndex].BetaCutoff(move);
                    HistoryTable table = history[board.IsWhiteToMove ? 0 : 1];
                    table.BetaCutoff(move, depth);
                    for (int j = noisy; j < i; j++)
                    {
                        table.FailedCutoff(moves[j], depth);
                    }
                    break;
                }
                i++;
            }
            return value;
        }
        else
        {
            int value = Eval.MaxEval;
            foreach (Move move in moves)
            {
                board.MakeMove(move);
                value = Math.Min(value, AlphaBeta(board, depth - 1, alpha, beta, plyIndex + 1));
                board.UndoMove(move);
                beta = Math.Min(beta, value);
                if (value <= alpha)
                {
                    ply[plyIndex].BetaCutoff(move);
                    HistoryTable table = history[board.IsWhiteToMove ? 0 : 1];
                    table.BetaCutoff(move, depth);
                    for (int j = noisy; j < i; j++)
                    {
                        table.FailedCutoff(moves[j], depth);
                    }
                    break;
                }
                i++;
            }
            return value;
        }
    }
}
