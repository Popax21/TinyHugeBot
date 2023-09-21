using System.Collections.Generic;

public class TokenEncoder {
    public readonly byte[] Data;
    public TokenEncoder(byte[] data) => Data = data;

    private byte GetTinyBotByte(int idx) => idx < Data.Length ? Data[idx] : (byte) 0;
    private ushort GetTinyBotShort(int idx) => (ushort) (GetTinyBotByte(idx+0) + (GetTinyBotByte(idx+1) << 8));
    private int GetTinyBotInt(int idx) => GetTinyBotByte(idx+0) + (GetTinyBotByte(idx+1) << 8) + (GetTinyBotByte(idx+2) << 16) + (GetTinyBotByte(idx+3) << 24);

    public decimal[] Encode(out int bufSize) {
        List<decimal> encDecs = new List<decimal>();

        int curBufOff = 0;
        bool scalarParity = false;
        int lastScalarAccumToken = -1;
        while(curBufOff < Data.Length) {
            //Determine the number of zero bytes
            int skipAmount = 0;
            while(curBufOff+skipAmount < Data.Length && Data[curBufOff+skipAmount] == 0) skipAmount++;

            if(curBufOff+skipAmount >= Data.Length) break;

            //Check if it is more efficient to skip forward
            if(skipAmount <= (scalarParity ? 2 : 1)) {
                //Handle the scalar accumulator
                byte scalarNibble = 0;
                if(scalarParity) {
                    byte extraByte = GetTinyBotByte(curBufOff++);
                    scalarNibble = (byte) (extraByte & 0xf);

                    int[] prevDecBits = decimal.GetBits(encDecs[lastScalarAccumToken]);
                    prevDecBits[3] |= (extraByte >> 4) << 16;
                    encDecs[lastScalarAccumToken] = new decimal(prevDecBits);
                }
                scalarParity = !scalarParity;
                lastScalarAccumToken = encDecs.Count;

                //Encode a regular token
                encDecs.Add(new decimal(GetTinyBotInt(curBufOff + 0), GetTinyBotInt(curBufOff + 4), GetTinyBotInt(curBufOff + 8), false, scalarNibble));
                curBufOff += 12;
            } else {
                //Encode a skip token
                if(skipAmount > byte.MaxValue) skipAmount = byte.MaxValue;
                curBufOff += skipAmount;

                encDecs.Add(new decimal(skipAmount | GetTinyBotByte(curBufOff + 0) << 8 | GetTinyBotShort(curBufOff + 1) << 16, GetTinyBotInt(curBufOff + 3), GetTinyBotInt(curBufOff + 7), false, 16));
                curBufOff += 11;
            }
        }

        bufSize = int.Max(curBufOff, Data.Length);
        return encDecs.ToArray();
    }
}