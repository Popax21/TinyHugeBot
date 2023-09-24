using ChessChallenge.API;

public partial class MyBot {
    public bool IsLMRAllowedForMove_I(Move move, int moveIdx, int depth)
        => depth >= 3 && moveIdx >= 6 && IsMoveQuiet_I(move) && !IsSpecialPruningMove_I(move);

    public void ApplyLMR_I(bool isPvCandidateNode, int lmrIdx, ref int depth) {
        //Determine and apply the depth reduction (based on a combination of Senpai's and Fruit Reloaded's formula)
        //TODO Experiment with different formulas
        int R = lmrIdx <= 6 ? 1 : (depth / 3);
        depth -= isPvCandidateNode ? R * 2 / 3 : R;

#if FSTATS
        STAT_LMR_ApplyReduction_I();
#endif
    }
}