using ChessChallenge.API;

namespace HugeBot;

public partial class MyBot {
    public const int ExtensionFractBits = 8, ExtensionFractMask = (1 << ExtensionFractBits) - 1, OnePlyExtension = 1 << ExtensionFractBits;
    public const int MaxExtension = 3 * OnePlyExtension, MaxExtensionReductionPerIter = OnePlyExtension / 5;

    private ushort[] threatMoves = new ushort[MaxPlies];

    public void ApplyFractExtension_I(int extension, ref int depth, ref int prevExtensions) {
        //Apply extensions / reductions
        int limitedExt = extension;
        if(prevExtensions + extension > maxExtension) {
            limitedExt = maxExtension - prevExtensions;
#if FSTATS
            if(prevExtensions < maxExtension) STAT_ExtensionLimitHit_I(depth);
#endif
        }

        depth += (((prevExtensions + limitedExt) & ~ExtensionFractMask) - (prevExtensions & ~ExtensionFractMask)) >> ExtensionFractBits;
        prevExtensions += limitedExt;
    }

    public int DetermineMoveZWExtensions_I(Move move, bool gaveCheck, int moveDepth) {
        int extensions = 0;

        //Apply a search extension if in check
        //We don't use the fractional extension variable here because this is before the Q-search check / TT lookup
        //TODO Use a better check extension here (maybe using SEE?)
        if(gaveCheck) {
            extensions += OnePlyExtension;
#if FSTATS
            STAT_CheckExtension_I();
#endif
        }

        return extensions;
    }

    public bool ApplyBotvinnikMarkoffExtension_I(ushort threatMove, int ply) {
        threatMoves[ply] = threatMove;

        //Check if the same threat move was played twice in a row
        return ply >= 2 && threatMoves[ply-2] == ply;
    }

    public bool IsLMRAllowedForMove_I(Move move, int moveIdx, int depth)
        => depth >= 3 && moveIdx >= 6 && IsMoveQuiet_I(move) && !IsThreatEscapeMove_I(move);

    public int DetermineLMRReduction_I(int moveIdx, int depth)
        => 2*OnePlyExtension;
}