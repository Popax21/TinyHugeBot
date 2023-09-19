using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;

if(args.Length < 2) throw new ArgumentException("Usage: <huge bot DLL> <tiny bot DLL> [tiny bot CS] [--debug]");

bool DEBUG = args.Length >= 4 && args[3].Equals("--debug", StringComparison.InvariantCultureIgnoreCase);

string asmPath, botClass;
if(!DEBUG) {
    //Read the huge bot DLL
    ModuleDefinition botMod = ModuleDefinition.FromFile(args[0], new ModuleReaderParameters(AppDomain.CurrentDomain.BaseDirectory));
    TypeDefinition botType = botMod.TopLevelTypes.First(t => t.FullName == "MyBot");

    //Tinyfy and write the tiny bot DLL to disk
    new Tinyfier(botMod).AddExternalReference(botType).TinyfyEverything().WithNamePriority(botType, 1000).Build().Write(args[1]);
    botClass = botType.FullName!;

    static long GetDLLSize(string path) => new FileInfo(path).Length;
    Console.WriteLine($"Built tiny bot: {args[0]} ({GetDLLSize(args[0])} bytes) -> {args[1]} ({GetDLLSize(args[1])} bytes)");

    //Ensure that the DLL can still be loaded
    try {
        Assembly.LoadFile(Path.GetFullPath(args[1])).GetType(botClass, true);
    } catch(Exception e) {
        throw new Exception("TinyBot DLL verification error!", e);
    }

    asmPath = args[1];
} else {
    //Use HugeBot.dll directly
    asmPath = args[0];
    botClass = "MyBot";
    Console.WriteLine("Skipping tiny bot build as --debug flag was given");
}

if (args.Length <= 2) return;

//Encode the TinyBot DLL
byte[] tinyBotData = File.ReadAllBytes(asmPath);
byte GetTinyBotByte(int idx) => idx < tinyBotData.Length ? tinyBotData[idx] : (byte) 0;
ushort GetTinyBotShort(int idx) => (ushort) (GetTinyBotByte(idx+0) + (GetTinyBotByte(idx+1) << 8));
int GetTinyBotInt(int idx) => GetTinyBotByte(idx+0) + (GetTinyBotByte(idx+1) << 8) + (GetTinyBotByte(idx+2) << 16) + (GetTinyBotByte(idx+3) << 24);

List<decimal> tinyBotEncDecs = new List<decimal>();

int curBufOff = 0;
bool scalarParity = false;
int lastScalarAccumToken = -1;
while(curBufOff < tinyBotData.Length) {
    //Determine the number of zero bytes
    int skipAmount = 0;
    while(curBufOff+skipAmount < tinyBotData.Length && tinyBotData[curBufOff+skipAmount] == 0) skipAmount++;

    if(curBufOff+skipAmount >= tinyBotData.Length) break;

    //Check if it is more efficient to skip forward
    if(skipAmount <= (scalarParity ? 2 : 1)) {
        //Handle the scalar accumulator
        byte scalarNibble = 0;
        if(scalarParity) {
            byte extraByte = GetTinyBotByte(curBufOff++);
            scalarNibble = (byte) (extraByte & 0xf);

            int[] prevDecBits = decimal.GetBits(tinyBotEncDecs[lastScalarAccumToken]);
            prevDecBits[3] |= (extraByte >> 4) << 16;
            tinyBotEncDecs[lastScalarAccumToken] = new decimal(prevDecBits);
        }
        scalarParity = !scalarParity;
        lastScalarAccumToken = tinyBotEncDecs.Count;

        //Encode a regular token
        tinyBotEncDecs.Add(new decimal(GetTinyBotInt(curBufOff + 0), GetTinyBotInt(curBufOff + 4), GetTinyBotInt(curBufOff + 8), false, scalarNibble));
        curBufOff += 12;
    } else {
        //Encode a skip token
        if(skipAmount > byte.MaxValue) skipAmount = byte.MaxValue;
        curBufOff += skipAmount;

        tinyBotEncDecs.Add(new decimal(skipAmount | GetTinyBotByte(curBufOff + 0) << 8 | GetTinyBotShort(curBufOff + 1) << 16, GetTinyBotInt(curBufOff + 3), GetTinyBotInt(curBufOff + 7), false, 16));
        curBufOff += 11;
    }
}

Console.WriteLine($"Encoded {tinyBotData.Length} bytes into {tinyBotEncDecs.Count} tokens");

int tinyBotBufSize = int.Max(curBufOff, tinyBotData.Length);
StringBuilder tinyBotEncData = new StringBuilder();
foreach(decimal dec in tinyBotEncDecs) {
    if(tinyBotEncData.Length > 0) tinyBotEncData.Append(',');
    tinyBotEncData.Append(dec.ToString(CultureInfo.InvariantCulture));
    tinyBotEncData.Append('M');
}

//Format the launchpad
using Stream launchPadStream = Assembly.GetEntryAssembly()!.GetManifestResourceStream("launchpad") ?? throw new Exception("Couldn't open launchpad resource!");
using StreamReader launchPadReader = new StreamReader(launchPadStream);
string launchpad = launchPadReader.ReadToEnd();

File.WriteAllText(args[2], launchpad.Replace("<TINYASMENCDAT>", tinyBotEncData.ToString()).Replace("<TINYASMSIZE>", tinyBotBufSize.ToString()).Replace("<TINYBOTCLASS>", botClass));
Console.WriteLine($"Wrote launchpad with encoded bot to '{args[2]}'");