using System;
using System.Numerics;
using ChessChallenge.API;

namespace HugeBot;

//PeSTO evaluation function
//TODO Replace with custom evaluation function

public static class Eval {
    public const short MinEval = -30000, MaxEval = +30000;
    public const short MinMate = -29000, MaxMate = +29000;
    public const int MinSentinel = -0x10_0000, MaxSentinel = +0x10_0000;

    internal const ulong EvalMask = 0x0000_ffff_0000_ffffUL;

    private static readonly int[] PhaseContributions = { 0, 1, 1, 2, 4, 0 };
    private static ulong[,] PeSTOTables = new ulong[12,64];

    public struct EvalState {
        internal bool isWhiteToMove;
        internal int phase;
        internal ulong eval;

        public int Resolve() {
            //Handle early promotion
            int clampedPhase = phase;
            if(clampedPhase > 24) clampedPhase = 24;

            //Interpolate between the midgame and endgame evaluation based on the phase
            short mgEval = unchecked((short) eval), egEval = unchecked((short) (eval >> 32));
            int resEval = (mgEval * clampedPhase + egEval * (24 - clampedPhase)) / 24;
            return isWhiteToMove ? resEval : -resEval;
        }

        public void Update_I(Move move) {
            int movedPiece = (int) move.MovePieceType - 1;
            int sideOffset = isWhiteToMove ? 0 : 6;

            //Update the old square
            eval -= PeSTOTables[movedPiece + sideOffset, move.StartSquare.Index];
            phase -= PhaseContributions[movedPiece];

            //Update the new square
            int targetSquare = move.TargetSquare.Index;
            int newPieceType = move.IsPromotion ? (int) move.PromotionPieceType - 1 : movedPiece;
            eval += PeSTOTables[newPieceType + sideOffset, targetSquare];
            phase += PhaseContributions[newPieceType];

            //Update captures (if any)
            if(move.IsCapture) {
                int capturedPiece = (int) move.CapturePieceType - 1;
                int captureSquare = targetSquare;
                if(move.IsEnPassant) captureSquare += isWhiteToMove ? -8 : +8; //En Passant captures a piece on a different square :)
                eval -= PeSTOTables[capturedPiece + (sideOffset ^ 6), captureSquare];
                phase -= PhaseContributions[capturedPiece];
            }

            //Handle castling
            if(move.IsCastles) {
                //This just sucks, I'm not gonna sugarcoat it
                int oldRookSquare = targetSquare & 56, newRookSquare = oldRookSquare;
                if((targetSquare & 7) == 2) {
                    newRookSquare |= 3; 
                } else {
                    oldRookSquare |= 7;
                    newRookSquare |= 5;
                }

                eval -= PeSTOTables[(int) PieceType.Rook - 1 + sideOffset, oldRookSquare];
                eval += PeSTOTables[(int) PieceType.Rook - 1 + sideOffset, newRookSquare];
            }

            //Update the side to move
            isWhiteToMove ^= true;

#if VALIDATE
            Validate();
#endif
        }

        public void SwitchSide_I() => isWhiteToMove ^= true;

#if VALIDATE
        public void Validate() {
            //Check that the phase is in-range
            if(phase < 0) throw new Exception($"Unexpected evaluation phase value: {phase}");

            //Check for potential overflows
            if((eval & 0x0000_0000_ffff_0000) < 0x0000_0000_6000_0000 || (eval & 0x0000_0000_ffff_0000) > 0x0000_0000_afff_0000) throw new Exception($"Potential MG eval overflow: 0x{eval:x16}");
            if((eval & 0xffff_0000_0000_0000) < 0x6000_0000_0000_0000 || (eval & 0xffff_0000_0000_0000) > 0xafff_0000_0000_0000) throw new Exception($"Potential EG eval overflow: 0x{eval:x16}");            
        }

        public void Check(Move move, EvalState evalState) {
            if(isWhiteToMove != evalState.isWhiteToMove || phase != evalState.phase || eval != evalState.eval) {
                throw new Exception($"Incremental eval difference after move {move.ToString()[7..^1]} ({move.MovePieceType} x {move.CapturePieceType}): {isWhiteToMove} {phase} 0x{eval:x16} vs {evalState.isWhiteToMove} {evalState.phase} 0x{evalState.eval:x16}");
            }
        }
#endif
    }

    public static EvalState Evaluate(Board board) {
        ulong eval = 0x8000_0000_8000_0000;

        //Evaluate PST and determine phase
        int phase = 0;
        for(int s = 0; s <= 6; s += 6) {
            for(int p = 0; p < 6; p++) {
                ulong bitboard = board.GetPieceBitboard((PieceType) p + 1, s == 0);
                while(bitboard != 0) {
                    eval += PeSTOTables[p+s, BitOperations.TrailingZeroCount(bitboard)];
                    phase += PhaseContributions[p];
                    bitboard &= bitboard-1;
                }
            }
        }

        EvalState state = new EvalState() { isWhiteToMove = board.IsWhiteToMove, eval = eval, phase = phase };
#if VALIDATE
        state.Validate();
#endif
        return state;
    }

    private static uint[,] ComprPeSTOTables = new uint[,] {
        //Pawn - White
        {
            0x005e_0052, 0x005e_0052, 0x005e_0052, 0x005e_0052, 0x005e_0052, 0x005e_0052, 0x005e_0052, 0x005e_0052, 
            0x006b_002f, 0x0066_0051, 0x0066_003e, 0x0068_003b, 0x006b_0043, 0x005e_006a, 0x0060_0078, 0x0057_003c, 
            0x0062_0038, 0x0065_004e, 0x0058_004e, 0x005f_0048, 0x005e_0055, 0x0059_0055, 0x005d_0073, 0x0056_0046, 
            0x006b_0037, 0x0067_0050, 0x005b_004d, 0x0057_005e, 0x0057_0063, 0x0056_0058, 0x0061_005c, 0x005d_0039, 
            0x007e_0044, 0x0076_005f, 0x006b_0058, 0x0063_0067, 0x005c_0069, 0x0062_005e, 0x006f_0063, 0x006f_003b, 
            0x00bc_004c, 0x00c2_0059, 0x00b3_006c, 0x00a1_0071, 0x0096_0093, 0x0093_008a, 0x00b0_006b, 0x00b2_003e, 
            0x0110_00b4, 0x010b_00d8, 0x00fc_008f, 0x00e4_00b1, 0x00f1_0096, 0x00e2_00d0, 0x0103_0074, 0x0119_0047, 
            0x005e_0052, 0x005e_0052, 0x005e_0052, 0x005e_0052, 0x005e_0052, 0x005e_0052, 0x005e_0052, 0x005e_0052, 
        },
        //Knight - White
        {
            0x00fc_00e8, 0x00e6_013c, 0x0102_0117, 0x010a_0130, 0x0103_0140, 0x0107_0135, 0x00e7_013e, 0x00d9_013a, 
            0x00ef_0134, 0x0105_011c, 0x010f_0145, 0x0114_014e, 0x0117_0150, 0x0105_0163, 0x0102_0143, 0x00ed_013e, 
            0x0102_013a, 0x0116_0148, 0x0118_015d, 0x0128_015b, 0x0123_0164, 0x0116_0162, 0x0105_016a, 0x0103_0141, 
            0x0107_0144, 0x0113_0155, 0x0129_0161, 0x0132_015e, 0x0129_016d, 0x012a_0164, 0x011d_0166, 0x0107_0149, 
            0x0108_0148, 0x011c_0162, 0x012f_0164, 0x012f_0186, 0x012f_0176, 0x0124_0196, 0x0121_0163, 0x0107_0167, 
            0x0101_0122, 0x0105_018d, 0x0123_0176, 0x0122_0192, 0x0118_01a5, 0x0110_01d2, 0x0106_019a, 0x00f0_017d, 
            0x0100_0108, 0x0111_0128, 0x0100_0199, 0x0117_0175, 0x0110_0168, 0x0100_018f, 0x0101_0158, 0x00e5_0140, 
            0x00df_00aa, 0x00f3_00f8, 0x010c_012f, 0x00fd_0120, 0x00fa_018e, 0x00fe_00f0, 0x00da_0142, 0x00b6_00e6, 
        },
        //Bishop - White
        {
            0x0112_014c, 0x0120_016a, 0x0112_015f, 0x0124_0158, 0x0120_0160, 0x0119_0161, 0x0124_0146, 0x0118_0158, 
            0x011b_0171, 0x0117_017c, 0x0122_017d, 0x0128_016d, 0x012d_0174, 0x0120_0182, 0x011a_018e, 0x010e_016e, 
            0x011d_016d, 0x0126_017c, 0x0131_017c, 0x0133_017c, 0x0136_017b, 0x012c_0188, 0x0122_017f, 0x011a_0177, 
            0x0123_0167, 0x012c_017a, 0x0136_017a, 0x013c_0187, 0x0130_018f, 0x0133_0179, 0x0126_0177, 0x0120_0171, 
            0x0126_0169, 0x0132_0172, 0x0135_0180, 0x0132_019f, 0x0137_0192, 0x0133_0192, 0x012c_0174, 0x012b_016b, 
            0x012b_015d, 0x0121_0192, 0x0129_0198, 0x0128_0195, 0x0127_0190, 0x012f_019f, 0x0129_0192, 0x012d_016b, 
            0x0121_0153, 0x0125_017d, 0x0130_015b, 0x011d_0160, 0x0126_018b, 0x011c_01a8, 0x0125_017f, 0x011b_013e, 
            0x011b_0150, 0x0114_0171, 0x011e_011b, 0x0121_0148, 0x0122_0154, 0x0120_0143, 0x0118_0174, 0x0111_0165, 
        },
        //Rook - White
        {
            0x01f7_01ca, 0x0202_01d0, 0x0203_01de, 0x01ff_01ee, 0x01fb_01ed, 0x01f3_01e4, 0x0204_01b8, 0x01ec_01c3, 
            0x01fa_01b1, 0x01fa_01cd, 0x0200_01c9, 0x0202_01d4, 0x01f7_01dc, 0x01f7_01e8, 0x01f5_01d7, 0x01fd_0196, 
            0x01fc_01b0, 0x0200_01c4, 0x01fb_01cd, 0x01ff_01cc, 0x01f9_01e0, 0x01f4_01dd, 0x01f8_01d8, 0x01f0_01bc, 
            0x0203_01b9, 0x0205_01c3, 0x0208_01d1, 0x0204_01dc, 0x01fb_01e6, 0x01fa_01d6, 0x01f8_01e3, 0x01f5_01c6, 
            0x0204_01c5, 0x0203_01d2, 0x020d_01e4, 0x0201_01f7, 0x0202_01f5, 0x0201_0200, 0x01ff_01d5, 0x0202_01c9, 
            0x0207_01d8, 0x0207_01f0, 0x0207_01f7, 0x0205_0201, 0x0204_01ee, 0x01fd_020a, 0x01fb_021a, 0x01fd_01ed, 
            0x020b_01f8, 0x020d_01fd, 0x020d_0217, 0x020b_021b, 0x01fd_022d, 0x0203_0220, 0x0208_01f7, 0x0203_0209, 
            0x020d_01fd, 0x020a_0207, 0x0212_01fd, 0x020f_0210, 0x020c_021c, 0x020c_01e6, 0x0208_01fc, 0x0205_0208, 
        },
        //Queen - White
        {
            0x0387_0400, 0x038c_03ef, 0x0392_03f8, 0x037d_040b, 0x03a3_03f2, 0x0388_03e8, 0x0394_03e2, 0x037f_03cf, 
            0x0392_03de, 0x0391_03f9, 0x038a_040c, 0x0398_0403, 0x0398_0409, 0x0391_0410, 0x0384_03fe, 0x0388_0402, 
            0x0398_03f3, 0x038d_0403, 0x03b7_03f6, 0x03ae_03ff, 0x03b1_03fc, 0x03b9_0403, 0x03b2_040f, 0x03ad_0406, 
            0x0396_03f8, 0x03c4_03e7, 0x03bb_03f8, 0x03d7_03f7, 0x03c7_03ff, 0x03ca_03fd, 0x03cf_0404, 0x03bf_03fe, 
            0x03ab_03e6, 0x03be_03e6, 0x03c0_03f1, 0x03d5_03f1, 0x03e1_0400, 0x03d0_0412, 0x03e1_03ff, 0x03cc_0402, 
            0x0394_03f4, 0x03ae_03f0, 0x03b1_0408, 0x03d9_0409, 0x03d7_041e, 0x03cb_0439, 0x03bb_0430, 0x03b1_043a, 
            0x0397_03e9, 0x03bc_03da, 0x03c8_03fc, 0x03d1_0402, 0x03e2_03f1, 0x03c1_043a, 0x03c6_041d, 0x03a8_0437, 
            0x039f_03e5, 0x03be_0401, 0x03be_041e, 0x03c3_040d, 0x03c3_043c, 0x03bb_042d, 0x03b2_042c, 0x03bc_042e, 
        },
        //King - White
        {
            0xffcb_fff1, 0xffde_0024, 0xffeb_000c, 0xfff5_ffca, 0xffe4_0008, 0xfff2_ffe4, 0xffe8_0018, 0xffd5_000e, 
            0xffe5_0001, 0xfff5_0007, 0x0004_fff8, 0x000d_ffc0, 0x000e_ffd5, 0x0004_fff0, 0xfffb_0009, 0xffef_0008, 
            0xffed_fff2, 0xfffd_fff2, 0x000b_ffea, 0x0015_ffd2, 0x0017_ffd4, 0x0010_ffe2, 0x0007_fff1, 0xfff7_ffe5, 
            0xffee_ffcf, 0xfffc_ffff, 0x0015_ffe5, 0x0018_ffd9, 0x001b_ffd2, 0x0017_ffd4, 0x0009_ffdf, 0xfff5_ffcd, 
            0xfff8_ffef, 0x0016_ffec, 0x0018_fff4, 0x001b_ffe5, 0x001a_ffe2, 0x0021_ffe7, 0x001a_fff2, 0x0003_ffdc, 
            0x000a_fff7, 0x0011_0018, 0x0017_0002, 0x000f_fff0, 0x0014_ffec, 0x002d_0006, 0x002c_0016, 0x000d_ffea, 
            0xfff4_001d, 0x0011_ffff, 0x000e_ffec, 0x0011_fff9, 0x0011_fff8, 0x0026_fffc, 0x0017_ffda, 0x000b_ffe3, 
            0xffb6_ffbf, 0xffdd_0017, 0xffee_0010, 0xffee_fff1, 0xfff5_ffc8, 0x000f_ffde, 0x0004_0002, 0xffef_000d, 
        },
        //Pawn - Black
        {
            0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 
            0xfef0_ff4c, 0xfef5_ff28, 0xff04_ff71, 0xff1c_ff4f, 0xff0f_ff6a, 0xff1e_ff30, 0xfefd_ff8c, 0xfee7_ffb9, 
            0xff44_ffb4, 0xff3e_ffa7, 0xff4d_ff94, 0xff5f_ff8f, 0xff6a_ff6d, 0xff6d_ff76, 0xff50_ff95, 0xff4e_ffc2, 
            0xff82_ffbc, 0xff8a_ffa1, 0xff95_ffa8, 0xff9d_ff99, 0xffa4_ff97, 0xff9e_ffa2, 0xff91_ff9d, 0xff91_ffc5, 
            0xff95_ffc9, 0xff99_ffb0, 0xffa5_ffb3, 0xffa9_ffa2, 0xffa9_ff9d, 0xffaa_ffa8, 0xff9f_ffa4, 0xffa3_ffc7, 
            0xff9e_ffc8, 0xff9b_ffb2, 0xffa8_ffb2, 0xffa1_ffb8, 0xffa2_ffab, 0xffa7_ffab, 0xffa3_ff8d, 0xffaa_ffba, 
            0xff95_ffd1, 0xff9a_ffaf, 0xff9a_ffc2, 0xff98_ffc5, 0xff95_ffbd, 0xffa2_ff96, 0xffa0_ff88, 0xffa9_ffc4, 
            0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 0xffa2_ffae, 
        },
        //Knight - Black
        {
            0xff21_ff56, 0xff0d_ff08, 0xfef4_fed1, 0xff03_fee0, 0xff06_fe72, 0xff02_ff10, 0xff26_febe, 0xff4a_ff1a, 
            0xff00_fef8, 0xfeef_fed8, 0xff00_fe67, 0xfee9_fe8b, 0xfef0_fe98, 0xff00_fe71, 0xfeff_fea8, 0xff1b_fec0, 
            0xfeff_fede, 0xfefb_fe73, 0xfedd_fe8a, 0xfede_fe6e, 0xfee8_fe5b, 0xfef0_fe2e, 0xfefa_fe66, 0xff10_fe83, 
            0xfef8_feb8, 0xfee4_fe9e, 0xfed1_fe9c, 0xfed1_fe7a, 0xfed1_fe8a, 0xfedc_fe6a, 0xfedf_fe9d, 0xfef9_fe99, 
            0xfef9_febc, 0xfeed_feab, 0xfed7_fe9f, 0xfece_fea2, 0xfed7_fe93, 0xfed6_fe9c, 0xfee3_fe9a, 0xfef9_feb7, 
            0xfefe_fec6, 0xfeea_feb8, 0xfee8_fea3, 0xfed8_fea5, 0xfedd_fe9c, 0xfeea_fe9e, 0xfefb_fe96, 0xfefd_febf, 
            0xff11_fecc, 0xfefb_fee4, 0xfef1_febb, 0xfeec_feb2, 0xfee9_feb0, 0xfefb_fe9d, 0xfefe_febd, 0xff13_fec2, 
            0xff04_ff18, 0xff1a_fec4, 0xfefe_fee9, 0xfef6_fed0, 0xfefd_fec0, 0xfef9_fecb, 0xff19_fec2, 0xff27_fec6, 
        },
        //Bishop - Black
        {
            0xfee5_feb0, 0xfeec_fe8f, 0xfee2_fee5, 0xfedf_feb8, 0xfede_feac, 0xfee0_febd, 0xfee8_fe8c, 0xfeef_fe9b, 
            0xfedf_fead, 0xfedb_fe83, 0xfed0_fea5, 0xfee3_fea0, 0xfeda_fe75, 0xfee4_fe58, 0xfedb_fe81, 0xfee5_fec2, 
            0xfed5_fea3, 0xfedf_fe6e, 0xfed7_fe68, 0xfed8_fe6b, 0xfed9_fe70, 0xfed1_fe61, 0xfed7_fe6e, 0xfed3_fe95, 
            0xfeda_fe97, 0xfece_fe8e, 0xfecb_fe80, 0xfece_fe61, 0xfec9_fe6e, 0xfecd_fe6e, 0xfed4_fe8c, 0xfed5_fe95, 
            0xfedd_fe99, 0xfed4_fe86, 0xfeca_fe86, 0xfec4_fe79, 0xfed0_fe71, 0xfecd_fe87, 0xfeda_fe89, 0xfee0_fe8f, 
            0xfee3_fe93, 0xfeda_fe84, 0xfecf_fe84, 0xfecd_fe84, 0xfeca_fe85, 0xfed4_fe78, 0xfede_fe81, 0xfee6_fe89, 
            0xfee5_fe8f, 0xfee9_fe84, 0xfede_fe83, 0xfed8_fe93, 0xfed3_fe8c, 0xfee0_fe7e, 0xfee6_fe72, 0xfef2_fe92, 
            0xfeee_feb4, 0xfee0_fe96, 0xfeee_fea1, 0xfedc_fea8, 0xfee0_fea0, 0xfee7_fe9f, 0xfedc_feba, 0xfee8_fea8, 
        },
        //Rook - Black
        {
            0xfdf3_fe03, 0xfdf6_fdf9, 0xfdee_fe03, 0xfdf1_fdf0, 0xfdf4_fde4, 0xfdf4_fe1a, 0xfdf8_fe04, 0xfdfb_fdf8, 
            0xfdf5_fe08, 0xfdf3_fe03, 0xfdf3_fde9, 0xfdf5_fde5, 0xfe03_fdd3, 0xfdfd_fde0, 0xfdf8_fe09, 0xfdfd_fdf7, 
            0xfdf9_fe28, 0xfdf9_fe10, 0xfdf9_fe09, 0xfdfb_fdff, 0xfdfc_fe12, 0xfe03_fdf6, 0xfe05_fde6, 0xfe03_fe13, 
            0xfdfc_fe3b, 0xfdfd_fe2e, 0xfdf3_fe1c, 0xfdff_fe09, 0xfdfe_fe0b, 0xfdff_fe00, 0xfe01_fe2b, 0xfdfe_fe37, 
            0xfdfd_fe47, 0xfdfb_fe3d, 0xfdf8_fe2f, 0xfdfc_fe24, 0xfe05_fe1a, 0xfe06_fe2a, 0xfe08_fe1d, 0xfe0b_fe3a, 
            0xfe04_fe50, 0xfe00_fe3c, 0xfe05_fe33, 0xfe01_fe34, 0xfe07_fe20, 0xfe0c_fe23, 0xfe08_fe28, 0xfe10_fe44, 
            0xfe06_fe4f, 0xfe06_fe33, 0xfe00_fe37, 0xfdfe_fe2c, 0xfe09_fe24, 0xfe09_fe18, 0xfe0b_fe29, 0xfe03_fe6a, 
            0xfe09_fe36, 0xfdfe_fe30, 0xfdfd_fe22, 0xfe01_fe12, 0xfe05_fe13, 0xfe0d_fe1c, 0xfdfc_fe48, 0xfe14_fe3d, 
        },
        //Queen - Black
        {
            0xfc61_fc1b, 0xfc42_fbff, 0xfc42_fbe2, 0xfc3d_fbf3, 0xfc3d_fbc4, 0xfc45_fbd3, 0xfc4e_fbd4, 0xfc44_fbd2, 
            0xfc69_fc17, 0xfc44_fc26, 0xfc38_fc04, 0xfc2f_fbfe, 0xfc1e_fc0f, 0xfc3f_fbc6, 0xfc3a_fbe3, 0xfc58_fbc9, 
            0xfc6c_fc0c, 0xfc52_fc10, 0xfc4f_fbf8, 0xfc27_fbf7, 0xfc29_fbe2, 0xfc35_fbc7, 0xfc45_fbd0, 0xfc4f_fbc6, 
            0xfc55_fc1a, 0xfc42_fc1a, 0xfc40_fc0f, 0xfc2b_fc0f, 0xfc1f_fc00, 0xfc30_fbee, 0xfc1f_fc01, 0xfc34_fbfe, 
            0xfc6a_fc08, 0xfc3c_fc19, 0xfc45_fc08, 0xfc29_fc09, 0xfc39_fc01, 0xfc36_fc03, 0xfc31_fbfc, 0xfc41_fc02, 
            0xfc68_fc0d, 0xfc73_fbfd, 0xfc49_fc0a, 0xfc52_fc01, 0xfc4f_fc04, 0xfc47_fbfd, 0xfc4e_fbf1, 0xfc53_fbfa, 
            0xfc6e_fc22, 0xfc6f_fc07, 0xfc76_fbf4, 0xfc68_fbfd, 0xfc68_fbf7, 0xfc6f_fbf0, 0xfc7c_fc02, 0xfc78_fbfe, 
            0xfc79_fc00, 0xfc74_fc11, 0xfc6e_fc08, 0xfc83_fbf5, 0xfc5d_fc0e, 0xfc78_fc18, 0xfc6c_fc1e, 0xfc81_fc31, 
        },
        //King - Black
        {
            0x004a_0041, 0x0023_ffe9, 0x0012_fff0, 0x0012_000f, 0x000b_0038, 0xfff1_0022, 0xfffc_fffe, 0x0011_fff3, 
            0x000c_ffe3, 0xffef_0001, 0xfff2_0014, 0xffef_0007, 0xffef_0008, 0xffda_0004, 0xffe9_0026, 0xfff5_001d, 
            0xfff6_0009, 0xffef_ffe8, 0xffe9_fffe, 0xfff1_0010, 0xffec_0014, 0xffd3_fffa, 0xffd4_ffea, 0xfff3_0016, 
            0x0008_0011, 0xffea_0014, 0xffe8_000c, 0xffe5_001b, 0xffe6_001e, 0xffdf_0019, 0xffe6_000e, 0xfffd_0024, 
            0x0012_0031, 0x0004_0001, 0xffeb_001b, 0xffe8_0027, 0xffe5_002e, 0xffe9_002c, 0xfff7_0021, 0x000b_0033, 
            0x0013_000e, 0x0003_000e, 0xfff5_0016, 0xffeb_002e, 0xffe9_002c, 0xfff0_001e, 0xfff9_000f, 0x0009_001b, 
            0x001b_ffff, 0x000b_fff9, 0xfffc_0008, 0xfff3_0040, 0xfff2_002b, 0xfffc_0010, 0x0005_fff7, 0x0011_fff8, 
            0x0035_000f, 0x0022_ffdc, 0x0015_fff4, 0x000b_0036, 0x001c_fff8, 0x000e_001c, 0x0018_ffe8, 0x002b_fff2, 
        },
    };

    static Eval() {
        for(int p = 0; p < 12; p++) {
            for(int sq = 0; sq < 64; sq++) {
                ulong val = ComprPeSTOTables[p,sq];
                PeSTOTables[p,sq] = (val & 0xffff_0000) << (32 - 16) | (val & 0x0000_ffff);
            }
        }
    }
}