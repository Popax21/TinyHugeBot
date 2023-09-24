using System;

public partial class MyBot {
    public const int TTSize = 1 << 24; //16k entries * 8b/e -> 128MB
    private const ulong TTIdxMask = TTSize-1;

    private enum TTBoundType {
        None = 0b00 << 16,
        Exact = 0b01 << 16, //For PV nodes
        Lower = 0b10 << 16, //For Fail-High / cut nodes
        Upper = 0b11 << 16, //For Fail-Low / all nodes

        MASK = 0b11 << 16
    }

    //TT entry structure:
    //  bits  0-15: position evaluation
    //  bits 16-17: bound/node type
    //  bits 18-23: search depth
    //  bits 24-63: upper hash bits
    private ulong[] transposTable = new ulong[TTSize];
    private ushort[] transposMoveTable = new ushort[TTSize];

    private bool CheckTTEntry_I(ulong entry, ulong boardHash, int alpha, int beta, int depth) {
        //Check if the hash bits match
        if((entry & ~TTIdxMask) != (boardHash & ~TTIdxMask)) {
#if STATS && FSTATS
            STAT_TTRead_Miss_I();
#endif
            return false;
        }

        //Check that the entry searched at least as deep as we would
        if((int) ((entry >> 18) & 0x3f) < depth) {
#if STATS && FSTATS
            STAT_TTRead_DepthMiss_I();
#endif
            return false;
        }

        //Check the node bound type
        if(!((TTBoundType) (entry & (ulong) TTBoundType.MASK) switch {
            TTBoundType.Exact => true,
            TTBoundType.Lower => beta <= unchecked((short) entry),
            TTBoundType.Upper => unchecked((short) entry) <= alpha,
#if DEBUG
            _ => throw new Exception($"Invalid TT entry bound type: entry 0x{entry:x16}")
#else
            _ => false
#endif
        })) {
#if STATS && FSTATS
            STAT_TTRead_BoundMiss_I();
#endif

            return false;
        }

#if STATS && FSTATS
        STAT_TTRead_Hit_I();
#endif

        return true;
    }

    private void StoreTTEntry_I(ref ulong entrySlot, short eval, TTBoundType bound, int depth, ulong boardHash) {
#if DEBUG
        //Check for overflows
        if(bound < TTBoundType.Exact || bound > TTBoundType.Upper) throw new ArgumentException($"Garbage TT bound given: {bound}");
        if(depth < 0 || depth >= (1 << 6)) throw new ArgumentException($"Out-of-bounds TT depth given: {depth}");
#endif

#if STATS && FSTATS
        //Check for collisions
        ulong prevEntry = entrySlot;
        if((prevEntry & (ulong) TTBoundType.MASK) == (ulong) TTBoundType.None) STAT_TTWrite_NewSlot_I();
        else if((prevEntry & ~TTIdxMask) != (boardHash & ~TTIdxMask)) STAT_TTWrite_IdxCollision_I();
        else STAT_TTWrite_SlotUpdate_I();
#endif

        //Store the TT entry
        entrySlot =
            unchecked((ushort) eval) |
            (ulong) bound |
            ((ulong) depth << 18) |
            (boardHash & ~TTIdxMask)
        ;
    }
}