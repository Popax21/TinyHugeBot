using ChessChallenge.API;
using System;
using System.Numerics;
using BitBoard = System.UInt64;
using Eval = System.UInt64;

namespace HugeBot;

//Evaluation Format: 0x00_MMMMMM_00_EEEEEE
// - MMMM/EEEE: midgame/endgame evaluation (24 bit int)
// - the middle regions are buffers for catching carry bits

public static partial class Evaluator {
    public const BitBoard All           = 0xffff_ffff_ffff_ffff;
    public const BitBoard AFile         = 0x0101_0101_0101_0101;
    public const BitBoard HFile         = 0x8080_8080_8080_8080;
    public const BitBoard ABFile        = 0x0303_0303_0303_0303;
    public const BitBoard DarkSquares   = 0xAA55_AA55_AA55_AA55;
    public const BitBoard LightSquares  = 0x55AA_55AA_55AA_55AA;

    public const Eval BishopPairEval = 0x00_000070_00_0000a1;

    public static int Evaluate(Board board) {
        Eval eval = 0x80_000000_80_000000; //Don't zero the buffer regions to prevent underflows

        //Get piece bitboards
        Span<BitBoard> whitePieces = stackalloc BitBoard[6], blackPieces = stackalloc BitBoard[6];
        for(int i = 0; i < 6; i++) {
            whitePieces[i] = board.GetPieceBitboard((PieceType) i+1, true);
            blackPieces[i] = board.GetPieceBitboard((PieceType) i+1, false);
        }

        //Evaluate materials (except kings)
        for(int i = 0; i < 5; i++) {
            eval += MaterialEval[i] * (uint) BitOperations.PopCount(whitePieces[i]);
            eval -= MaterialEval[i] * (uint) BitOperations.PopCount(blackPieces[i]);
        }

        //Evaluate bishop pairs
        if((whitePieces[2] & DarkSquares) != 0) eval += BishopPairEval;
        if((blackPieces[2] & DarkSquares) != 0) eval -= BishopPairEval;

        //Evaluate the board
        static BitBoard FlipRows(BitBoard board) {
            board = ((board >>  8) & 0x00ff00ff00ff00ff) | ((board <<  8) & 0xff00ff00ff00ff00);
            board = ((board >> 16) & 0x0000ffff0000ffff) | ((board << 16) & 0xffff0000ffff0000);
            board = ((board >> 32) & 0x00000000ffffffff) | ((board << 32) & 0xffffffff00000000);
            return board;
        }

        eval += EvalPieceSquareTable(whitePieces, 0b0000);
        eval -= EvalPieceSquareTable(blackPieces, 0b1100);
        eval += EvalMobility(whitePieces, ~board.AllPiecesBitboard, ~board.WhitePiecesBitboard);
        eval -= EvalMobility(blackPieces, ~board.AllPiecesBitboard, ~board.BlackPiecesBitboard);
        eval += EvalPawnStructure(whitePieces[0]);
        eval -= EvalPawnStructure(blackPieces[0]);
        eval += EvalWhitePassedPawns(whitePieces[0], blackPieces[0]);
        eval -= EvalWhitePassedPawns(FlipRows(blackPieces[0]), FlipRows(whitePieces[0]));
        eval += EvalOpenFile(whitePieces[3], whitePieces[0], blackPieces[0]);
        eval -= EvalOpenFile(blackPieces[3], blackPieces[0], whitePieces[0]);

        //Determine the phase of the game
        int phase = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        for(int i = 0; i < 4; i++) phase += PiecePhaseWeights[i] * (pieces[i+1].Count + pieces[i+1 + 6].Count);

        //Determine the resolved evaluation score
        int mgEval = (int) ((eval >> 32) & 0xffffff), egEval = (int) (eval & 0xffffff);
        if((mgEval & 0x800000) != 0) mgEval -= 0x1000000;
        if((egEval & 0x800000) != 0) egEval -= 0x1000000;

        int score = (mgEval * phase + egEval * (24 - phase)) / 24;
        return board.IsWhiteToMove ? score : -score;
    }

    public static readonly Eval[] MaterialEval = {
        /* Pawns   */ 0x00_00013e_00_000137,
        /* Knights */ 0x00_00029a_00_000297,
        /* Bishops */ 0x00_000302_00_0002d3,
        /* Rooks   */ 0x00_00047a_00_000539,
        /* Queens  */ 0x00_00098f_00_000956,
    };

    public static readonly int[] PiecePhaseWeights = {
        /* Knights */ 1,
        /* Bishops */ 1,
        /* Rooks   */ 2,
        /* Queens  */ 4,
    };
}