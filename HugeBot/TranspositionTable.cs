using System;
using ChessChallenge.API;

namespace HugeBot;

public static class TTBound {
    public const int None = 0, Lower = 1, Upper = 2, Exact = 3;
};

public static class TranspositionTable {
    public const int TableSize = 128*1024*1024 / 8; //For a ~128MB transposition table

    private const int TTDepthMask = 0x3fff, TTDepthSignBit = 0x2000;

    public static void Reset(ulong[] table) => Array.Clear(table);

    public static void Store(ulong[] table, ulong hash, Move move, int eval, int depth, byte bound) {
        depth = Math.Clamp(depth, -(1 << 13), +(1 << 13) - 1);
        table[hash % TableSize] =
            move.RawValue |
            ((ulong) (short) eval) << 16 |
            ((ulong) bound) << 32 |
            (((ulong) depth) & TTDepthMask) << 34 |
            (hash & 0xffff_0000_0000_0000)
        ;
    }

    public static bool Lookup(ulong[] table, ulong hash, out ushort rawMove, out int eval, out int depth, out byte bound) {
        ulong ttData = table[hash % TableSize];
    
        //Check if the upper bits of the hash match
        if((ttData & 0xffff_0000_0000_0000) != (hash & 0xffff_0000_0000_0000)) {
            rawMove = default;
            eval = default;
            bound = default;
            depth = default;
            return false;
        }

        //Decode the table data
        rawMove = (ushort) ttData;
        eval = (short) (ttData >> 16);
        bound = (byte) ((ttData >> 32) & 0b11);
        depth = (int) (ttData >> 34) & TTDepthMask;
        depth = (depth & TTDepthSignBit) - (depth & ~TTDepthSignBit);
        return true;
    }
}