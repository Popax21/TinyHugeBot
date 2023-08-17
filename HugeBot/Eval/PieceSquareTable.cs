
using System;
using System.Numerics;
using BitBoard = System.UInt64;
using Eval = System.UInt64;

namespace HugeBot;

static partial class Evaluator {
    public static Eval EvalPieceSquareTable(Span<BitBoard> pieces, byte pstIdxFlip) {
        Eval eval = 0x800_00000_800_00000;
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
                eval += PieceSquareTable[pieceType << 4 | pstIndex];
            }
        }
        return eval & 0x000_fffff_000_fffff;
    }

    public static readonly Eval[] PieceSquareTable = DecompressEvals(new ushort[] {
        //Pawns
        0xcb_02, 0xb6_13, 0x00_06, 0xf9_d8,
        0xc9_f2, 0xed_e1, 0xfd_e6, 0xf7_c8,
        0xff_22, 0x2b_f7, 0x4b_e6, 0x26_f1,
        0x29_4b, 0x28_38, 0x10_21, 0x05_1d,

        //Knights
        0xe8_ed, 0xda_df, 0xe8_df, 0xea_f9,
        0xe2_fc, 0xd5_f3, 0xd0_ec, 0x08_09,
        0x27_0d, 0x31_09, 0x1c_17, 0x46_1d,
        0x02_04, 0x17_0e, 0x14_0c, 0x05_01,

        //Bishops
        0x16_f0, 0xdb_e8, 0xe3_f0, 0x18_ee,
        0x06_04, 0x01_02, 0xe9_04, 0x0b_fa,
        0x04_0b, 0x1d_06, 0x1b_0f, 0x16_10,
        0xf9_03, 0x00_07, 0x02_03, 0xfa_fb,

        //Rooks
        0xc4_dd, 0xfc_da, 0xec_d1, 0xcd_d5,
        0xcf_f4, 0xe9_fd, 0xea_f7, 0xfb_ed,
        0x0d_17, 0x1c_1f, 0x1d_14, 0x1d_0a,
        0x1e_26, 0x2e_2c, 0x22_21, 0x21_1f,

        //Queens
        0xee_ea, 0xfc_d1, 0xf1_d2, 0xed_eb,
        0xe1_f9, 0xde_03, 0xf1_ff, 0x10_ff,
        0xef_fb, 0xfd_19, 0x1f_25, 0x3c_18,
        0xef_fe, 0x0d_17, 0x1f_1f, 0x22_0c,

        //Kings
        0x2a_e2, 0xec_da, 0xb5_da, 0x30_bc,
        0x05_fa, 0xfc_06, 0xf0_fd, 0xef_eb,
        0x0b_16, 0x0c_28, 0x09_2a, 0x06_17,
        0x06_07, 0x09_16, 0x0b_1c, 0x05_0e,
    });
}