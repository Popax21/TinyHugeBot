using System;
using System.Numerics;
using ChessChallenge.API;

namespace HugeBot;

//Evaluation function heavily based on ice4 (https://github.com/MinusKelvin/ice4)
//All credits belong to Mark Carlson (MinusKelvin) for their amazing engine!

public static class Eval {
    public const short MinEval = -30000, MaxEval = +30000;

    internal const ulong EvalMask = 0x0000_ffff_0000_ffffUL;

    private const ulong TempoEvalBoost = 0x0000_0002_0000_0006;
    private const ulong BishopPairEval = 0x0000_0029_0000_0017;
    private const ulong RookOpenFileEval = 0x0000_000b_0000_001d;
    private const ulong RookSemiOpenFileEval = 0x0000_0010_0000_0010;

    private static readonly int[] PSTIndices = { 0, 2, 3, 4, 5, 6 };
    private static readonly int[] PhaseContributions = { 0, 1, 1, 2, 4, 0 };

    public static int Evaluate(Board board) {
        //TODO Incremental evaluation
        //TODO More evaluation features (currently only ice4's are implemented - figure out how to tune new / existing ones)
        //TODO Pawn structure / passed pawns
        ulong eval = 0x8000_0000_8000_0000;

        //Evaluate PST and determine phase
        int phase = 0;
        for(int m = 0; m <= PSTTable.BlackPiece; m += PSTTable.BlackPiece) {
            for(int i = 0; i < 6; i++) {
                ulong bboard = board.GetPieceBitboard((PieceType) (i+1), m == 0);
                while(bboard != 0) {
                    eval += PSTTable.PieceSquareTable[PSTIndices[i] | m][BitOperations.TrailingZeroCount(bboard)];
                    phase += PhaseContributions[i];
                    bboard &= bboard - 1;
                }
            }
        }

        //Give the side to move a small tempo eval boost
        eval += board.IsWhiteToMove ? TempoEvalBoost : ((~TempoEvalBoost + 1) & EvalMask);

        //Evaluate bishop pairs
        if(board.GetPieceList(PieceType.Bishop, true).Count >= 2)   eval += BishopPairEval;
        if(board.GetPieceList(PieceType.Bishop, false).Count >= 2)  eval -= BishopPairEval;

        //Evaluate rooks on (semi-)open files
        ulong whitePawns = board.GetPieceBitboard(PieceType.Pawn, true), blackPawns = board.GetPieceBitboard(PieceType.Pawn, false);
        ulong whiteRooks = board.GetPieceBitboard(PieceType.Rook, true), blackRooks = board.GetPieceBitboard(PieceType.Rook, false);

        ulong fileMask = 0x0101010101010101;
        for(int i = 0; i < 8; i++) {
            if((whiteRooks & fileMask) != 0 && (whitePawns & fileMask) == 0) {
                eval += ((blackPawns & fileMask) == 0) ? RookOpenFileEval : RookSemiOpenFileEval;
            }
            if((blackRooks & fileMask) != 0 && (blackPawns & fileMask) == 0) {
                eval -= ((whitePawns & fileMask) == 0) ? RookOpenFileEval : RookSemiOpenFileEval;
            }
            unchecked { fileMask <<= 1; }
        }

#if DEBUG
        //Check that the phase is in-range
        if(phase < 0) throw new Exception($"Unexpected evaluation phase value: {phase}");
#endif

        //Handle early promotion
        if(phase > 24) phase = 24;

        //Resolve the evaluation
        short mgEval = unchecked((short) eval), egEval = unchecked((short) (eval >> 32));
        int resEval = (mgEval * phase + egEval * (24 - phase)) / 24;
        return board.IsWhiteToMove ? resEval : -resEval;
    }
}