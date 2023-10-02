using ChessChallenge.API;
using static System.AppDomain;

class MyBot : IChessBot {
    //TinyBot_asmBuf either holds the TinyBot IChessBot instance, or the assembly buffer during decoding
    dynamic TinyBot_asmBuf = new byte[<TINYASMSIZE>];
    int asmBufOff, scaleParity, asmLoadCount;
    byte scaleAccum;

    public MyBot() {
        //Decode the assembly
        //The assembly is encoded in a semi-RLE-like format
        //There are two types of tokens:
        // - scaling factors 0-15: regular tokens
        //   - the decimal integer number encodes 12 bytes, which are copied to the assembly buffer
        //   - the decimal scaling factor encodes an additional 4 bits - two regular tokens are paired up, and their scaling factors are combined to form an extra byte
        // - scaling factor 16: skip tokens (can skip up to 255 bytes forward to efficiently encode a stretch of zero bytes)
        //   - the lowest byte of the decimal integer number contains the amount of bytes to skip forward by
        //   - the remaining 11 integer number bytes are copied to the assembly buffer
        //   - skip tokens are invisible to the scalar accumulator; they don't contribute their scale value, nor do they affect parity
        // - the sign bit is unused as a minus sign would require an extra token
        foreach(decimal dec in new[] {
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> BEGIN ENCODED ASSEMBLY <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
            <TINYASMENCDAT>
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>  END ENCODED ASSEMBLY  <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
        }) {
            //Get the bits of the decimal
            var bits = decimal.GetBits(dec);

            //Skip forward if the highest scalar bit is set
            dynamic idx = bits[3] >> 16; //16 for skip tokens, <16 otherwise
            if(idx == 16) asmBufOff += (byte) bits[0];
            else {
                //Accumulate two 4 bit scales, then add to the buffer
                //Note that for even parity tokens, the byte we write here is immediately overwritten again 
                scaleAccum <<= 4;
                TinyBot_asmBuf[asmBufOff++] = scaleAccum |= idx;
                asmBufOff -= scaleParity ^= 1;
            }

            //Add the 88/96 bits of the integer number to the buffer
            idx >>= 4; //1 for skip tokens, 0 otherwise
            while(idx < 12) TinyBot_asmBuf[asmBufOff++] = (byte) (bits[idx / 4] >> idx++ * 8);
        }

        //Load the tiny bot from the assembly
        //We can't just load it and be done with it, because the byte[] overload doesn't add the assembly to the regular load path
        //As such load it whenever any assembly fails to load >:)
        //If this is our second load call, assume that the Chess-Challenge assembly has been renamed, and as such return it
        //Note that this only uses allowed APIs, not invoking any method not in an allowed namespace!
        System.ResolveEventHandler asmResolveCB = (_, _) => asmLoadCount++ == 0 ? CurrentDomain.Load(TinyBot_asmBuf) : typeof(Board).Assembly;
        CurrentDomain.AssemblyResolve += asmResolveCB;
        TinyBot_asmBuf = CurrentDomain.CreateInstanceAndUnwrap(ToString(), "<TINYBOTCLASS>");
        CurrentDomain.AssemblyResolve -= asmResolveCB;
    }

    public Move Think(Board board, Timer timer) => TinyBot_asmBuf.Think(board, timer);
}