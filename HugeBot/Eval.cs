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
    private static ulong[] PeSTOTables = new ulong[12*64];

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
            eval -= PeSTOTables[(movedPiece + sideOffset) << 6 | move.StartSquare.Index];
            phase -= PhaseContributions[movedPiece];

            //Update the new square
            int targetSquare = move.TargetSquare.Index;
            int newPieceType = move.IsPromotion ? (int) move.PromotionPieceType - 1 : movedPiece;
            eval += PeSTOTables[(newPieceType + sideOffset) << 6 | targetSquare];
            phase += PhaseContributions[newPieceType];

            //Update captures (if any)
            if(move.IsCapture) {
                int capturedPiece = (int) move.CapturePieceType - 1;
                int captureSquare = targetSquare;
                if(move.IsEnPassant) captureSquare += isWhiteToMove ? -8 : +8; //En Passant captures a piece on a different square :)
                eval -= PeSTOTables[(capturedPiece + (sideOffset ^ 6)) << 6 | captureSquare];
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

                eval -= PeSTOTables[((int) PieceType.Rook - 1 + sideOffset) << 6 | oldRookSquare];
                eval += PeSTOTables[((int) PieceType.Rook - 1 + sideOffset) << 6 | newRookSquare];
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

    public static EvalState Evaluate_I(Board board) {
        ulong eval = 0x8000_0000_8000_0000;

        //Evaluate PST and determine phase
        int phase = 0;
        for(int s = 0; s <= 6; s += 6) {
            for(int p = 0; p < 6; p++) {
                ulong bitboard = board.GetPieceBitboard((PieceType) p + 1, s == 0);
                while(bitboard != 0) {
                    eval += PeSTOTables[(p+s) << 6 | BitOperations.TrailingZeroCount(bitboard)];
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

    private static ushort[] ComprPeSTOTables = {
        //Pawn
        0xffdd, 0xfff8, 
        0x0823, 0x0823, 0x0823, 0x0823, 0x0823, 0x0823, 0x0823, 0x0823, 
        0x1500, 0x1022, 0x100f, 0x120c, 0x1514, 0x083b, 0x0a49, 0x010d, 
        0x0c09, 0x0f1f, 0x021f, 0x0919, 0x0826, 0x0326, 0x0744, 0x0017, 
        0x1508, 0x1121, 0x051e, 0x012f, 0x0134, 0x0029, 0x0b2d, 0x070a, 
        0x2815, 0x2030, 0x1529, 0x0d38, 0x063a, 0x0c2f, 0x1934, 0x190c, 
        0x661d, 0x6c2a, 0x5d3d, 0x4b42, 0x4064, 0x3d5b, 0x5a3c, 0x5c0f, 
        0xba85, 0xb5a9, 0xa660, 0x8e82, 0x9b67, 0x8ca1, 0xad45, 0xc318, 
        0x0823, 0x0823, 0x0823, 0x0823, 0x0823, 0x0823, 0x0823, 0x0823, 

        //Knight
        0xff59, 0xff9d, 
        0x463e, 0x3092, 0x4c6d, 0x5486, 0x4d96, 0x518b, 0x3194, 0x2390, 
        0x398a, 0x4f72, 0x599b, 0x5ea4, 0x61a6, 0x4fb9, 0x4c99, 0x3794, 
        0x4c90, 0x609e, 0x62b3, 0x72b1, 0x6dba, 0x60b8, 0x4fc0, 0x4d97, 
        0x519a, 0x5dab, 0x73b7, 0x7cb4, 0x73c3, 0x74ba, 0x67bc, 0x519f, 
        0x529e, 0x66b8, 0x79ba, 0x79dc, 0x79cc, 0x6eec, 0x6bb9, 0x51bd, 
        0x4b78, 0x4fe3, 0x6dcc, 0x6ce8, 0x62fb, 0x5a28, 0x50f0, 0x3ad3, 
        0x4a5e, 0x5b7e, 0x4aef, 0x61cb, 0x5abe, 0x4ae5, 0x4bae, 0x2f96, 
        0x2900, 0x3d4e, 0x5685, 0x4776, 0x44e4, 0x4846, 0x2498, 0x003c, 

        //Bishop
        0xffae, 0xffe5, 
        0x0431, 0x124f, 0x0444, 0x163d, 0x1245, 0x0b46, 0x162b, 0x0a3d, 
        0x0d56, 0x0961, 0x1462, 0x1a52, 0x1f59, 0x1267, 0x0c73, 0x0053, 
        0x0f52, 0x1861, 0x2361, 0x2561, 0x2860, 0x1e6d, 0x1464, 0x0c5c, 
        0x154c, 0x1e5f, 0x285f, 0x2e6c, 0x2274, 0x255e, 0x185c, 0x1256, 
        0x184e, 0x2457, 0x2765, 0x2484, 0x2977, 0x2577, 0x1e59, 0x1d50, 
        0x1d42, 0x1377, 0x1b7d, 0x1a7a, 0x1975, 0x2184, 0x1b77, 0x1f50, 
        0x1338, 0x1762, 0x2240, 0x0f45, 0x1870, 0x0e8d, 0x1764, 0x0d23, 
        0x0d35, 0x0656, 0x1000, 0x132d, 0x1439, 0x1228, 0x0a59, 0x034a, 

        //Rook
        0xffb9, 0xffec, 
        0x0b34, 0x163a, 0x1748, 0x1358, 0x0f57, 0x074e, 0x1822, 0x002d, 
        0x0e1b, 0x0e37, 0x1433, 0x163e, 0x0b46, 0x0b52, 0x0941, 0x1100, 
        0x101a, 0x142e, 0x0f37, 0x1336, 0x0d4a, 0x0847, 0x0c42, 0x0426, 
        0x1723, 0x192d, 0x1c3b, 0x1846, 0x0f50, 0x0e40, 0x0c4d, 0x0930, 
        0x182f, 0x173c, 0x214e, 0x1561, 0x165f, 0x156a, 0x133f, 0x1633, 
        0x1b42, 0x1b5a, 0x1b61, 0x196b, 0x1858, 0x1174, 0x0f84, 0x1157, 
        0x1f62, 0x2167, 0x2181, 0x1f85, 0x1197, 0x178a, 0x1c61, 0x1773, 
        0x2167, 0x1e71, 0x2667, 0x237a, 0x2086, 0x2050, 0x1c66, 0x1972, 

        //Queen
        0xffce, 0xffd5, 
        0x0a31, 0x0f20, 0x1529, 0x003c, 0x2623, 0x0b19, 0x1713, 0x0200, 
        0x150f, 0x142a, 0x0d3d, 0x1b34, 0x1b3a, 0x1441, 0x072f, 0x0b33, 
        0x1b24, 0x1034, 0x3a27, 0x3130, 0x342d, 0x3c34, 0x3540, 0x3037, 
        0x1929, 0x4718, 0x3e29, 0x5a28, 0x4a30, 0x4d2e, 0x5235, 0x422f, 
        0x2e17, 0x4117, 0x4322, 0x5822, 0x6431, 0x5343, 0x6430, 0x4f33, 
        0x1725, 0x3121, 0x3439, 0x5c3a, 0x5a4f, 0x4e6a, 0x3e61, 0x346b, 
        0x1a1a, 0x3f0b, 0x4b2d, 0x5433, 0x6522, 0x446b, 0x494e, 0x2b68, 
        0x2216, 0x4132, 0x414f, 0x463e, 0x466d, 0x3e5e, 0x355d, 0x3f5f, 

        //King
        0xffbf, 0xffb6, 
        0x1532, 0x2865, 0x354d, 0x3f0b, 0x2e49, 0x3c25, 0x3259, 0x1f4f, 
        0x2f42, 0x3f48, 0x4e39, 0x5701, 0x5816, 0x4e31, 0x454a, 0x3949, 
        0x3733, 0x4733, 0x552b, 0x5f13, 0x6115, 0x5a23, 0x5132, 0x4126, 
        0x3810, 0x4640, 0x5f26, 0x621a, 0x6513, 0x6115, 0x5320, 0x3f0e, 
        0x4230, 0x602d, 0x6235, 0x6526, 0x6423, 0x6b28, 0x6433, 0x4d1d, 
        0x5438, 0x5b59, 0x6143, 0x5931, 0x5e2d, 0x7747, 0x7657, 0x572b, 
        0x3e5e, 0x5b40, 0x582d, 0x5b3a, 0x5b39, 0x703d, 0x611b, 0x5524, 
        0x0000, 0x2758, 0x3851, 0x3832, 0x3f09, 0x591f, 0x4e43, 0x394e, 

        //Overrides
        0x046d, //Knight f6
    };
    private static short[] PeSTOMGPieceValues = { 82, 337, 365, 477, 1025, 0};
    private static short[] PeSTOEGPieceValues = { 94, 281, 297, 512,  936, 0};

    static Eval() {
        //Decompress the neutral PST
        uint[] neutralPst = new uint[6*64];
        for(int p = 0; p < 6; p++) {
            short mgBaseVal = unchecked((short) ComprPeSTOTables[p*66 + 0]);
            short egBaseVal = unchecked((short) ComprPeSTOTables[p*66 + 1]);
            for(int sq = 0; sq < 64; sq++) {
                ushort comprVal = ComprPeSTOTables[p*66 + 2 + sq];
                short mgVal = (short) (mgBaseVal + (byte) (comprVal & 0xff));
                short egVal = (short) (egBaseVal + (byte) (comprVal >> 8));
                neutralPst[p << 6 | sq] = unchecked((uint) (ushort) egVal) << 16 | unchecked((ushort) mgVal);
            }
        }

        //Apply PST overrides in case 8 bits weren't enough to encode the base deltas
        for(int i = 6*66; i < ComprPeSTOTables.Length; i++) {
            uint ovrVal = ComprPeSTOTables[i];
            ref uint pstVal = ref neutralPst[ovrVal & 0x1ff];

            short mgVal = unchecked((short) (pstVal & 0xffff));
            short egVal = unchecked((short) (pstVal >> 16));
            mgVal += (short) (((ovrVal >> 10) & 7) << 8);
            egVal += (short) (((ovrVal >> 13) & 7) << 8);
            pstVal = unchecked((uint) (ushort) egVal) << 16 | unchecked((ushort) mgVal);
        }

        //Construct the full PST
        for(int p = 0; p < 6; p++) {
            for(int sq = 0; sq < 64; sq++) {
                short mgVal = (short) (PeSTOMGPieceValues[p] + unchecked((short) (neutralPst[p << 6 | sq] & 0xffff)));
                short egVal = (short) (PeSTOEGPieceValues[p] + unchecked((short) (neutralPst[p << 6 | sq] >> 16)));
                PeSTOTables[(p + 0) << 6 | sq] = unchecked((ulong) (ushort) egVal) << 32 | unchecked((ushort) mgVal);
                PeSTOTables[(p + 6) << 6 | (sq ^ 0b111000)] = unchecked((ulong) (ushort) (short) -egVal) << 32 | unchecked((ushort) (short) -mgVal);
            }
        }
    }
}