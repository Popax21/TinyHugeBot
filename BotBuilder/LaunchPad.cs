using System;
using ChessChallenge.API;
using static System.AppDomain;

class MyBot : IChessBot {
    //Making this dynamic instead of IChessBot allows for reuse of this variable and removes a cast
    dynamic TinyBot;

    public Move Think(Board board, Timer timer) {
        if(TinyBot == null) {
            //Decode the assembly
            //Here, TinyBot stores the decoded assembly
            TinyBot = new byte[<TINYASMSIZE>];
            int asmDataBufOff = 0, accum = 0, parity = 1, remVals = 0;
            foreach(decimal dec in TinyBotAsmEncodedData) {
                var bits = decimal.GetBits(dec);

                //Check if we reached the end of the block
                if(remVals-- == 0) {
                    asmDataBufOff += bits[0];
                    remVals = bits[1];
                    continue;
                }

                //Add the 96 bit integer to the buffer
                for(int i = 0; i < 12; bits[i++ / 4] >>= 8)
                    TinyBot[asmDataBufOff++] = (byte) bits[i / 4];

                //Accumulate two 4 bit scales, then add to the buffer
                TinyBot[asmDataBufOff] = (byte) (accum = accum << 4 | bits[3] >> 16);
                asmDataBufOff += parity ^= 1;
            }

            //Load the tiny bot from the assembly
            //We can't just load it and be done with it, because the byte[] overload doesn't add the assembly to the regular load path
            //As such load it whenever any assembly fails to load >:)
            ResolveEventHandler asmResolveCB = (_, _) => CurrentDomain.Load(TinyBot);
            CurrentDomain.AssemblyResolve += asmResolveCB;

            //Here, TinyBot switches from storing the assembly to storing the actual chess bot instance
            TinyBot = Activator.CreateInstance("B", "<TINYBOTCLASS>").Unwrap();
            CurrentDomain.AssemblyResolve -= asmResolveCB;
        }
        return TinyBot.Think(board, timer);
    }

//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> BEGIN ENCODED ASSEMBLY <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
    decimal[] TinyBotAsmEncodedData = {
        <TINYASMENCDAT>
    };
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> END ENCODED ASSEMBLY <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
}