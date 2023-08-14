using System;
using ChessChallenge.API;

public class MyBot : IChessBot {
    private static IChessBot TinyBot;

    public Move Think(Board board, Timer timer) {
        if(TinyBot == null) {
            //Decode the assembly
            var asmDataBuf = new byte[<TINYASMSIZE>];
            int asmDataBufOff = 0, accum = 0, parity = 1;
            foreach(decimal dec in TinyBotAsmEncodedData) {
                var bits = decimal.GetBits(dec);

                //Add the 96 bit integer to the buffer
                for(int i = 0; i < 12; bits[i++ / 4] >>= 8)
                    asmDataBuf[asmDataBufOff++] = (byte) bits[i / 4];

                //Accumulate two 4 bit scales, then add to the buffer
                asmDataBuf[asmDataBufOff] = (byte) (accum = (accum << 4) | (bits[3] >> 16));
                asmDataBufOff += parity ^= 1;
            }

            //Load the tiny bot from the assembly
            //We can't just load it and be done with it, because the byte[] overload doesn't add the assembly to the regular load path
            //As such load it whenever any assembly fails to load >:)
            ResolveEventHandler asmResolveCB = (_, _) => AppDomain.CurrentDomain.Load(asmDataBuf);
            AppDomain.CurrentDomain.AssemblyResolve += asmResolveCB;
            TinyBot = (IChessBot) Activator.CreateInstance("B", "<TINYBOTCLASS>").Unwrap();
            AppDomain.CurrentDomain.AssemblyResolve -= asmResolveCB;
        }
        return TinyBot.Think(board, timer);
    }

//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> BEGIN ENCODED ASSEMBLY <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
    private decimal[] TinyBotAsmEncodedData = {
        <TINYASMENCDAT>
    };
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> END ENCODED ASSEMBLY <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
}