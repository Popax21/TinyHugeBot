using System;
using System.Numerics;
using ChessChallenge.API;

namespace HugeBot;

//Evaluation function heavily based on ice4 (https://github.com/MinusKelvin/ice4)
//All credits belong to Mark Carlson (MinusKelvin) for their amazing engine!

public static class Eval {
    private static readonly int[] PSTIndices = { 0, 2, 3, 4, 5, 6 };
    private static readonly int[] PhaseContributions = { 0, 1, 1, 2, 4, 0 };

    public static int Evaluate(Board board) {
        //TODO Incremental evaluation
        //TODO Pawn structure / passed pawns
        //TODO Bishop pairs
        //TODO Rook (semi-)open file reward

        //Determine PST evaluation and phase
        ulong pstEval = 0;
        int phase = 0;
        for(int m = 0; m <= PSTTable.BlackPiece; m += PSTTable.BlackPiece) {
            for(int i = 0; i < 6; i++) {
                ulong bboard = board.GetPieceBitboard((PieceType) (i+1), m == 0);
                while(bboard != 0) {
                    pstEval += PSTTable.PieceSquareTable[PSTIndices[i] | m][BitOperations.TrailingZeroCount(bboard)];
                    phase += PhaseContributions[i];
                    bboard &= bboard - 1;
                }
            }
        }

#if DEBUG
        //Check that the phase is in-range
        if(phase < 0) throw new Exception($"Unexpected evaluation phase value: {phase}");
#endif

        //Handle early promotion
        if(phase > 24) phase = 24;

        //Resolve the evaluation
        short mgEval = unchecked((short) pstEval), egEval = unchecked((short) (pstEval >> 32));
        int resEval = (mgEval * phase + egEval * (24 - phase)) / 24;
        return board.IsWhiteToMove ? resEval : -resEval;
    }
}