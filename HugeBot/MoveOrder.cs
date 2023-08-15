using ChessChallenge.API;
using System;
using System.Diagnostics;

namespace HugeBot;

// ported from STRO4K (https://github.com/ONE-RANDOM-HUMAN/STRO4K/tree/master)
struct KillerTable
{
    public Move[] data;

    public KillerTable()
    {
        data = new Move[] { Move.NullMove, Move.NullMove };
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

static class MoveOrder
{
    public static uint OrderNoisyMoves(Board position, ref Move[] moves)
    {
        throw new NotImplementedException();
    }

    public static uint OrderQuietMoves(ref Move[] moves, int startIndex, KillerTable kt, HistoryTable history)
    {
        throw new NotImplementedException();
    }

    public static void InsertionSortBy(ref Move[] moves, int startIndex, int endIndex, Func<Move, Move, bool> cmp)
    {
        throw new NotImplementedException();
    }

    public static void InsertionSortFlags(ref Move[] moves, int startIndex)
    {
        for (int i = startIndex + 1; i < moves.Length; i++)
        {
            Move move = moves[i];
            int cmp = (move.IsPromotion ? 8 : 0) | (move.IsEnPassant ? 4 : 0) | (move.IsCapture ? 2 : 0) | (move.IsCastles ? 1 : 0);
            int j = i;
            while (j > 0)
            {
                Move move2 = moves[j - 1];
                int cmp2 = (move2.IsPromotion ? 8 : 0) | (move2.IsEnPassant ? 4 : 0) | (move2.IsCapture ? 2 : 0) | (move2.IsCastles ? 1 : 0);
                if (cmp2 < cmp)
                {
                    moves[j] = move2;
                }
                else
                {
                    break;
                }
                j--;
            }
            moves[j] = move;
        }
    }

    public static int CmpMVV(Board position, Move lhs, Move rhs)
    {
        Piece lhs_p = position.GetPiece(lhs.TargetSquare);
        int lhs_v = lhs_p.IsWhite != position.IsWhiteToMove ? (int)lhs_p.PieceType : 0;
        Piece rhs_p = position.GetPiece(rhs.TargetSquare);
        int rhs_v = rhs_p.IsWhite != position.IsWhiteToMove ? (int)rhs_p.PieceType : 0;
        if (lhs_v == rhs_v)
        {
            return 0;
        }
        else if (lhs_v < rhs_v)
        {
            return -1;
        }
        else
        {
            return 1;
        }
    }

    public static bool CmpLVA(Board position, Move lhs, Move rhs)
    {
        Piece lhs_p = position.GetPiece(lhs.StartSquare);
        int lhs_v = lhs_p.IsWhite == position.IsWhiteToMove ? (int)lhs_p.PieceType : 0;
        Piece rhs_p = position.GetPiece(rhs.StartSquare);
        int rhs_v = rhs_p.IsWhite == position.IsWhiteToMove ? (int)rhs_p.PieceType : 0;
        return lhs_v > rhs_v;
    }
}