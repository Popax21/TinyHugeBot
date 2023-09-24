using System;
using ChessChallenge.API;

public partial class MyBot {
    private const int NumKillerTableSlots = 4;

    private ushort[] killerTable = new ushort[MaxPly * NumKillerTableSlots];

    public void MoveOrder_Reset_I() => Array.Clear(killerTable);

    public Span<Move> PlaceBestMoveFirst_I(int alpha, int beta, int remDepth, int ply, Span<Move> moves, ulong ttEntry, ulong boardHash) {
#if STATS
        STAT_MoveOrder_BestMoveInvoke_I();
#endif

        //Check if TT contains a move
        //Otherwise, if this isn't a PV node (we assume all non-ZW nodes are), potentially use IID (Internal Iterative Deepening)
        ushort bestMove;
        if((ttEntry & ~TTIdxMask) == (boardHash & ~TTIdxMask)) {
            //Place the move in the TT first
            bestMove = transposMoveTable[boardHash & TTIdxMask];

#if STATS
            STAT_MoveOrder_BestMoveTTHit_I();
#endif
        } else if(beta > alpha-1 && remDepth >= 3) {
            //Perform IID to determine the move to place first
            NegaMax(alpha, beta, remDepth - 2, ply, out bestMove);

#if STATS
            STAT_MoveOrder_BestMoveIIDInvoke_I();
#endif
        } else return moves;

        //Place the best move first
        for(int i = 0; i < moves.Length; i++) {
            if(moves[i].RawValue == bestMove) {
                Move tmp = moves[i];
                moves[i] = moves[0];
                moves[0] = tmp;
                return moves.Slice(1); //Don't use range operators, those bloat the IL
            }
        }
        return moves;
    }

    private static int curMoveOrderPly;
    public void SortMoves_I(Span<Move> moves, int ply) {
        long GetMoveScore_I(Move move) {
            if(move.IsCapture | move.IsPromotion) {
                //Score by MVV-LAA (Most Valuable Victim - Least Valuable Aggressor)
                //Promotions take priority over other moves
                //TODO Try other metrics (e.g. SSE)
                return (long) ((int) move.PromotionPieceType << 16 | (int) move.CapturePieceType << 4 | (15 - (int) move.MovePieceType)) << 32;
            } else {
                //Check if the move is in the killer table
                for(int i = 0; i < NumKillerTableSlots; i++) {
                    if(killerTable[NumKillerTableSlots*curMoveOrderPly + i] == move.RawValue) return (long) (NumKillerTableSlots - i) << 16;
                }

                return 0;
            }
        }
        int CompareMoves(Move a, Move b) => GetMoveScore_I(b).CompareTo(GetMoveScore_I(a));

        curMoveOrderPly = ply;
        moves.Sort(CompareMoves);
    }

    public bool IsMoveQuiet_I(Move move) => !move.IsCapture && !move.IsPromotion;

    public void InsertIntoKillerTable_I(int ply, Move move) {
        ushort moveVal = move.RawValue;

        for(int i = 1; i < NumKillerTableSlots; i++) {
            if(killerTable[NumKillerTableSlots*ply + i] == moveVal) {
                //Don't remove the old first move
                killerTable[NumKillerTableSlots*ply + i] = killerTable[NumKillerTableSlots*ply + 0];
                break;
            }
        }

        killerTable[NumKillerTableSlots*ply + 0] = moveVal;
    }
}