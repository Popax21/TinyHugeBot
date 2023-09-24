using ChessChallenge.API;
using HugeBot;

public partial class MyBot {
    private int numNullMoves = 0;
    private ushort nullMoveRefutation;
    public bool ApplyNullMovePruning_I(int alpha, int beta, int remDepth, int ply, ref int score) {
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

    private const int DeltaPruningSafetyMargin = 2*90; //~200 centipawns
    private static readonly ushort[] DeltaPruningMargins = new ushort[] {
        DeltaPruningSafetyMargin + 1000,    //None - as we only evaluate non-quiet moves this means that it's a pawn promition, so it has the same margin as a queen
        DeltaPruningSafetyMargin + 90,      //Pawns
        DeltaPruningSafetyMargin + 310,     //Knights
        DeltaPruningSafetyMargin + 340,     //Bishops
        DeltaPruningSafetyMargin + 500,     //Rooks
        DeltaPruningSafetyMargin + 1000,    //Queen
        0                                   //Kings - just a placeholder
    };

    public bool ApplyDeltaPruning_I(Move move, int alpha, int standPatScore)
        => standPatScore + DeltaPruningMargins[(int) move.CapturePieceType] < alpha;

    private void Pruning_ResetSpecialMove_I() => nullMoveRefutation = 0;

    private bool Pruning_IsSpecialMove_I(Move move) {
        //Check if the move is escaping the square attacked by the null move refutation
        if(nullMoveRefutation != 0 && move.StartSquare.Index == ((nullMoveRefutation >> 6) & 63)) return true;

        return false;
    }
}