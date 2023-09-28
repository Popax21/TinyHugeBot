using ChessChallenge.API;

namespace HugeBot;

public partial class MyBot {
    public const int MaxExtension = 6;

    public void ApplyExtension_I(int newExtension, ref int totalSearchExtension, ref int remDepth) {
        int prevExt = totalSearchExtension;
        totalSearchExtension += newExtension;
        if(totalSearchExtension > MaxExtension) totalSearchExtension = MaxExtension;

        remDepth += totalSearchExtension - prevExt;
    }

    public int DetermineMoveZWExtensions_I(Move move, bool gaveCheck, int moveDepth) {
        int extensions = 0;

        //Apply a search extension if in check
        //TODO Use a better check extension here (maybe using SEE?)
        if(gaveCheck) {
            extensions++;
#if FSTATS
            STAT_CheckExtension_I();
#endif
        }

        return extensions;
    }

    public bool IsLMRAllowedForMove_I(Move move, int moveIdx, int depth, int ply) => depth >= 3 && moveIdx >= 6 && !IsThreatEscapeMove_I(move, ply);
    public int DetermineLMRReduction_I(int moveIdx, int depth) => 2;
}