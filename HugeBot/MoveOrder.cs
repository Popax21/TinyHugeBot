using System;
using ChessChallenge.API;

public partial class MyBot {
    public void OrderMoves_I(int alpha, int beta, int remDepth, int ply, Span<Move> moves, ulong ttEntry, ulong boardHash) {
        static void SwapMove_I(ref Move a, ref Move b) {
            Move tmp = a;
            a = b;
            b = tmp;
        }

        //Check if TT contains a move
        //Otherwise, if this isn't a PV node (we assume all non-ZW nodes are), potentially use IID (Internal Iterative Deepening)
        ushort bestMove;
        if((ttEntry & ~TTIdxMask) == (boardHash & ~TTIdxMask)) {
            //Place the move in the TT first
            bestMove = transposMoveTable[boardHash & TTIdxMask];
        } else if(beta > alpha-1 && remDepth >= 3) {
            //Perform IID to determine the move to place first
            NegaMax(alpha, beta, remDepth - 2, ply, out bestMove);
        } else return;

        //Place the best move first
        for(int i = 0; i < moves.Length; i++) {
            if(moves[i].RawValue == bestMove) {
                SwapMove_I(ref moves[0], ref moves[i]);
                break;
            }
        }
    }
}