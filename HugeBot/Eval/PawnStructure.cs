using System.Numerics;
using BitBoard = System.UInt64;
using Eval = System.UInt64;

namespace HugeBot;

public static partial class Evaluator {
    public static Eval EvalPawnStructure(BitBoard pawns) {
        Eval eval = 0;
        BitBoard file = AFile;
        for(int i = 0; i < 8; i++, file <<= 1) {
            //Get the number of pawns on this file
            uint pawnCount = (uint) BitOperations.PopCount(pawns & file);
            if(pawnCount == 0) continue;

            //Penalize multiple pawns on the same file
            if(pawnCount > 1) eval += DoubledPawnEval[i] * (pawnCount - 1);

            //Check if there are pawns on the adjacent files, and if not add a penalty
            BitBoard adjacentFile = ((file << 1) & ~AFile) | ((file & ~AFile) >> 1);
            if((pawns & adjacentFile) == 0) eval += IsolatedPawnEval[i] * pawnCount;
        }
        return eval;
    }

    public static readonly Eval[] DoubledPawnEval = {
        0x00_ffffc5_00_ffffcb,
        0x00_ffffde_00_ffffd7,
        0x00_ffffc3_00_ffffe2,
        0x00_ffffda_00_fffff0,
        0x00_ffffc3_00_ffffe7,
        0x00_ffffcd_00_ffffd1,
        0x00_ffffed_00_ffffdc,
        0x00_ffffd7_00_ffffd0,
    };

    public static readonly Eval[] IsolatedPawnEval = {
        0x00_ffffdf_00_ffffec,
        0x00_ffffea_00_ffffea,
        0x00_ffffc6_00_ffffe3,
        0x00_ffffc0_00_ffffd7,
        0x00_ffffa9_00_ffffd3,
        0x00_ffffd7_00_ffffd9,
        0x00_ffffe5_00_ffffeb,
        0x00_ffffa8_00_ffffe1,
    };
}