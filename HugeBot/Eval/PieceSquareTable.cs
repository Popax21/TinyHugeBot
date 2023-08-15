
using System;
using System.Numerics;
using BitBoard = System.UInt64;
using Eval = System.UInt64;

namespace HugeBot;

static partial class Evaluator {
    public static Eval EvalPieceSquareTable(Span<BitBoard> pieces, byte pstIdxFlip) {
        Eval eval = 0;
        for(int pieceType = 0; pieceType < 6; pieceType++) {
            BitBoard pieceBoard = pieces[pieceType];
            while(pieceBoard != 0) {
                //Get the piece's board index and move onto the next bit
                int boardIdx = BitOperations.TrailingZeroCount(pieceBoard);
                pieceBoard &= pieceBoard-1;

                //Convert the 8x8 board index into a 4x4 PST index
                int pstIndex = ((boardIdx >> 1) & 0b11) | ((boardIdx >> (3+1 - 2)) & 0b1100);
                pstIndex ^= pstIdxFlip; //Flip the row bits for the opposite side

                //Update the evaluation
                eval += PieceSquareTable[pieceType, pstIndex];
            }
        }
        return eval;
    }

    public static readonly Eval[,] PieceSquareTable = {
        //Pawns
        {
            0x00_ffffcb_00_000002, 0x00_ffffb6_00_000013, 0x00_000000_00_000006, 0x00_fffff9_00_ffffd8,
            0x00_ffffc9_00_fffff2, 0x00_ffffed_00_ffffe1, 0x00_fffffd_00_ffffe6, 0x00_fffff7_00_ffffc8,
            0x00_ffffff_00_000022, 0x00_00002b_00_fffff7, 0x00_00004b_00_ffffe6, 0x00_000026_00_fffff1,
            0x00_000029_00_00004b, 0x00_000028_00_000038, 0x00_000010_00_000021, 0x00_000005_00_00001d,
        },
        //Knights
        {
            0x00_ffffe8_00_ffffed, 0x00_ffffda_00_ffffdf, 0x00_ffffe8_00_ffffdf, 0x00_ffffea_00_fffff9,
            0x00_ffffe2_00_fffffc, 0x00_ffffd5_00_fffff3, 0x00_ffffd0_00_ffffec, 0x00_000008_00_000009,
            0x00_000027_00_00000d, 0x00_000031_00_000009, 0x00_00001c_00_000017, 0x00_000046_00_00001d,
            0x00_000002_00_000004, 0x00_000017_00_00000e, 0x00_000014_00_00000c, 0x00_000005_00_000001,
        },
        //Bishops
        {
            0x00_000016_00_fffff0, 0x00_ffffdb_00_ffffe8, 0x00_ffffe3_00_fffff0, 0x00_000018_00_ffffee,
            0x00_000006_00_000004, 0x00_000001_00_000002, 0x00_ffffe9_00_000004, 0x00_00000b_00_fffffa,
            0x00_000004_00_00000b, 0x00_00001d_00_000006, 0x00_00001b_00_00000f, 0x00_000016_00_000010,
            0x00_fffff9_00_000003, 0x00_000000_00_000007, 0x00_000002_00_000003, 0x00_fffffa_00_fffffb,
        },
        //Rooks
        {
            0x00_ffffc4_00_ffffdd, 0x00_fffffc_00_ffffda, 0x00_ffffec_00_ffffd1, 0x00_ffffcd_00_ffffd5,
            0x00_ffffcf_00_fffff4, 0x00_ffffe9_00_fffffd, 0x00_ffffea_00_fffff7, 0x00_fffffb_00_ffffed,
            0x00_00000d_00_000017, 0x00_00001c_00_00001f, 0x00_00001d_00_000014, 0x00_00001d_00_00000a,
            0x00_00001e_00_000026, 0x00_00002e_00_00002c, 0x00_000022_00_000021, 0x00_000021_00_00001f,
        },
        //Queens
        {
            0x00_ffffee_00_ffffea, 0x00_fffffc_00_ffffd1, 0x00_fffff1_00_ffffd2, 0x00_ffffed_00_ffffeb,
            0x00_ffffe1_00_fffff9, 0x00_ffffde_00_000003, 0x00_fffff1_00_ffffff, 0x00_000010_00_ffffff,
            0x00_ffffef_00_fffffb, 0x00_fffffd_00_000019, 0x00_00001f_00_000025, 0x00_00003c_00_000018,
            0x00_ffffef_00_fffffe, 0x00_00000d_00_000017, 0x00_00001f_00_00001f, 0x00_000022_00_00000c,
        },
        //Kings
        {
            0x00_00002a_00_ffffe2, 0x00_ffffec_00_ffffda, 0x00_ffffb5_00_ffffda, 0x00_000030_00_ffffbc,
            0x00_000005_00_fffffa, 0x00_fffffc_00_000006, 0x00_fffff0_00_fffffd, 0x00_ffffef_00_ffffeb,
            0x00_00000b_00_000016, 0x00_00000c_00_000028, 0x00_000009_00_00002a, 0x00_000006_00_000017,
            0x00_000006_00_000007, 0x00_000009_00_000016, 0x00_00000b_00_00001c, 0x00_000005_00_00000e,
        }
    };
}