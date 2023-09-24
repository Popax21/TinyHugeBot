using System;
using ChessChallenge.API;
using HugeBot;

public partial class MyBot {
    public int QSearch(int alpha, int beta, int ply) {
#if STATS
        STAT_NewNode_I(false, true);
#endif

        //Handle repetition
        if(searchBoard.IsRepeatedPosition()) return 0;

        //Probe the TT
        //TODO Is storing our result back into the TT worth it?
        ulong boardHash = searchBoard.ZobristKey;
        ref ulong ttSlot = ref transposTable[boardHash & TTIdxMask];
        if(CheckTTEntry_I(ttSlot, boardHash, alpha, beta, 0)) {
            //The evaluation is stored in the lower 16 bits of the entry
            return unchecked((short) ttSlot);
        }

        //Evaluate the current position as a stand-pat score, and update the window using it
        int standPatScore = Eval.Evaluate_I(searchBoard);
        if(standPatScore > alpha) {
            if(standPatScore >= beta) return standPatScore;
            alpha = standPatScore;
        }

        //Generate legal capture moves
        Span<Move> moves = stackalloc Move[256];
        searchBoard.GetLegalMovesNonAlloc(ref moves, true);
        if(moves.Length == 0) return alpha;

#if STATS
        //Report that we are starting to search a new Q-Search node
        STAT_AlphaBeta_SearchNode_I(false, true, moves.Length);
        bool failedLow = true;
#endif

        //Move ordering
        Span<Move> toBeOrderedMoves = PlaceBestMoveFirst_I(alpha, beta, 0, -1, moves, ttSlot, boardHash);
        SortMoves(toBeOrderedMoves, ply);

        for(int i = 0; i < moves.Length; i++) {
            Move move = moves[i];

            //Apply move delta-pruning
            if(ApplyDeltaPruning_I(move, alpha, standPatScore)) {
#if FSTATS
                STAT_DeltaPruning_PrunedMove();
#endif

                continue;
            }

            //Evaluate the move
            searchBoard.MakeMove(move);
            int score = -QSearch(-beta, -alpha, ply+1);
            searchBoard.UndoMove(move);

#if STATS
            STAT_AlphaBeta_SearchedMove_I(false, true);
#endif

            //Update the window
            if(score > alpha) {
                if(score >= beta) {
                    //We failed high
#if STATS
                    STAT_AlphaBeta_FailHigh_I(false, true, i);
#endif

                    //Insert into the killer table if the move is quiet
                    if(IsMoveQuiet_I(move)) InsertIntoKillerTable_I(ply, move); 

                    return score;
                }

                //We raised alpha
                alpha = score;

#if STATS
                failedLow = false;
#endif
            }
        }

#if STATS
        //Report if we failed low
        if(failedLow) STAT_AlphaBeta_FailLow_I(false, true);
#endif

        return alpha;
    }
}