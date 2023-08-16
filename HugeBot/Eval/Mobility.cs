using System;
using System.Numerics;
using BitBoard = System.UInt64;
using Eval = System.UInt64;

namespace HugeBot;

public static partial class Evaluator {
    public static BitBoard Dumb7Fill(BitBoard startSpaces, BitBoard leftEndMask, BitBoard freeSpaces, byte shift) {
        BitBoard leftSpaces = startSpaces, rightSpaces = startSpaces;

        //Do the first 6 moves where we aren't allowed to capture
        for(int i = 0; i < 6; i++) {
            leftSpaces |= (leftSpaces << shift) & leftEndMask & freeSpaces;
            rightSpaces |= ((rightSpaces & leftEndMask) >> shift) & freeSpaces;
        }

        //Do the last move where we are allowed to capture (even our own pieces! but eh, that can be filtered out later)
        return ((leftSpaces << shift) & leftEndMask) | ((rightSpaces & leftEndMask) >> shift);
    }

    public static Eval EvalMobility(Span<BitBoard> pieces, BitBoard freeSpaces, BitBoard notOwnSpaces) {
        Eval eval = 0;
        for(int i = 0; i < 4; i++) {
            BitBoard pieceBoard = pieces[i + 1];
            while(pieceBoard != 0) {
                //Get one piece from the board
                BitBoard piece = pieceBoard & unchecked((ulong) -(long) pieceBoard);
                pieceBoard &= pieceBoard - 1; //Move onto the next piece

                //Determine the spaces it can move to
                BitBoard reachableSpaces;
                switch(i) {
                    case 0: //Knights
                        ulong colMove1 = ((piece << 1) & ~AFile) | ((piece & ~AFile) >> 1);
                        ulong colMove2 = ((piece << 2) & ~ABFile) | ((piece & ~ABFile) >> 2);
                        reachableSpaces = (colMove1 << 16) | (colMove1 >> 16) | (colMove2 << 8) | (colMove2 >> 8);
                        break;
                    case 1: //Bishops
                        reachableSpaces = Dumb7Fill(piece, ~AFile, freeSpaces, 9) | Dumb7Fill(piece, ~HFile, freeSpaces, 7);
                        break;
                    case 2: //Rooks
                        reachableSpaces = Dumb7Fill(piece, ~AFile, freeSpaces, 1) | Dumb7Fill(piece, All, freeSpaces, 8);
                        break;
                    default: //Queens
                        reachableSpaces = 
                            Dumb7Fill(piece, ~AFile, freeSpaces, 9) | Dumb7Fill(piece, ~HFile, freeSpaces, 7) | 
                            Dumb7Fill(piece, ~AFile, freeSpaces, 1) | Dumb7Fill(piece, All, freeSpaces, 8);
                        break;
                }
                reachableSpaces &= notOwnSpaces;

                //Update the evaluation
                eval += MobilityEval[i] * (uint) BitOperations.PopCount(reachableSpaces);
            }
        }
        return eval;
    }    

    public static readonly Eval[] MobilityEval = DecompressEvals(new ushort[] {
        /* Knight */ 0x25_15,
        /* Bishop */ 0x18_0a,
        /* Rook   */ 0x11_01,
        /* Queen  */ 0X0c_04,
    });
}