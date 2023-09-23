using System;
using ChessChallenge.API;

public partial class MyBot {
    public void OrderBestMoveFirst_I(int alpha, int beta, int remDepth, int ply, Span<Move> moves, ulong ttEntry, ulong boardHash) {
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
        } else return;

        //Place the best move first
        for(int i = 0; i < moves.Length; i++) {
            if(moves[i].RawValue == bestMove) {
                Move tmp = moves[i];
                moves[i] = moves[0];
                moves[0] = tmp;
                break;
            }
        }
    }
}