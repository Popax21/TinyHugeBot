using ChessChallenge.API;
using HugeBot;

public partial class MyBot {
    private int numNullMoves = 0;
    private ushort nullMoveRefutation;
    public bool TryNullMovePruning_I(int alpha, int beta, int remDepth, int ply, ref int score) {
#if FSTATS
        STAT_NullMovePruning_Invoke_I();
#endif

        //Check if we should apply NMP
        if(remDepth < 3 || beta >= Eval.MaxMate) return false;

        //Allow two null moves in a row to mitigate Zugzwang
        if(numNullMoves >= 2) return false;
        
        //TODO Experiment with other values for X 
        //TODO Transition to Null Move Reductions once we get near the endgame
        //R = 2.5 + depth / 4 = 2 + (depth + 2) / 4
        //http://www.talkchess.com/forum3/viewtopic.php?t=31436#p314767
        int R = 2 + (remDepth + 2) / 4;

        //Evaluate the null move using a ZWS
        if(!searchBoard.TrySkipTurn()) return false;
        numNullMoves++;
        score = -NegaMax(-beta, -beta + 1, remDepth - 1 - R, ply+1, out nullMoveRefutation);
        numNullMoves--;
        searchBoard.UndoSkipTurn(); 

#if FSTATS
        if(score >= beta) STAT_NullMovePruning_Cutoff_I();
#endif

        return score >= beta;
    }

    private void Pruning_ResetSpecialMove_I() => nullMoveRefutation = 0;

    private bool Pruning_IsSpecialMove_I(Move move) {
        //Check if the move is escaping the square attacked by the null move refutation
        if(nullMoveRefutation != 0 && move.StartSquare.Index == ((nullMoveRefutation >> 6) & 63)) return true;

        return false;
    }
}