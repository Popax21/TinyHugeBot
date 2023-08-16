using System.Numerics;
using BitBoard = System.UInt64;
using Eval = System.UInt64;

namespace HugeBot;

public static partial class Evaluator {
    public const Eval OpenFileEval     = 0x000_00049_000_00000;
    public const Eval SemiOpenFileEval = 0x000_00026_000_00001;

    public static Eval EvalOpenFile(BitBoard ourRooks, BitBoard ourPawns, BitBoard enemyPawns) {
        BitBoard allPawns = ourPawns | enemyPawns;

        Eval eval = 0;
        BitBoard file = AFile;
        for(int i = 0; i < 8; i++, file <<= 1) {
            if((ourRooks & file) == 0) continue;

            //Check if there are any pawns in the way of our rooks
            if((allPawns & file) == 0) eval += OpenFileEval * (uint) BitOperations.PopCount(ourRooks & file);
            else {
                //Check if any of our pawns in the way of our rooks
                if((ourPawns & file) == 0) eval += SemiOpenFileEval * (uint) BitOperations.PopCount(ourRooks & file);
            }
        }
        return eval;
    }
}