using ChessChallenge.API;
using static System.AppDomain;

class MyBot : IChessBot {
    //TinyBot_asmBuf either holds the TinyBot IChessBot instance, or the assembly buffer during decoding
    dynamic TinyBot_asmBuf = new byte[<TINYASMSIZE>];
    int asmBufOff;

    public MyBot() {
        //Decode the assembly
        //The assembly is encoded in a semi-RLE-like format
        //There are two types of tokens:
        // - scaling factors 0: regular tokens
        //   - carry 12 bytes of info through the decimal integer number, which are copied to the assembly buffer
        // - scaling factor 1: skip tokens
        //   - lowest byte of the decimal integer number: the skip amount
        //   - the remaining 11 integer number bytes are copied to the assembly buffer
        // - the sign bit is unused as a minus sign requires an extra token
        foreach(decimal dec in new[] {
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> BEGIN ENCODED ASSEMBLY <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
            <TINYASMENCDAT>
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>  END ENCODED ASSEMBLY  <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
        }) {
            //Get the bits of the decimal
            var bits = decimal.GetBits(dec);

            //Skip forward if the highest scalar bit is set
            dynamic idx = bits[3] >> 16; //1 for skip tokens, 0 otherwise
            if(idx != 0) asmBufOff += (byte) bits[0];

            //Add the 88/96 bits of the integer number to the buffer
            while(idx < 12) TinyBot_asmBuf[asmBufOff++] = (byte) (bits[idx / 4] >> idx++ * 8);
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