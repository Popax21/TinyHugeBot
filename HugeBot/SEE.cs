using System;
using System.Numerics;
using ChessChallenge.API;

namespace HugeBot;

public static class SEE {
    private const ulong AFile = 0x0101010101010101, BFile = 0x0202020202020202, ABFile = AFile | BFile;
    private const ulong GFile = 0x4040404040404040, HFile = 0x8080808080808080, GHFile = GFile | HFile;

    private static int[] PieceValues = { 1, 12, 14, 22, 40, 10000 };
    public static int EvaluateCapture(Board board, Move move, bool isWhite) {
        int targetSquare = move.TargetSquare.Index;
        ulong targetBitboard = 1UL << targetSquare;
        ulong occupiedBitboard = board.AllPiecesBitboard;

        Span<ulong> pieceBitboards = stackalloc ulong[12];
        for(int i = 0; i < 12; i++) pieceBitboards[i] = board.GetPieceBitboard((PieceType) (i / 2 + 1), i % 2 == 0);

        //Determine attackers
        ulong attackers = 0;

        // - pawns
        unchecked {
            attackers |= pieceBitboards[0*2 + 0] & ((targetBitboard & ~AFile) >> (8+1) | (targetBitboard & ~HFile) >> (8-1));
            attackers |= pieceBitboards[0*2 + 1] & ((targetBitboard & ~AFile) << (8-1) | (targetBitboard & ~HFile) << (8+1));
        }

        // - knights
        attackers |= (pieceBitboards[1*2 + 0] | pieceBitboards[1*2 + 1]) & KnightAttacks[targetSquare];

        // - sliders (bishops / rooks / queens)
        static ulong DeterminePosRayAttacks(int square, ulong occup, int dir) {
            ulong attacks = RayAttacks[dir*65 + square];
            int blockSq = BitOperations.TrailingZeroCount(attacks & occup);
            attacks ^= RayAttacks[dir*65 + blockSq];
            return attacks;
        }
        static ulong DetermineNegRayAttacks(int square, ulong occup, int dir) {
            ulong attacks = RayAttacks[dir*65 + square];
            int blockSq = 63 - BitOperations.LeadingZeroCount((attacks & occup) | 1);
            attacks ^= RayAttacks[dir*65 + blockSq];
            return attacks;
        }
        
        ulong orthSliders = pieceBitboards[3*2 + 0] | pieceBitboards[3*2 + 1] | pieceBitboards[4*2 + 0] | pieceBitboards[4*2 + 1];
        ulong orthRay1Attacks = DetermineNegRayAttacks(targetSquare, occupiedBitboard, 0);
        ulong orthRay2Attacks = DeterminePosRayAttacks(targetSquare, occupiedBitboard, 1);
        ulong orthRay3Attacks = DetermineNegRayAttacks(targetSquare, occupiedBitboard, 2);
        ulong orthRay4Attacks = DeterminePosRayAttacks(targetSquare, occupiedBitboard, 3);
        attackers |= orthSliders & (orthRay1Attacks | orthRay2Attacks | orthRay3Attacks | orthRay4Attacks);

        ulong diagSliders = pieceBitboards[2*2 + 0] | pieceBitboards[2*2 + 1] | pieceBitboards[4*2 + 0] | pieceBitboards[4*2 + 1];
        ulong diagRay1Attacks = DetermineNegRayAttacks(targetSquare, occupiedBitboard, 4);
        ulong diagRay2Attacks = DeterminePosRayAttacks(targetSquare, occupiedBitboard, 5);
        ulong diagRay3Attacks = DetermineNegRayAttacks(targetSquare, occupiedBitboard, 6);
        ulong diagRay4Attacks = DeterminePosRayAttacks(targetSquare, occupiedBitboard, 7);
        attackers |= diagSliders & (diagRay1Attacks | diagRay2Attacks | diagRay3Attacks | diagRay4Attacks);

        // - kings
        attackers |= (pieceBitboards[5*2 + 0] | pieceBitboards[5*2 + 1]) & KingAttacks[targetSquare];

        //Execute the SEE swap algorithm
        int depth = 0;
        Span<int> gains = stackalloc int[32];
        gains[0] = PieceValues[(int) move.CapturePieceType - 1];

        ulong potXrayPieces = occupiedBitboard ^ pieceBitboards[1*2 + 0] ^ pieceBitboards[1*2 + 1] ^ pieceBitboards[5*2 + 0] ^ pieceBitboards[5*2 + 1];

        int attackerType = (int) move.MovePieceType - 1;
        int attackerSide = isWhite ? 0 : 1;
        ulong attackerBitboard = 1UL << move.StartSquare.Index;
        do {
            depth++;

            //Update the gain
            gains[depth] = PieceValues[attackerType] - gains[depth-1];
            if(gains[depth] < 0 && gains[depth-1] > 0) break;

            //Remove the attacker
#if VALIDATE
            if((attackers & attackerBitboard) == 0) throw new Exception("SEE attacker is not contained in attacker bitboard");
            if((attackers & occupiedBitboard) == 0) throw new Exception("SEE attacker is not contained in occupied bitboard");
#endif

            attackers ^= attackerBitboard;
            occupiedBitboard ^= attackerBitboard;
            attackerSide ^= 1;

            //Handle X-Rays
            if((attackerBitboard & potXrayPieces) != 0) {
                //Re-determine attackers on the ray the piece was on
                       if((attackerBitboard & orthRay1Attacks) != 0) {
                    orthRay1Attacks = DetermineNegRayAttacks(targetSquare, occupiedBitboard, 0);
                } else if((attackerBitboard & orthRay2Attacks) != 0) {
                    orthRay2Attacks = DeterminePosRayAttacks(targetSquare, occupiedBitboard, 1);
                } else if((attackerBitboard & orthRay3Attacks) != 0) {
                    orthRay3Attacks = DetermineNegRayAttacks(targetSquare, occupiedBitboard, 2);
                } else if((attackerBitboard & orthRay4Attacks) != 0) {
                    orthRay4Attacks = DeterminePosRayAttacks(targetSquare, occupiedBitboard, 3); 
                } else if((attackerBitboard & diagRay1Attacks) != 0) {
                    diagRay1Attacks = DetermineNegRayAttacks(targetSquare, occupiedBitboard, 4);
                } else if((attackerBitboard & diagRay2Attacks) != 0) {
                    diagRay2Attacks = DeterminePosRayAttacks(targetSquare, occupiedBitboard, 5);
                } else if((attackerBitboard & diagRay3Attacks) != 0) {
                    diagRay3Attacks = DetermineNegRayAttacks(targetSquare, occupiedBitboard, 6);        
                } else if((attackerBitboard & diagRay4Attacks) != 0) {
                    diagRay4Attacks = DeterminePosRayAttacks(targetSquare, occupiedBitboard, 7);
                }
#if VALIDATE
                else throw new Exception("Potential X-Ray piece not on any ray");
#endif

                attackers |= occupiedBitboard & orthSliders & (orthRay1Attacks | orthRay2Attacks | orthRay3Attacks | orthRay4Attacks);
                attackers |= occupiedBitboard & diagSliders & (diagRay1Attacks | diagRay2Attacks | diagRay3Attacks | diagRay4Attacks);
            }

            //Determine the least valuable attacking piece
            attackerBitboard = 0;
            for(int i = attackerSide; i < 2*6; i += 2) {
                ulong atck = pieceBitboards[i] & attackers;
                if(atck == 0) continue;

                attackerType = i/2;
                attackerBitboard = atck & ~(atck-1);
                break;
            } 
        } while(attackerBitboard != 0);

        //Determine the final gain
        while(--depth > 0) {
            if(-gains[depth] < gains[depth-1]) gains[depth-1] = -gains[depth];
        }
        return gains[0];
    }

    private static readonly ulong[] KingAttacks = new ulong[64];
    private static readonly ulong[] KnightAttacks = new ulong[64];
    private static readonly ulong[] RayAttacks = new ulong[8*65];
    private static readonly sbyte[] RayAttackVectors = { -1, +1, -8, +8, -1-8, +1+8, +1-8, -1+8 };

    static SEE() {
        unchecked {
            for(int sq = 0; sq < 64; sq++) {
                ulong squareBitboard = 1UL << sq;

                //Precompute king attacks
                KingAttacks[sq] |= squareBitboard << 8;
                KingAttacks[sq] |= squareBitboard >> 8;
                KingAttacks[sq] |= (squareBitboard & ~AFile) << (8 - 1);
                KingAttacks[sq] |= (squareBitboard & ~AFile) >> (0 + 1);
                KingAttacks[sq] |= (squareBitboard & ~AFile) >> (8 + 1);
                KingAttacks[sq] |= (squareBitboard & ~HFile) << (8 + 1);
                KingAttacks[sq] |= (squareBitboard & ~HFile) << (0 + 1);
                KingAttacks[sq] |= (squareBitboard & ~HFile) >> (8 - 1);

                //Precompute knight attacks
                KnightAttacks[sq] |= (squareBitboard & ~AFile) << (16 - 1) | (squareBitboard & ~AFile) >> (16 + 1);
                KnightAttacks[sq] |= (squareBitboard & ~HFile) << (16 + 1) | (squareBitboard & ~HFile) >> (16 - 1);
                KnightAttacks[sq] |= (squareBitboard & ~ABFile) << (8 - 2) | (squareBitboard & ~ABFile) >> (8 + 2);
                KnightAttacks[sq] |= (squareBitboard & ~GHFile) << (8 + 2) | (squareBitboard & ~GHFile) >> (8 - 2);
                
                //Precompute ray attacks
                for(int i = 0; i < 8; i++) {
                    int rayDir = RayAttackVectors[i];
                    ulong rayMask = (rayDir & 7) switch {
                        0 => ~0UL,
                        1 => ~HFile,
                        7 => ~AFile,
#if VALIDATE
                        _ => throw new Exception("Invalid ray direction for ray attack precomputation")
#else
                        _ => 0
#endif
                    };

                    ulong bb = squareBitboard;
                    while(true) {
                        bb &= rayMask;
                        bb = rayDir < 0 ? (bb >> -rayDir) : (bb << rayDir);
                        if(bb == 0) break;
                        RayAttacks[i*65 + sq] |= bb;
                    }
                }
            }
        }
    }
}