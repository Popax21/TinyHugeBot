
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

    // public static readonly Eval[] PieceSquareTable = DecompressEvals(new ushort[] {
    //     //Pawns
    //     0x00_00, 0x00_00, 0x00_00, 0x00_00, 0x00_00, 0x00_00, 0x00_00, 0x00_00, 
    //     0x62_7f, 0x7f_7f, 0x3d_7f, 0x5f_7f, 0x44_7f, 0x7e_7f, 0x22_7f, 0xf5_7f, 
    //     0xfa_5e, 0x07_64, 0x1a_55, 0x1f_43, 0x41_38, 0x38_35, 0x19_52, 0xec_54, 
    //     0xf2_20, 0x0d_18, 0x06_0d, 0x15_05, 0x17_fe, 0x0c_04, 0x11_11, 0xe9_11, 
    //     0xe5_0d, 0xfe_09, 0xfb_fd, 0x0c_f9, 0x11_f9, 0x06_f8, 0x0a_03, 0xe7_ff, 
    //     0xe6_04, 0xfc_07, 0xfc_fa, 0xf6_01, 0x03_00, 0x03_fb, 0x21_ff, 0xf4_f8, 
    //     0xdd_0d, 0xff_08, 0xec_08, 0xe9_0a, 0xf1_0d, 0x18_00, 0x26_02, 0xea_f9, 
    //     0x00_00, 0x00_00, 0x00_00, 0x00_00, 0x00_00, 0x00_00, 0x00_00, 0x00_00, 

    //     //Knights
    //     0x80_c6, 0xa7_da, 0xde_f3, 0xcf_e4, 0x3d_e1, 0x9f_e5, 0xf1_c1, 0x95_9d, 
    //     0xb7_e7, 0xd7_f8, 0x48_e7, 0x24_fe, 0x17_f7, 0x3e_e7, 0x07_e8, 0xef_cc, 
    //     0xd1_e8, 0x3c_ec, 0x25_0a, 0x41_09, 0x54_ff, 0x7f_f7, 0x49_ed, 0x2c_d7, 
    //     0xf7_ef, 0x11_03, 0x13_16, 0x35_16, 0x25_16, 0x45_0b, 0x12_08, 0x16_ee, 
    //     0xf3_ee, 0x04_fa, 0x10_10, 0x0d_19, 0x1c_10, 0x13_11, 0x15_04, 0xf8_ee, 
    //     0xe9_e9, 0xf7_fd, 0x0c_ff, 0x0a_0f, 0x13_0a, 0x11_fd, 0x19_ec, 0xf0_ea, 
    //     0xe3_d6, 0xcb_ec, 0xf4_f6, 0xfd_fb, 0xff_fe, 0x12_ec, 0xf2_e9, 0xed_d4, 
    //     0x97_e3, 0xeb_cd, 0xc6_e9, 0xdf_f1, 0xef_ea, 0xe4_ee, 0xed_ce, 0xe9_c0, 

    //     //Bishops
    //     0xe3_f2, 0x04_eb, 0xae_f5, 0xdb_f8, 0xe7_f9, 0xd6_f7, 0x07_ef, 0xf8_e8, 
    //     0xe6_f8, 0x10_fc, 0xee_07, 0xf3_f4, 0x1e_fd, 0x3b_f3, 0x12_fc, 0xd1_f2, 
    //     0xf0_02, 0x25_f8, 0x2b_00, 0x28_ff, 0x23_fe, 0x32_06, 0x25_00, 0xfe_04, 
    //     0xfc_fd, 0x05_09, 0x13_0c, 0x32_09, 0x25_0e, 0x25_0a, 0x07_03, 0xfe_02, 
    //     0xfa_fa, 0x0d_03, 0x0d_0d, 0x1a_13, 0x22_07, 0x0c_0a, 0x0a_fd, 0x04_f7, 
    //     0x00_f4, 0x0f_fd, 0x0f_08, 0x0f_0a, 0x0e_0d, 0x1b_03, 0x12_f9, 0x0a_f1, 
    //     0x04_f2, 0x0f_ee, 0x10_f9, 0x00_ff, 0x07_04, 0x15_f7, 0x21_f1, 0x01_e5, 
    //     0xdf_e9, 0xfd_f7, 0xf2_e9, 0xeb_fb, 0xf3_f7, 0xf4_f0, 0xd9_fb, 0xeb_ef, 

    //     //Rooks
    //     0x20_0d, 0x2a_0a, 0x20_12, 0x33_0f, 0x3f_0c, 0x09_0c, 0x1f_08, 0x2b_05, 
    //     0x1b_0b, 0x20_0d, 0x3a_0d, 0x3e_0b, 0x50_fd, 0x43_03, 0x1a_08, 0x2c_03, 
    //     0xfb_07, 0x13_07, 0x1a_07, 0x24_05, 0x11_04, 0x2d_fd, 0x3d_fb, 0x10_fd, 
    //     0xe8_04, 0xf5_03, 0x07_0d, 0x1a_01, 0x18_02, 0x23_01, 0xf8_ff, 0xec_02, 
    //     0xdc_03, 0xe6_05, 0xf4_08, 0xff_04, 0x09_fb, 0xf9_fa, 0x06_f8, 0xe9_f5, 
    //     0xd3_fc, 0xe7_00, 0xf0_fb, 0xef_ff, 0x03_f9, 0x00_f4, 0xfb_f8, 0xdf_f0, 
    //     0xd4_fa, 0xf0_fa, 0xec_00, 0xf7_02, 0xff_f7, 0x0b_f7, 0xfa_f5, 0xb9_fd, 
    //     0xed_f7, 0xf3_02, 0x01_03, 0x11_ff, 0x10_fb, 0x07_f3, 0xdb_04, 0xe6_ec, 

    //     //Queens
    //     0xe4_f7, 0x00_16, 0x1d_16, 0x0c_1b, 0x3b_1b, 0x2c_13, 0x2b_0a, 0x2d_14, 
    //     0xe8_ef, 0xd9_14, 0xfb_20, 0x01_29, 0xf0_3a, 0x39_19, 0x1c_1e, 0x36_00, 
    //     0xf3_ec, 0xef_06, 0x07_09, 0x08_31, 0x1d_2f, 0x38_23, 0x2f_13, 0x39_09, 
    //     0xe5_03, 0xe5_16, 0xf0_18, 0xf0_2d, 0xff_39, 0x11_28, 0xfe_39, 0x01_24, 
    //     0xf7_ee, 0xe6_1c, 0xf7_13, 0xf6_2f, 0xfe_1f, 0xfc_22, 0x03_27, 0xfd_17, 
    //     0xf2_f0, 0x02_e5, 0xf5_0f, 0xfe_06, 0xfb_09, 0x02_11, 0x0e_0a, 0x05_05, 
    //     0xdd_ea, 0xf8_e9, 0x0b_e2, 0x02_f0, 0x08_f0, 0x0f_e9, 0xfd_dc, 0x01_e0, 
    //     0xff_df, 0xee_e4, 0xf7_ea, 0x0a_d5, 0xf1_fb, 0xe7_e0, 0xe1_ec, 0xce_d7, 

    //     //Kings
    //     0xbf_b6, 0x17_dd, 0x10_ee, 0xf1_ee, 0xc8_f5, 0xde_0f, 0x02_04, 0x0d_ef, 
    //     0x1d_f4, 0xff_11, 0xec_0e, 0xf9_11, 0xf8_11, 0xfc_26, 0xda_17, 0xe3_0b, 
    //     0xf7_0a, 0x18_11, 0x02_17, 0xf0_0f, 0xec_14, 0x06_2d, 0x16_2c, 0xea_0d, 
    //     0xef_f8, 0xec_16, 0xf4_18, 0xe5_1b, 0xe2_1a, 0xe7_21, 0xf2_1a, 0xdc_03, 
    //     0xcf_ee, 0xff_fc, 0xe5_15, 0xd9_18, 0xd2_1b, 0xd4_17, 0xdf_09, 0xcd_f5, 
    //     0xf2_ed, 0xf2_fd, 0xea_0b, 0xd2_15, 0xd4_17, 0xe2_10, 0xf1_07, 0xe5_f7, 
    //     0x01_e5, 0x07_f5, 0xf8_04, 0xc0_0d, 0xd5_0e, 0xf0_04, 0x09_fb, 0x08_ef, 
    //     0xf1_cb, 0x24_de, 0x0c_eb, 0xca_f5, 0x08_e4, 0xe4_f2, 0x18_e8, 0x0e_d5,
    // });
}