using ChessChallenge.API;

public partial class MyBot {
    private int numNullMoves = 0;
    private ushort threatMove;
    public bool ApplyNullMovePruning_I(int alpha, int beta, int remDepth, int ply, int curExtension, ref int score) {
        //Check if we should apply NMP
        //Allow two null moves in a row to mitigate Zugzwang
        //TODO Find better ways to do this
        if(remDepth < 3 || numNullMoves >= 2) return false;
        
        //TODO Experiment with other values for X 
        //TODO Transition to Null Move Reductions once we get near the endgame
        int R = 2 + remDepth / 4;

        //Evaluate the null move using a ZWS
        searchBoard.ForceSkipTurn();
        numNullMoves++;
        score = -NegaMax(-beta, -beta + 1, remDepth - 1 - R, ply+1, out threatMove, curExtension);
        numNullMoves--;
        searchBoard.UndoSkipTurn(); 

        return true;
    }

    private const int PruningSafetyMargin = 2*90; //~200 centipawns

    public bool ApplyReverseFutilityPruning_I(int eval, int beta, int depth, ref int score) {
        //TODO Experiment with different values
        //TODO This relies on the Null Move Hypothesis, investigate potential Zugzwang issues
        score = eval - depth * 90 - (PruningSafetyMargin - 1*90);
        return depth < 7 && score >= beta;
    }

    private static readonly ushort[] DeltaPruningMargins = new ushort[] {
        PruningSafetyMargin + 1000,    //None - as we only evaluate non-quiet moves this means that it's a pawn promition, so it has the same margin as a queen
        PruningSafetyMargin + 90,      //Pawns
        PruningSafetyMargin + 310,     //Knights
        PruningSafetyMargin + 340,     //Bishops
        PruningSafetyMargin + 500,     //Rooks
        PruningSafetyMargin + 1000,    //Queen
        0                              //Kings - just a placeholder
    };

    //TODO Check if disabling near the endgame helps things
    public bool ApplyDeltaPruning_I(Move move, int alpha, int standPatScore)
        => standPatScore + DeltaPruningMargins[(int) move.CapturePieceType] < alpha;

    private void ResetThreatMove_I(int ply) => threatMove = threatMoves[ply] = 0;
    private bool IsThreatEscapeMove_I(Move move) {
        //Check if the move is escaping the square attacked by the null move refutation
        if(threatMove != 0 && move.StartSquare.Index == ((threatMove >> 6) & 63)) return true;

        return false;
    }
}