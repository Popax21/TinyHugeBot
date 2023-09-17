using ChessChallenge.API;
using static System.AppDomain;

class MyBot : IChessBot {
    //TinyBot_asmBuf either holds the TinyBot IChessBot instance, or the assembly buffer during decoding
    //We declare all our other variables here as well to save tokens later
    dynamic TinyBot_asmBuf = new byte[<TINYASMSIZE>], bits;
    int asmDataBufOff, accum, remVals, parity;

    public MyBot() {
        //Decode the assembly
        foreach(decimal dec in new[] {
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> BEGIN ENCODED ASSEMBLY <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
            <TINYASMENCDAT>
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>  END ENCODED ASSEMBLY  <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
        }) {
            //Get the bits of the decimal
            bits = decimal.GetBits(dec);

            //Check if we reached the end of the block
            if(remVals-- == 0) {
                asmDataBufOff += bits[0];
                remVals = bits[1];
                continue;
            }

            //Add the 96 bit integer to the buffer
            for(int i = 0; i < 96; i += 8)
                TinyBot_asmBuf[asmDataBufOff++] = (byte) (bits[i / 32] >> i);

            //Accumulate two 4 bit scales, then add to the buffer
            accum <<= 4;
            TinyBot_asmBuf[asmDataBufOff++] = (byte) (accum |= bits[3] >> 16);
            asmDataBufOff -= parity ^= 1;
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