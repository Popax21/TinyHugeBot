using ChessChallenge.API;
using System;

namespace HugeBot;

// ported from STRO4K (https://github.com/ONE-RANDOM-HUMAN/STRO4K)

public static class KillerTable {
    public const int TableSize = 2;

    public static void Reset(Move[] table) => Array.Clear(table);

    public static void OnBetaCutoff(Move[] table, Move move) {
        //Insert into the killer table
        for(int i = 1; i < TableSize; i++) table[i] = table[i - 1];
        table[0] = move;
    }
}

public static class HistoryTable {
    public const int TableSize = 64*64;

    public static void Reset(long[] table) => Array.Clear(table);

    public static long Get(long[] table, Move move) => table[move.RawValue & 0xfff];
    public static void OnBetaCutoff(long[] table, Move move, int depth) => table[move.RawValue & 0xfff] += (long) depth * depth;
    public static void OnFailedCutoff(long[] table, Move move, int depth) => table[move.RawValue & 0xfff] -= depth;
}

public static class MoveOrder {
    public static int OrderNoisyMoves(Span<Move> moves) {
        //Sort by move group
        static int GetMoveGroup(Move move) => (move.IsPromotion ? 0 : 0b100) | (move.IsCapture ? 0 : 0b010) | (move.IsCastles ? 0 : 0b001);
        moves.Sort(static (a, b) => GetMoveGroup(a).CompareTo(GetMoveGroup(b)));

        //Find the first non-promo and quiet move
        int nonPromoMovesIdx = 0;
        while(nonPromoMovesIdx < moves.Length && moves[nonPromoMovesIdx].IsPromotion) nonPromoMovesIdx++;

        int quietMovesIdx = nonPromoMovesIdx;
        while(quietMovesIdx < moves.Length && moves[quietMovesIdx].IsCapture) quietMovesIdx++;

        //Sort the non-quiet moves
        moves[nonPromoMovesIdx..quietMovesIdx].Sort(static (a, b) => {
            //Sort by the captured piece's value (more valuable pieces first)
            if(a.CapturePieceType != b.CapturePieceType) return -a.CapturePieceType.CompareTo(b.CapturePieceType);

            //Sort by the moved piece's value (less valuable pieces first)
            return a.MovePieceType.CompareTo(b.MovePieceType);
        });

        return quietMovesIdx;
    }

    public static int OrderQuietMoves(Span<Move> moves, long[] historyTable, Move[] killerTable) {
        //Put moves in the killer table at the start
        int killerInsertIdx = 0;
        for(int i = 0; i < KillerTable.TableSize; i++) {
            if(killerTable[i].IsNull) break;
            ushort rawKillerMove = killerTable[i].RawValue;

            for(int j = killerInsertIdx; j < moves.Length; j++) {
                if(moves[j].RawValue == rawKillerMove) {
                    //Swap the move to move it to the start
                    //We'll sort the moves anyway later, so this is good to do
                    (moves[killerInsertIdx], moves[j]) = (moves[j], moves[killerInsertIdx]);
                    killerInsertIdx++;
                    break;
                }
            }
        }

        //Sort the remaining moves using the history table
        moves[killerInsertIdx..].Sort((a, b) => -HistoryTable.Get(historyTable, a).CompareTo(HistoryTable.Get(historyTable, b)));

        return moves.Length;
    }
}