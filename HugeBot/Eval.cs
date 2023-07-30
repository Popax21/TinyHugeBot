using ChessChallenge.API;
using System.Linq;
using System.Numerics;
using BitBoard = System.UInt64;

namespace HugeBot;

// ported from STRO4K (https://github.com/ONE-RANDOM-HUMAN/STRO4K/tree/master)
struct Eval
{
    public const int MaxEval = 128 * 256 - 1;
    public const int MinEval = -MaxEval;

    public short top;
    public short bottom;

    public Eval(short white, short black)
    {
        this.top = white;
        this.bottom = black;
    }

    public void Accumulate(Eval eval, int count)
    {
        this = new Eval((short)(this.top + eval.top * count), (short)(this.bottom + eval.bottom * count));
    }

    public void Accumulate(uint eval, int count)
    {
        this = new Eval((short)(this.top + (short)(eval >> 16) * count), (short)(this.bottom + (short)(eval & 0xFFFF) * count));
    }
}

class Evaluator
{
    static readonly uint[] MobilityEval =
    {
        2424853,
        1572874,
        1114113,
        786436,
    };

    static readonly uint[] MaterialEval =
    {
        20840759,
        43647639,
        50463443,
        75105593,
        160368982,
    };

    const uint BishopPairEval = 7340193;

    static readonly uint[,] Pst = {
        {
            4291493890,
            4290117651,
            6,
            4294574040,
            4291428338,
            4293787617,
            4294836198,
            4294442952,
            4294901794,
            2883575,
            4980710,
            2555889,
            2687051,
            2621496,
            1048609,
            327709,
        },
        {
            4293459949,
            4292542431,
            4293459935,
            4293591033,
            4293066748,
            4292214771,
            4291887084,
            524297,
            2555917,
            3211273,
            1835031,
            4587549,
            131076,
            1507342,
            1310732,
            327681,
        },
        {
            1507312,
            4292607976,
            4293132272,
            1638382,
            393220,
            65538,
            4293459972,
            786426,
            262155,
            1900550,
            1769487,
            1441808,
            4294508547,
            7,
            131075,
            4294639611,
        },
        {
            4291100637,
            4294770650,
            4293722065,
            4291690453,
            4291821556,
            4293525501,
            4293591031,
            4294705133,
            851991,
            1835039,
            1900564,
            1900554,
            1966118,
            3014700,
            2228257,
            2162719,
        },
        {
            4293853162,
            4294770641,
            4294049746,
            4293787627,
            4293001209,
            4292739075,
            4294049791,
            1114111,
            4293918715,
            4294770713,
            2031653,
            3932184,
            4293918718,
            851991,
            2031647,
            2228236,
        },
        {
            2818018,
            4293722074,
            4290117594,
            3211196,
            393210,
            4294705158,
            4293984253,
            4293918699,
            720918,
            786472,
            589866,
            393239,
            393223,
            589846,
            720924,
            327694,
        },
    };

    static readonly uint[] DoubledPawnEval = {
        4291166155,
        4292804567,
        4291035106,
        4292542448,
        4291035111,
        4291690449,
        4293787612,
        4292345808,
    };

    static readonly uint[] IsolatedPawnEval = {
        4292870124,
        4293591018,
        4291231715,
        4290838487,
        4289331155,
        4292345817,
        4293263339,
        4289265633,
    };

    static readonly uint[] PassedPawnEval = {
        0,
        0,
        41,
        1900602,
        6684798,
        6684865,
    };

    const uint OpenFileEval = 4784128;
    const uint SemiOpenFileEval = 2490369;

    const BitBoard LightSquares = 0xAA55AA55AA55AA55;
    const BitBoard DarkSquares = ~LightSquares;
    const BitBoard AFile = 0x0101010101010101;
    const BitBoard ABFile = 0x0303030303030303;
    const BitBoard HFile = 0x8080808080808080;
    const BitBoard All = ~0ul;

    public static BitBoard Dumb7Fill(BitBoard gen, BitBoard leftMask, BitBoard occ, byte shift)
    {
        BitBoard leftGen = gen, rightGen = gen;
        for (int i = 0; i < 6; i++)
        {
            leftGen |= (leftGen << shift) & leftMask & ~occ;
            rightGen |= ((rightGen & leftMask) >> shift) & ~occ;
        }

        return ((leftGen << shift) & leftMask) | ((rightGen & leftMask) >> shift);
    }

    public static int LeadingZeros(BitBoard board)
    {
        int ret = 0;
        for (BitBoard i = 1; i > 0; i >>= 1)
        {
            ret++;
        }
        return 64 - ret;
    }

    public static BitBoard SwapBytes(BitBoard board)
    {
        return (board << 56)
            | (board << 48 & 0xff000000000000)
            | (board << 40 & 0xff0000000000)
            | (board << 32 & 0xff00000000)
            | (board << 24 & 0xff000000)
            | (board << 16 & 0xff0000)
            | (board << 8 & 0xff00)
            | (board & 0xff);
    }

    public static Eval SidePst(BitBoard[] pieces, byte rowMask)
    {
        Eval eval = new Eval(0, 0);
        for (int i = 0; i < 6; i++)
        {
            BitBoard pieceBoard = pieces[i];
            int pieceIndex = 0;
            while (pieceIndex < 64)
            {
                if ((pieceBoard & (1ul << pieceIndex)) == 1)
                {
                    int index = ((pieceIndex / 2) & 0b11) | ((pieceIndex / 4) & 0b1100);
                    eval.Accumulate(Pst[i, index ^ rowMask], 1);
                }
                pieceIndex++;
            }
        }
        return eval;
    }

    public static Eval SideMobility(BitBoard[] pieces, BitBoard occ, BitBoard mask)
    {
        Eval eval = new Eval(0, 0);
        for (int i = 0; i < 4; i++)
        {
            BitBoard pieceBoard = pieces[i + 1];
            while (pieceBoard != 0)
            {
                BitBoard piece = pieceBoard & (pieceBoard << 1 >> 1);
                BitBoard movement;
                switch (i)
                {
                    case 0:
                        ulong out1 = ((piece << 1) & ~AFile) | ((piece & ~AFile) >> 1);
                        ulong out2 = ((piece << 2) & ~ABFile) | ((piece & ~ABFile) >> 2);
                        movement = (out1 << 16) | (out1 >> 16) | (out2 << 8) | (out2 >> 8);
                        break;
                    case 1:
                        movement = Dumb7Fill(piece, ~AFile, occ, 9) | Dumb7Fill(piece, ~HFile, occ, 7);
                        break;
                    case 2:
                        movement = Dumb7Fill(piece, ~AFile, occ, 1) | Dumb7Fill(piece, All, occ, 8);
                        break;
                    default:
                        movement = (Dumb7Fill(piece, ~AFile, occ, 9) | Dumb7Fill(piece, ~HFile, occ, 7))
                            | (Dumb7Fill(piece, ~AFile, occ, 1) | Dumb7Fill(piece, All, occ, 8));
                        break;
                }
                movement &= mask;
                eval.Accumulate(MobilityEval[i], BitboardHelper.GetNumberOfSetBits(movement));
                pieceBoard &= pieceBoard - 1;
            }
        }
        return eval;
    }

    public static Eval SidePawnStructure(BitBoard pawns)
    {
        Eval eval = new Eval(0, 0);
        BitBoard file = AFile;
        for (int i = 0; i < 8; i++)
        {
            short pawnCount = (short)BitboardHelper.GetNumberOfSetBits(pawns & file);
            BitBoard adjacent = ((file << 1) & ~AFile) | ((file & ~AFile) >> 1);
            if ((pawns & adjacent) == 0)
            {
                eval.Accumulate(IsolatedPawnEval[i], pawnCount);
            }
            eval.Accumulate(DoubledPawnEval[i], (short)(System.Math.Max(pawnCount, (short)1) - 1));
            file <<= 1;
        }
        return eval;
    }

    public static Eval WhitePassedPawn(BitBoard side, BitBoard enemy)
    {
        Eval eval = new Eval(0, 0);
        enemy |= enemy >> 8;
        enemy |= enemy >> 16;
        enemy |= enemy >> 32;
        enemy |= ((enemy >> 7) & ~AFile) | ((enemy & ~AFile) >> 9);
        BitBoard pawns = side & ~enemy;
        BitBoard file = AFile;
        for (int i = 0; i < 8; i++)
        {
            int index = LeadingZeros(pawns & file);
            if (index != 64)
            {
                eval.Accumulate(PassedPawnEval[6 - index / 8], 1);
            }
            file <<= 1;
        }
        return eval;
    }
    public static Eval SideOpenFile(BitBoard rook, BitBoard sidePawns, BitBoard enemyPawns)
    {
        Eval eval = new Eval(0, 0);
        BitBoard file = AFile;
        for (int i = 0; i < 8; i++)
        {
            if (((sidePawns | enemyPawns) & file) == 0)
            {
                eval.Accumulate(OpenFileEval, BitboardHelper.GetNumberOfSetBits(rook & file));
            }
            else if ((sidePawns & file) == 0)
            {
                eval.Accumulate(SemiOpenFileEval, BitboardHelper.GetNumberOfSetBits(rook & file));
            }
            file <<= 1;
        }
        return eval;
    }

    public static int Evaluate(Board board)
    {
        Eval eval = new Eval(0, 0);
        BitBoard[] whitePieces = (BitBoard[])(from i in Enumerable.Range(0, 6) select board.GetPieceBitboard((PieceType)i, true));
        BitBoard[] blackPieces = (BitBoard[])(from i in Enumerable.Range(0, 6) select board.GetPieceBitboard((PieceType)i, false));
        for (int i = 0; i < 5; i++)
        {
            short count = (short)(BitboardHelper.GetNumberOfSetBits(whitePieces[i]) - BitboardHelper.GetNumberOfSetBits(blackPieces[i]));
            eval.Accumulate(MaterialEval[i], count);
        }
        if ((whitePieces[2] & DarkSquares) != 0)
        {
            eval.Accumulate(BishopPairEval, 1);
        }
        if ((blackPieces[2] & DarkSquares) != 0)
        {
            eval.Accumulate(BishopPairEval, -1);
        }
        eval.Accumulate(SidePst(whitePieces, 0), 1);
        eval.Accumulate(SidePst(blackPieces, 0b1100), -1);
        eval.Accumulate(SideMobility(whitePieces, board.AllPiecesBitboard, All), 1);
        eval.Accumulate(SideMobility(blackPieces, board.AllPiecesBitboard, All), -1);
        eval.Accumulate(SidePawnStructure(whitePieces[0]), 1);
        eval.Accumulate(SidePawnStructure(blackPieces[0]), -1);
        eval.Accumulate(WhitePassedPawn(whitePieces[0], blackPieces[0]), 1);
        eval.Accumulate(WhitePassedPawn(SwapBytes(blackPieces[0]), SwapBytes(whitePieces[0])), -1);
        eval.Accumulate(SideOpenFile(whitePieces[3], whitePieces[0], blackPieces[0]), 1);
        eval.Accumulate(SideOpenFile(blackPieces[3], blackPieces[0], whitePieces[0]), -1);
        int phase = 0;
        int[] weights = { 1, 1, 2, 4 };
        PieceList[] pieces = board.GetAllPieceLists();
        for (int i = 0; i < 4; i++)
        {
            phase += weights[i] * (pieces[i].Count + pieces[i + 6].Count);
        }
        int score = (eval.top * phase + eval.bottom * (24 - phase)) / phase;
        return board.IsWhiteToMove ? score : -score;
    }
}