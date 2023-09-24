using System;
using ChessChallenge.API;

public partial class MyBot {
    public const int ExtensionFractBits = 8, ExtensionFractMask = (1 << ExtensionFractBits) - 1, OnePlyExtension = 1 << ExtensionFractBits;
    public const int MaxExtension = 3 * OnePlyExtension, MaxExtensionReductionPerIter = OnePlyExtension / 5;

    private ushort[] threatMoves = new ushort[MaxPlies];

    public bool ApplyBotvinnikMarkoffExtension_I(ushort threatMove, int ply) {
#if DEBUG
        if(threatMove == 0) throw new Exception("Invalid threat move given to the Botvinnik-Markoff extension");
#endif

        threatMoves[ply] = threatMove;

        //Check if the same threat move was played twice in a row
        return ply >= 2 && threatMoves[ply-2] == ply;
    }

    public bool IsLMRAllowedForMove_I(Move move, int moveIdx, int depth)
        => depth >= 3 && moveIdx >= 4 && IsMoveQuiet_I(move) && !IsSpecialPruningMove_I(move);

    public void ApplyLMR_I(int lmrIdx, int depth, ref int extension) {
        //Determine and apply the depth reduction
        if(lmrIdx > 4) extension -= depth * OnePlyExtension / 3;
        else extension -= OnePlyExtension;

#if FSTATS
        STAT_LMR_ApplyReduction_I();
#endif
    }
}