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
        ulong ttEntry = transposTable[boardHash & TTIdxMask];
        if(CheckTTEntry_I(ttEntry, boardHash, alpha, beta, 0)) {
            //The evaluation is stored in the lower 16 bits of the entry
            return unchecked((short) ttEntry);
        }

        //Evaluate the current position as a stand-pat score, and update the window using it
        int standPatScore = Eval.Evaluate(searchBoard);
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
#endif

        //Move ordering
        Span<Move> toBeOrderedMoves = PlaceBestMoveFirst_I(alpha, beta, 0, -1, moves, ttEntry, boardHash);
        SortMoves(toBeOrderedMoves, ply);

        ushort bestMove = 0;
        for(int i = 0; i < moves.Length; i++) {
            Move move = moves[i];

            //Apply delta-pruning
            if(ShouldApplyDeltaPruning_I(move, alpha, standPatScore)) {
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
                //We raised alpha
                alpha = score;
                bestMove = move.RawValue;

                if(score >= beta) {
                    //We failed high
#if STATS
                    STAT_AlphaBeta_FailHigh_I(false, true, i);
#endif
                    break;
                }
            }
        }

#if STATS
        //Report if we failed low
        if(bestMove != 0) STAT_AlphaBeta_FailLow_I(false, true);
#endif

        //Store the score in the TT if we didn't fail low
        if(bestMove != 0) {
            TTBoundType ttBound = alpha >= beta ? TTBoundType.Lower : TTBoundType.Exact;
            StoreTTEntry_I(boardHash, (short) alpha, ttBound, 0, bestMove);
        }

        return alpha;
    }
}