using System;

namespace HugeBot;

//Evaluation function heavily based on ice4 (https://github.com/MinusKelvin/ice4)
//All credits belong to Mark Carlson (MinusKelvin) for their amazing engine!

public static class PSTTable {
    public const int WhitePiece = 0, BlackPiece = 8;
    public static readonly ulong[][] PieceSquareTable;

    //NOTE: The bleed between MG -> EG phases is intentional (I think)

    private static void UnpackPSTFull(int piece, ulong phase, double scale, short off) {
        unchecked {
            ulong[] whiteTable = PieceSquareTable[piece | WhitePiece], blackTable = PieceSquareTable[piece | BlackPiece];
            for(int rank = 1; rank < 7; rank++) {
                for(int file = 0; file < 8; file++) {
                    long val = checked((short) (CompressedPSTData[CompressedPSTDataOff++] * scale + off));
                    whiteTable[rank << 3        | file] += phase * (ulong) val;
                    blackTable[(7 - rank) << 3  | file] += phase * (ulong) val;
                }
            }
        }
    }

    private static void UnpackPSTHalf(int piece, ulong phase, double scale, short offQuadLL, short offQuadLR, short offQuadRL, short offQuadRR) {
        unchecked {
            ulong[] whiteTable = PieceSquareTable[piece | WhitePiece], blackTable = PieceSquareTable[piece | BlackPiece];
            for(int rank = 0; rank < 4; rank++) {
                for(int file = 0; file < 4; file++) {
                    short val = checked((short) (CompressedPSTData[CompressedPSTDataOff++] * scale));

                    whiteTable[rank << 3        | file]         += phase * (ulong) (long) checked((short) (offQuadLL + val));
                    whiteTable[rank << 3        | (7 - file)]   += phase * (ulong) (long) checked((short) (offQuadRL + val));
                    whiteTable[(7 - rank) << 3  | file]         += phase * (ulong) (long) checked((short) (offQuadLR + val));
                    whiteTable[(7 - rank) << 3  | (7 - file)]   += phase * (ulong) (long) checked((short) (offQuadRR + val));

                    blackTable[(7 - rank) << 3  | file]         += phase * (ulong) -(long) checked((short) (offQuadLL + val));
                    blackTable[(7 - rank) << 3  | (7 - file)]   += phase * (ulong) -(long) checked((short) (offQuadRL + val));
                    blackTable[rank << 3        | file]         += phase * (ulong) -(long) checked((short) (offQuadLR + val));
                    blackTable[rank << 3        | (7 - file)]   += phase * (ulong) -(long) checked((short) (offQuadRR + val));
                }
            }
        }
    }

    private static void UnpackPSTSmol(int piece, ulong phase, double scale, short off) {
        unchecked {
            ulong[] whiteTable = PieceSquareTable[piece | WhitePiece], blackTable = PieceSquareTable[piece | BlackPiece];
            for(int rank = 0; rank < 8; rank += 2) {
                for(int file = 0; file < 8; file += 2) {
                    long val = checked((short) (CompressedPSTData[CompressedPSTDataOff++] * scale + off));

                    whiteTable[rank << 3        | file]         += phase * (ulong) val;
                    whiteTable[rank << 3        | (file + 1)]   += phase * (ulong) val;
                    whiteTable[(rank+1) << 3    | file]         += phase * (ulong) val;
                    whiteTable[(rank+1) << 3    | (file + 1)]   += phase * (ulong) val;

                    blackTable[(7 - rank) << 3  | file]         += phase * (ulong) -val;
                    blackTable[(7 - rank) << 3  | (file + 1)]   += phase * (ulong) -val;
                    blackTable[(6 - rank) << 3  | file]         += phase * (ulong) -val;
                    blackTable[(6 - rank) << 3  | (file + 1)]   += phase * (ulong) -val;
                }
            }
        }
    }

    static PSTTable() {
        //Unpack the piece square tables
        PieceSquareTable = new ulong[16][];
        for(int i = 0; i < 16; i++) PieceSquareTable[i] = new ulong[8*8];

        /* Pawns        - MG */ UnpackPSTFull(00, 0x0_0000_0001, 1.088, 10);
        /* Pawns        - EG */ UnpackPSTFull(00, 0x1_0000_0000, 1.337, 93);
        /* Passed Pawns - MG */ UnpackPSTFull(01, 0x0_0000_0001, 1.251, -14);
        /* Passed Pawns - EG */ UnpackPSTFull(01, 0x1_0000_0000, 1.979, -5);
        /* Knights      - MG */ UnpackPSTHalf(02, 0x0_0000_0001, 1.0, 222, 241, 225, 246);
        /* Knights      - EG */ UnpackPSTHalf(02, 0x1_0000_0000, 1.241, 265, 275, 265, 276);
        /* Bishops      - MG */ UnpackPSTHalf(03, 0x0_0000_0001, 1.0, 237, 243, 236, 250);
        /* Bishops      - EG */ UnpackPSTHalf(03, 0x1_0000_0000, 1.0, 368, 378, 369, 377);
        /* Rooks        - MG */ UnpackPSTHalf(04, 0x0_0000_0001, 1.0, 280, 313, 286, 324);
        /* Rooks        - EG */ UnpackPSTHalf(04, 0x1_0000_0000, 1.0, 629, 653, 622, 646);
        /* Queens       - MG */ UnpackPSTHalf(05, 0x0_0000_0001, 1.0, 640, 640, 643, 656);
        /* Queens       - EG */ UnpackPSTHalf(05, 0x1_0000_0000, 1.0, 1274, 1329, 1268, 1333);
        /* Kings        - MG */ UnpackPSTSmol(06, 0x0_0000_0001, 1.0, -38);
        /* Kings        - EG */ UnpackPSTSmol(06, 0x1_0000_0000, 1.0, -39);

#if DEBUG
        if(CompressedPSTDataOff != CompressedPSTData.Length) throw new Exception("Mismatching PST unpacked consumed data length");
#endif

        //Mask of extra PST bits
        for(int i = 0; i < 16; i++) {
            for(int j = 0; j < 64; j++) PieceSquareTable[i][j] &= 0x0000_ffff_0000_ffffUL;
        }
    }

    private static int CompressedPSTDataOff = 0;
    private static readonly byte[] CompressedPSTData = new byte[] {
        /* Pawns        - MG */ 0x25, 0x39, 0x3c, 0x20, 0x1c, 0x21, 0x19, 0x13, 0x28, 0x36, 0x2e, 0x26, 0x23, 0x24, 0x19, 0x18, 0x28, 0x2f, 0x3b, 0x38, 0x34, 0x2d, 0x1d, 0x1b, 0x2f, 0x38, 0x47, 0x43, 0x40, 0x36, 0x2b, 0x25, 0x3b, 0x43, 0x5b, 0x56, 0x51, 0x47, 0x36, 0x2b, 0x20, 0x00, 0x49, 0x5e, 0x56, 0x4c, 0x17, 0x48,
        /* Pawns        - EG */ 0x05, 0x08, 0x10, 0x16, 0x14, 0x12, 0x0c, 0x0e, 0x01, 0x00, 0x0c, 0x0c, 0x0c, 0x08, 0x07, 0x05, 0x05, 0x09, 0x07, 0x06, 0x05, 0x08, 0x0c, 0x08, 0x0d, 0x0f, 0x0e, 0x0b, 0x0d, 0x10, 0x12, 0x11, 0x16, 0x23, 0x19, 0x1f, 0x1f, 0x1d, 0x1e, 0x1a, 0x41, 0x28, 0x4e, 0x5e, 0x59, 0x55, 0x52, 0x5a,
        /* Passed Pawns - MG */ 0x0e, 0x19, 0x13, 0x12, 0x0e, 0x0b, 0x0a, 0x10, 0x0c, 0x0d, 0x05, 0x06, 0x00, 0x00, 0x0b, 0x0f, 0x0e, 0x0d, 0x03, 0x01, 0x04, 0x06, 0x14, 0x16, 0x0d, 0x16, 0x0f, 0x10, 0x12, 0x15, 0x23, 0x1d, 0x13, 0x0a, 0x16, 0x13, 0x1c, 0x24, 0x2b, 0x31, 0x33, 0x2e, 0x21, 0x27, 0x37, 0x3c, 0x5e, 0x36,
        /* Passed Pawns - EG */ 0x03, 0x04, 0x00, 0x00, 0x03, 0x03, 0x0b, 0x02, 0x07, 0x0d, 0x07, 0x05, 0x04, 0x06, 0x0a, 0x07, 0x16, 0x19, 0x14, 0x0f, 0x0b, 0x0c, 0x11, 0x13, 0x25, 0x26, 0x1e, 0x16, 0x10, 0x13, 0x18, 0x1f, 0x38, 0x35, 0x2f, 0x1b, 0x16, 0x1c, 0x27, 0x30, 0x45, 0x5e, 0x3a, 0x15, 0x0f, 0x15, 0x24, 0x22,
        /* Knights      - MG */ 0x00, 0x03, 0x09, 0x0e, 0x09, 0x0d, 0x19, 0x14, 0x0b, 0x1f, 0x1d, 0x25, 0x1b, 0x18, 0x28, 0x27,
        /* Knights      - EG */ 0x00, 0x22, 0x35, 0x38, 0x2c, 0x3c, 0x42, 0x49, 0x36, 0x49, 0x52, 0x58, 0x3f, 0x51, 0x5a, 0x5e,
        /* Bishops      - MG */ 0x03, 0x00, 0x00, 0x01, 0x0d, 0x17, 0x13, 0x0e, 0x0e, 0x1c, 0x18, 0x16, 0x11, 0x11, 0x1a, 0x24,
        /* Bishops      - EG */ 0x00, 0x10, 0x06, 0x0d, 0x0b, 0x13, 0x18, 0x17, 0x12, 0x1d, 0x25, 0x27, 0x0c, 0x21, 0x27, 0x2a,
        /* Rooks        - MG */ 0x11, 0x13, 0x19, 0x1f, 0x00, 0x09, 0x12, 0x11, 0x0a, 0x15, 0x11, 0x12, 0x08, 0x07, 0x10, 0x11,
        /* Rooks        - EG */ 0x00, 0x06, 0x06, 0x02, 0x08, 0x09, 0x06, 0x08, 0x04, 0x05, 0x08, 0x07, 0x07, 0x0e, 0x0c, 0x09,
        /* Queens       - MG */ 0x00, 0x07, 0x09, 0x0c, 0x0e, 0x12, 0x16, 0x12, 0x14, 0x15, 0x14, 0x12, 0x13, 0x0d, 0x12, 0x10,
        /* Queens       - EG */ 0x19, 0x0e, 0x05, 0x00, 0x0d, 0x0f, 0x0c, 0x14, 0x0c, 0x23, 0x2e, 0x2b, 0x12, 0x2d, 0x37, 0x43,
        /* Kings        - MG */ 0x2f, 0x1e, 0x20, 0x3f, 0x16, 0x00, 0x02, 0x14, 0x2b, 0x30, 0x27, 0x17, 0x57, 0x56, 0x3b, 0x23,
        /* Kings        - EG */ 0x06, 0x12, 0x0e, 0x00, 0x15, 0x2c, 0x29, 0x12, 0x36, 0x42, 0x43, 0x34, 0x36, 0x48, 0x47, 0x3b,
    };
}