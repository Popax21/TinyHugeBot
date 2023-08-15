using System.Numerics;
using BitBoard = System.UInt64;
using Eval = System.UInt64;

namespace HugeBot;

public static partial class Evaluator {
    public static Eval EvalWhitePassedPawns(BitBoard whitePawns, BitBoard blackPawns) {
        //Determine all spaces black pawns could reach / attack
        BitBoard blackPawnMask = blackPawns;

        // - all the spaces they can reach by moving forward
        blackPawnMask |= blackPawnMask >>  8;
        blackPawnMask |= blackPawnMask >> 16;
        blackPawnMask |= blackPawnMask >> 32;

        // - all the spaces they can attack
        blackPawnMask |= ((blackPawnMask >> 7) & ~AFile) | ((blackPawnMask & ~AFile) >> 9);

        //Discard all pawns which are blocked by a black pawn
        whitePawns &= ~blackPawnMask;
        if(whitePawns == 0) return 0;

        //Evaluate all remaining passed pawns
        Eval eval = 0;
        BitBoard file = AFile;
        for(int i = 0; i < 8; i++, file <<= 1) {
            int invPawnIdx = BitOperations.LeadingZeroCount(whitePawns & file);
            if(invPawnIdx <= 63 - 1*8) eval += PassedPawnEval[6 - (invPawnIdx / 8)];
        }
        return eval;
    }

    public static readonly Eval[] PassedPawnEval = {
        0x00_000000_00_000000,
        0x00_000000_00_000000,
        0x00_000000_00_000029,
        0x00_00001d_00_00003a,
        0x00_000066_00_00007e,
        0x00_000066_00_0000c1,
    };
}