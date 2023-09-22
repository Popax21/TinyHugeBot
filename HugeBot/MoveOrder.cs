using System;
using ChessChallenge.API;

public partial class MyBot {
    public void OrderMoves_I(int alpha, int beta, int remDepth, int ply, Span<Move> moves, ulong ttEntry, ulong boardHash) {
#if STATS
        STAT_MoveOrder_Invoke_I();
#endif

        //Check if TT contains a move
        //Otherwise, if this isn't a PV node (we assume all non-ZW nodes are), potentially use IID (Internal Iterative Deepening)
        ushort bestMove;
        if((ttEntry & ~TTIdxMask) == (boardHash & ~TTIdxMask)) {
            //Place the move in the TT first
            bestMove = transposMoveTable[boardHash & TTIdxMask];

#if STATS
            STAT_MoveOrder_TTHit_I();
#endif
        } else if(beta > alpha-1 && remDepth >= 3) {
            //Perform IID to determine the move to place first
            NegaMax(alpha, beta, remDepth - 2, ply, out bestMove);

#if STATS
            STAT_MoveOrder_IIDInvoke_I();
#endif
        } else return;

        //Place the best move first
        for(int i = 0; i < moves.Length; i++) {
            if(moves[i].RawValue == bestMove) {
                (moves[0], moves[i]) = (moves[i], moves[0]);
                break;
            }
        }
    }
}