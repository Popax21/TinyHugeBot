using System.Numerics;
using BitBoard = System.UInt64;
using Eval = System.UInt64;

namespace HugeBot;

public static partial class Evaluator {
    public static Eval EvalPawnStructure(BitBoard pawns) {
        Eval eval = 0x800_00000_800_00000;
        BitBoard file = AFile;
        for(int i = 0; i < 8; i++, file <<= 1) {
            //Get the number of pawns on this file
            uint pawnCount = (uint) BitOperations.PopCount(pawns & file);
            if(pawnCount == 0) continue;

            //Penalize multiple pawns on the same file
            if(pawnCount > 1) eval -= DoubledPawnPenalty[i] * (pawnCount - 1);

            //Check if there are pawns on the adjacent files, and if not add a penalty
            BitBoard adjacentFiles = ((file << 1) & ~AFile) | ((file & ~AFile) >> 1);
            if((pawns & adjacentFiles) == 0) eval -= IsolatedPawnPenalty[i] * pawnCount;
        }
        return eval & 0x000_fffff_000_fffff;
    }

    public static readonly Eval[] DoubledPawnPenalty = DecompressEvals(new ushort[] {
        0x3b_35,
        0x22_29,
        0x3d_1e,
        0x26_10,
        0x3d_19,
        0x33_2f,
        0x13_24,
        0x29_30,
    });

    public static readonly Eval[] IsolatedPawnPenalty = DecompressEvals(new ushort[] {
        0x21_14,
        0x16_16,
        0x3a_1d,
        0x40_29,
        0x57_2d,
        0x29_27,
        0x1b_15,
        0x58_1f,
    });
}