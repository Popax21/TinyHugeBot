using ChessChallenge.API;
using System.ComponentModel;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using BitBoard = System.UInt64;

namespace HugeBot;

// ported from STRO4K
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

    public Eval AccumulateAndRet(Eval eval, short count)
    {
        return new Eval((short)(this.top + eval.top * count), (short)(this.bottom + eval.bottom * count));
    }

    public void Accumulate(Eval eval, short count)
    {
        this = this.AccumulateAndRet(eval, count);
    }
}

class Evaluator
{
    static readonly Eval[] MobilityEval =
    {
        new Eval(37, 21),
        new Eval(24, 10),
        new Eval(17, 1),
        new Eval(12, 4),
    };

    static readonly Eval[] MaterialEval =
    {
        new Eval(318, 311),
        new Eval(814, 747).AccumulateAndRet(MobilityEval[0], -4),
        new Eval(914, 783).AccumulateAndRet(MobilityEval[1], -6),
        new Eval(1265, 1344).AccumulateAndRet(MobilityEval[2], -7),
        new Eval(2603, 2442).AccumulateAndRet(MobilityEval[3], -13),
    };

    static readonly Eval BishopPairEval = new Eval(112, 161);

    static readonly Eval[,] Pst = {
        {
            new Eval(-53, 2),
            new Eval(-74, 19),
            new Eval(-0, 6),
            new Eval(-7, -40),
            new Eval(-55, -14),
            new Eval(-19, -31),
            new Eval(-3, -26),
            new Eval(-9, -56),
            new Eval(-1, 34),
            new Eval(43, -9),
            new Eval(75, -26),
            new Eval(38, -15),
            new Eval(41, 75),
            new Eval(40, 56),
            new Eval(16, 33),
            new Eval(5, 29),
        },
        {
            new Eval(-24, -19),
            new Eval(-38, -33),
            new Eval(-24, -33),
            new Eval(-22, -7),
            new Eval(-30, -4),
            new Eval(-43, -13),
            new Eval(-48, -20),
            new Eval(8, 9),
            new Eval(39, 13),
            new Eval(49, 9),
            new Eval(28, 23),
            new Eval(70, 29),
            new Eval(2, 4),
            new Eval(23, 14),
            new Eval(20, 12),
            new Eval(5, 1),
        },
        {
            new Eval(22, -16),
            new Eval(-37, -24),
            new Eval(-29, -16),
            new Eval(24, -18),
            new Eval(6, 4),
            new Eval(1, 2),
            new Eval(-23, 4),
            new Eval(11, -6),
            new Eval(4, 11),
            new Eval(29, 6),
            new Eval(27, 15),
            new Eval(22, 16),
            new Eval(-7, 3),
            new Eval(-0, 7),
            new Eval(2, 3),
            new Eval(-6, -5),
        },
        {
            new Eval(-60, -35),
            new Eval(-4, -38),
            new Eval(-20, -47),
            new Eval(-51, -43),
            new Eval(-49, -12),
            new Eval(-23, -3),
            new Eval(-22, -9),
            new Eval(-5, -19),
            new Eval(13, 23),
            new Eval(28, 31),
            new Eval(29, 20),
            new Eval(29, 10),
            new Eval(30, 38),
            new Eval(46, 44),
            new Eval(34, 33),
            new Eval(33, 31),
        },
        {
            new Eval(-18, -22),
            new Eval(-4, -47),
            new Eval(-15, -46),
            new Eval(-19, -21),
            new Eval(-31, -7),
            new Eval(-34, 3),
            new Eval(-15, -1),
            new Eval(16, -1),
            new Eval(-17, -5),
            new Eval(-3, 25),
            new Eval(31, 37),
            new Eval(60, 24),
            new Eval(-17, -2),
            new Eval(13, 23),
            new Eval(31, 31),
            new Eval(34, 12),
        },
        {
            new Eval(42, -30),
            new Eval(-20, -38),
            new Eval(-75, -38),
            new Eval(48, -68),
            new Eval(5, -6),
            new Eval(-4, 6),
            new Eval(-16, -3),
            new Eval(-17, -21),
            new Eval(11, 22),
            new Eval(12, 40),
            new Eval(9, 42),
            new Eval(6, 23),
            new Eval(6, 7),
            new Eval(9, 22),
            new Eval(11, 28),
            new Eval(5, 14),
        },
    };

    static readonly Eval[] DoubledPawnEval = {
        new Eval(-59, -53),
        new Eval(-34, -41),
        new Eval(-61, -30),
        new Eval(-38, -16),
        new Eval(-61, -25),
        new Eval(-51, -47),
        new Eval(-19, -36),
        new Eval(-41, -48),
    };

    static readonly Eval[] IsolatedPawnEval = {
        new Eval(-33, -20),
        new Eval(-22, -22),
        new Eval(-58, -29),
        new Eval(-64, -41),
        new Eval(-87, -45),
        new Eval(-41, -39),
        new Eval(-27, -21),
        new Eval(-88, -31),
    };

    static readonly Eval[] PassedPawnEval = {
        new Eval(0, 0),
        new Eval(0, 0),
        new Eval(0, 41),
        new Eval(29, 58),
        new Eval(102, 126),
        new Eval(102, 193),
    };

    static readonly Eval OpenFileEval = new Eval(73, 0);
    static readonly Eval SemiOpenFileEval = new Eval(38, 1);

    const BitBoard LightSquares = 0xAA55AA55AA55AA55;
    const BitBoard DarkSquares = ~LightSquares;
    const BitBoard AFile = 0x0101010101010101;
    const BitBoard All = ~0ul;

    // TODO: inline everything in this except for Dumb7Fill
    class MoveGen
    {
        const BitBoard ABFile = 0x0303030303030303;
        const BitBoard HFile = 0x8080808080808080;

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

        public static BitBoard QueenMoves(BitBoard pieces, BitBoard occ) => BishopMoves(pieces, occ) | RookMoves(pieces, occ);
        public static BitBoard BishopMoves(BitBoard pieces, BitBoard occ) => Dumb7Fill(pieces, ~AFile, occ, 9) | Dumb7Fill(pieces, ~HFile, occ, 7);
        public static BitBoard RookMoves(BitBoard pieces, BitBoard occ) => Dumb7Fill(pieces, ~AFile, occ, 1) | Dumb7Fill(pieces, All, occ, 8);
        public static BitBoard KnightMoves(BitBoard pieces, BitBoard occ)
        {
            ulong out_1 = ((pieces << 1) & ~AFile) | ((pieces & ~AFile) >> 1);
            ulong out_2 = ((pieces << 2) & ~ABFile) | ((pieces & ~ABFile) >> 2);
            return (out_1 << 16) | (out_1 >> 16) | (out_2 << 8) | (out_2 >> 8);
        }
    }

    public static short PopCount(BitBoard board)
    {
        short ret = 0;
        for (BitBoard i = 1; i > 0; i <<= 1)
        {
            if ((board & i) == 1)
            {
                ret++;
            }
        }
        return ret;
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

    public static int Resolve(Board board, Eval eval)
    {
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

    public static Eval SidePST(BitBoard[] pieces, byte rowMask)
    {
        Eval eval = new Eval(0, 0);
        for (int i = 0; i < 6; i++)
        {
            BitBoard pieceBoard = pieces[i];
            byte pieceIndex = 0;
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
            byte pieceIndex = 0;
            while (pieceIndex < 64)
            {
                BitBoard piece = pieceBoard & (pieceBoard << 1 >> 1);
                BitBoard movement;
                switch (i) {
                    case 0:
                        movement = MoveGen.KnightMoves(piece, occ);
                        break;
                    case 1:
                        movement = MoveGen.BishopMoves(piece, occ);
                        break;
                    case 2:
                        movement = MoveGen.RookMoves(piece, occ);
                        break;
                    default:
                        movement = MoveGen.QueenMoves(piece, occ);
                        break;
                }
                movement &= mask;
                eval.Accumulate(MobilityEval[i], PopCount(movement));
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
            short pawnCount = PopCount(pawns & file);
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
        throw new System.NotImplementedException();
    }

    public static int Evaluate(Board board)
    {
        throw new System.NotImplementedException();
    }
}