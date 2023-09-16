using System;

public partial class MyBot {
    public const int TTSize = 1 << 24; //16k entries * 8b/e -> 128MB
    private const ulong TTIdxMask = TTSize-1;

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
    private ulong[] transposTable = new ulong[TTSize];

    private bool CheckTTEntry(ulong entry, ulong boardHash, int alpha, int beta, int depth) {
        //Check if the hash bits match
        if((entry & ~TTIdxMask) != (boardHash & ~TTIdxMask)) return false;

        //Check that the entry searched at least as deep as we would
        if((int) ((entry >> 18) & 0x3f) < depth) return false;

        //Check the node bound type
        return (TTBoundType) ((entry >> 16) & 0b11) switch {
            TTBoundType.Exact => true,
            TTBoundType.Lower => beta <= unchecked((short) entry),
            TTBoundType.Upper => unchecked((short) entry) <= alpha,
            _ => false
        };
    }

    private ulong EncodeTTEntry(short eval, TTBoundType bound, int depth, ulong boardHash) {
#if DEBUG
        //Check for overflows
        if(bound < TTBoundType.Exact || bound > TTBoundType.Upper) throw new ArgumentException($"Garbage TT bound given: {bound}");
        if(depth < 0 || depth >= (1 << 6)) throw new ArgumentException($"Out-of-bounds TT depth given: {depth}");
#endif

        return
            unchecked((ushort) eval) |
            ((ulong) bound << 16) |
            ((ulong) depth << 18) |
            (boardHash & ~TTIdxMask)
        ;   
    }
}