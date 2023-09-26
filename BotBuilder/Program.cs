using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;

if(args.Length < 2) throw new ArgumentException("Usage: <huge bot DLL> <tiny bot DLL> [tiny bot CS] [--debug]");
string hugeBotDllPath = args[0];
string tinyBotDllPath = args[1];

bool DEBUG = args.Contains("--debug", StringComparer.InvariantCultureIgnoreCase);

string asmPath, botClass;
if(!DEBUG) {
    //Read the huge bot DLL
    ModuleDefinition botMod = ModuleDefinition.FromFile(hugeBotDllPath, new ModuleReaderParameters(AppDomain.CurrentDomain.BaseDirectory));
    TypeDefinition botType = botMod.TopLevelTypes.First(t => t.FullName == "HugeBot.MyBot");

    //Tinyfy and write the tiny bot DLL to disk
    new Tinyfier(botMod).AddExternalReference(botType).TinyfyEverything().WithNamePriority(botType, 1000).Build().Write(tinyBotDllPath);
    botClass = botType.FullName!;

    Console.WriteLine($"Built tiny bot: {hugeBotDllPath} ({new FileInfo(hugeBotDllPath).Length} bytes) -> {tinyBotDllPath} ({new FileInfo(tinyBotDllPath).Length} bytes)");

    //Ensure that the DLL can still be loaded
    try {
        Assembly.LoadFile(Path.GetFullPath(tinyBotDllPath)).GetType(botClass, true);
    } catch(Exception e) {
        throw new Exception("TinyBot DLL verification error!", e);
    }

    asmPath = tinyBotDllPath;
} else {
    //Use HugeBot.dll directly
    asmPath = hugeBotDllPath;
    botClass = "HugeBot.MyBot";
    Console.WriteLine("Skipping tiny bot build as --debug flag was given");
}

if(args.Length <= 2) return;
string encCsPath = args[2];

//Encode the TinyBot DLL
byte[] tinyBotData = File.ReadAllBytes(asmPath);
decimal[] tinyBotEncDecs = new TokenEncoder(tinyBotData).Encode(out int tinyBotBufSize);
Console.WriteLine($"Encoded {tinyBotData.Length} bytes into {tinyBotEncDecs.Length} tokens");

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

File.WriteAllText(encCsPath, launchpad.Replace("<TINYASMENCDAT>", tinyBotEncData.ToString()).Replace("<TINYASMSIZE>", tinyBotBufSize.ToString()).Replace("<TINYBOTCLASS>", botClass));
Console.WriteLine($"Wrote launchpad with encoded bot to '{args[2]}'");