using ChessChallenge.API;
using System;
using System.Diagnostics;
using System.Linq;

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

    public long Get(Move move)
    {
        int index = (move.StartSquare.Index << 6) | move.TargetSquare.Index;
        return data[index];
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
    public static int OrderNoisyMoves(Board position, ref Move[] moves, int startIndex)
    {
        InsertionSortFlags(ref moves, startIndex);
        int promo = moves.TakeWhile(t => t.IsPromotion).Count();
        int noisy = moves.TakeWhile(t => t.IsCapture || t.IsPromotion).Count();
        InsertionSortBy(ref moves, promo, noisy, (lhs, rhs) =>
        {
            int mvv = CmpMVV(position, lhs, rhs);
            if (mvv != 0)
            {
                return mvv > 0;
            }
            else
            {
                return CmpLVA(position, lhs, rhs);
            }
        });
        return noisy;
    }

    public static int OrderQuietMoves(ref Move[] moves, int startIndex, KillerTable kt, HistoryTable history)
    {
        int len = moves.Length - startIndex;
        foreach (Move move in kt.data)
        {
            if (move.IsNull)
            {
                break;
            }
            int index = moves.TakeWhile(t => t != move).Count();
            if (index >= startIndex && index < moves.Length)
            {
                Move temp = moves[0 + startIndex];
                moves[0 + startIndex] = moves[index];
                moves[index] = temp;
                startIndex++;
            }
        }
        InsertionSortBy(ref moves, startIndex, moves.Length, (lhs, rhs) => history.Get(lhs) < history.Get(rhs));
        return len;
    }

    public static void InsertionSortBy(ref Move[] moves, int startIndex, int endIndex, Func<Move, Move, bool> cmp)
    {
        for (int i = startIndex + 1; i < endIndex; i++)
        {
            Move move = moves[i];
            int j = i;
            while (j > 0)
            {
                if (cmp(moves[j - 1], move))
                {
                    moves[j] = moves[j - 1];
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
            return 1;
        }
        else
        {
            return -1;
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