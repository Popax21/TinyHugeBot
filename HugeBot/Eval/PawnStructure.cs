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
            if(pawnCount > 1) eval -= DoubledPawnPenalty[i] * (pawnCount - 1);

            //Check if there are pawns on the adjacent files, and if not add a penalty
            BitBoard adjacentFiles = ((file << 1) & ~AFile) | ((file & ~AFile) >> 1);
            if((pawns & adjacentFiles) == 0) eval -= IsolatedPawnPenalty[i] * pawnCount;
        }
        return eval;
    }

    public static readonly Eval[] DoubledPawnPenalty = {
        0x000_0003b_000_00035,
        0x000_00022_000_00029,
        0x000_0003d_000_0001e,
        0x000_00026_000_00010,
        0x000_0003d_000_00019,
        0x000_00033_000_0002f,
        0x000_00013_000_00024,
        0x000_00029_000_00030,
    };

    public static readonly Eval[] IsolatedPawnPenalty = {
        0x000_00021_000_00014,
        0x000_00016_000_00016,
        0x000_0003a_000_0001d,
        0x000_00040_000_00029,
        0x000_00057_000_0002d,
        0x000_00029_000_00027,
        0x000_0001b_000_00015,
        0x000_00058_000_0001f,
    };
}