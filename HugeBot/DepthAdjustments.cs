using ChessChallenge.API;

public partial class MyBot {
    public bool IsLMRAllowedForMove_I(Move move, int moveIdx, int depth)
        => depth >= 3 && moveIdx >= 4 && IsMoveQuiet_I(move) && !IsSpecialPruningMove_I(move);

    public void ApplyLMR_I(int lmrIdx, ref int depth) {
        //Determine and apply the depth reduction
        if(lmrIdx > 4) depth -= depth / 3;
        else depth--;

#if FSTATS
        STAT_LMR_ApplyReduction_I();
#endif
    }
}