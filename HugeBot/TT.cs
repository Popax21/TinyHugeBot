using System;

namespace HugeBot;

public partial class MyBot {
    public const int TTSize = 0x1800000; //24M entries * 10b/e -> 240MB (16MB for other tables)

    private enum TTBoundType {
        None = 0b00 << 16,
        Exact = 0b01 << 16, //For PV nodes
        Lower = 0b10 << 16, //For Fail-High / cut nodes
        Upper = 0b11 << 16, //For Fail-Low / all nodes
    }

    //TT entry structure:
    //  bits  0-15: position evaluation
    //  bits 16-17: bound/node type
    //  bits 18-23: search depth
    //  bits 24-63: upper hash bits
    private const ulong TTBoundMask = 0x030000UL;
    private const ulong TTDepthMask = 0xfc0000UL;
    private const ulong TTHashMask = ~0xffffffUL;
 
    private ulong[] transposTable = new ulong[TTSize];
    private ushort[] transposMoveTable = new ushort[TTSize];
 
    private bool CheckTTEntry_I(ulong boardHash, int alpha, int beta, int depth, out ulong index, out bool entryValid, out ulong entry) {
        //Fetch the entry and check its hash
        index = boardHash % TTSize;
        entry = transposTable[index];
        if((entry & TTHashMask) != (boardHash & TTHashMask)) {
#if FSTATS
            STAT_TTRead_Miss_I();
#endif
            entryValid = false;
            return false;
        }
        entryValid = true;


        //Check that the entry searched at least as deep as we would
        if((int) ((entry >> 18) & 0x3f) < depth) {
#if FSTATS
            STAT_TTRead_DepthMiss_I();
#endif
            return false;
        }

        //Check the node bound type
        //TODO ice4 checks whether the node is a PV node for exact scores
        if(!((TTBoundType) (entry & TTBoundMask) switch {
            TTBoundType.Exact => true,
            TTBoundType.Lower => beta <= unchecked((short) entry),
            TTBoundType.Upper => unchecked((short) entry) <= alpha,
#if VALIDATE
            _ => throw new Exception($"Invalid TT entry bound type: entry 0x{entry:x16}")
#else
            _ => false
#endif
        })) {
#if FSTATS
            STAT_TTRead_BoundMiss_I();
#endif

            return false;
        }

#if FSTATS
        STAT_TTRead_Hit_I();
#endif

        return true;
    }

    private void StoreTTEntry_I(ulong boardHash, short eval, TTBoundType bound, int depth, ushort bestMove) {
#if VALIDATE
        //Check for overflows
        if(bound < TTBoundType.Exact || bound > TTBoundType.Upper) throw new ArgumentException($"Garbage TT bound given: {bound}");
        if(depth < 0 || depth >= (1 << 6)) throw new ArgumentException($"Out-of-bounds TT depth given: {depth}");
#endif

        ulong ttIdx = boardHash % TTSize;
        ulong prevEntry = transposTable[ttIdx];
        bool isUpdate = (prevEntry & TTHashMask) == (boardHash & TTHashMask);

#if FSTATS
        //Check for collisions
        if((prevEntry & TTBoundMask) == (ulong) TTBoundType.None) STAT_TTWrite_NewSlot_I();
        else if(!isUpdate) STAT_TTWrite_IdxCollision_I();
        else STAT_TTWrite_SlotUpdate_I();
#endif

        //Store the TT entry
        transposTable[ttIdx] =
            unchecked((ushort) eval) |
            (ulong) bound |
            ((ulong) depth << 18) |
            (boardHash & TTHashMask)
        ;
        if(!isUpdate || bound != TTBoundType.Upper) transposMoveTable[ttIdx] = bestMove; //Don't overwrite the old move if we failed low
    }
}