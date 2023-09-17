using ChessChallenge.API;
using static System.AppDomain;

class MyBot : IChessBot {
    //TinyBot_asmBuf either holds the TinyBot IChessBot instance, or the assembly buffer during decoding
    //We declare all our other variables here as well to save tokens later by either:
    // - for bits: removing the need for a `var` token
    // - for the rest: removing the need to zero-initialize
    dynamic TinyBot_asmBuf = new byte[<TINYASMSIZE>], bits;
    int asmDataBufOff, scaleAccum, parity;

    public MyBot() {
        //Decode the assembly
        //The assembly is encoded in a semi-RLE-like format
        //There are two types of tokens:
        // - scaling factors [0;16): regular tokens: carry 100 bits of info
        //   - 96 bits through the decimal integer number, which are immediately copied to the assembly buffer
        //   - 4 bits through the decimal scaling factor: two regular tokens are paired up, and their scaling factors are combined into an extra byte
        // - scaling factor 16: skip tokens: carry 88 bits of info, and can skip up to 255 bytes forward (efficiently encoding a stretch of null bytes)
        //   - lowest 8 bits of the decimal integer number: the skip amount
        //   - the remaining integer number bits are immediately copied to the assembly buffer
        //   - skip tokens are invisible to the scalar accumulator; they don't contribute a scale value, nor do they affect parity
        // - the sign bit is unused as a minus sign requires an extra token
        foreach(decimal dec in new[] {
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> BEGIN ENCODED ASSEMBLY <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
            <TINYASMENCDAT>
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>  END ENCODED ASSEMBLY  <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
        }) {
            //Get the bits of the decimal
            bits = decimal.GetBits(dec);

            //Skip forward if the highest scalar bit is set
            int decBitIdx = bits[3] >> 20 << 3; //8 for skip tokens, 0 otherwise
            if(decBitIdx != 0) asmDataBufOff += (byte) bits[0];
            else {
                //Accumulate two 4 bit scales, then add to the buffer
                //Note that for even parity tokens, the byte we write here is immediately overwritten again 
                scaleAccum <<= 4;
                TinyBot_asmBuf[asmDataBufOff++] = (byte) (scaleAccum |= bits[3] >> 16);
                asmDataBufOff -= parity ^= 1;
            }

            //Add the 88/96 bits of the integer number to the buffer
            for(; decBitIdx < 96; decBitIdx += 8)
                TinyBot_asmBuf[asmDataBufOff++] = (byte) (bits[decBitIdx / 32] >> decBitIdx);
        }

        //Load the tiny bot from the assembly
        //We can't just load it and be done with it, because the byte[] overload doesn't add the assembly to the regular load path
        //As such load it whenever any assembly fails to load >:)
        System.ResolveEventHandler asmResolveCB = (_, _) => CurrentDomain.Load(TinyBot_asmBuf); //We can't use a dynamic variable for the callback because we need it to be a delegate type
        CurrentDomain.AssemblyResolve += asmResolveCB;
        TinyBot_asmBuf = CurrentDomain.CreateInstanceAndUnwrap("B", "<TINYBOTCLASS>");
        CurrentDomain.AssemblyResolve -= asmResolveCB;
    }

    public Move Think(Board board, Timer timer) => TinyBot_asmBuf.Think(board, timer);
}