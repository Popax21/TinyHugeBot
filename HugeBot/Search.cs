using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace HugeBot;

// ported from STRO4K (https://github.com/ONE-RANDOM-HUMAN/STRO4K/tree/master)
struct KillerTable
{
    public Move[] data;

    public KillerTable()
    {
        data = new Move[] {Move.NullMove, Move.NullMove};
    }

    public void BetaCutoff(Move move)
    {
        data[1] = data[0];
        data[0] = move;
    }
}

struct PlyData
{
    public KillerTable kt;
    public int staticEval;

    public PlyData()
    {
        kt = new KillerTable();
        staticEval = 0;
    }
}

struct HistoryTable
{
    public long[] data;

    public HistoryTable()
    {
        data = new long[4096];
    }

    public void Reset()
    {
        data = new long[4096];
    }

    public void BetaCutoff(Move move, long depth)
    {
        int index = (move.StartSquare.Index << 6) | move.TargetSquare.Index;
        data[index] += depth * depth;
    }

    public void FailedCutoff(Move move, long depth)
    {
        int index = (move.StartSquare.Index << 6) | move.TargetSquare.Index;
        data[index] -= depth;
    }
}

static class Search
{
    // TODO: history
    public static Move SearchMoves(Board board)
    {
        // TODO: dynamic depth and timer
        (Move, int)? best = null;
        foreach (Move move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            (Move, int) found = (move, AlphaBeta(board, 3, Eval.MinEval, Eval.MaxEval)); // setting depth to 5 just for testing
            board.UndoMove(move);
            if (best == null || (board.IsWhiteToMove && best?.Item2 < found.Item2) || (!board.IsWhiteToMove && best?.Item2 > found.Item2)) {
                best = found;
            }
        }
        return (Move)best?.Item1;
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
