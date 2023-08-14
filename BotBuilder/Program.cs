using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Collections;
using AsmResolver.DotNet.Serialized;
using AsmResolver.PE;
using AsmResolver.PE.DotNet.Builder;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;

if(args.Length < 2) throw new ArgumentException("Usage: <huge bot DLL> <tiny bot DLL> [tiny bot CS] [--debug]");

bool DEBUG = args.Length >= 4 && args[3].Equals("--debug", StringComparison.InvariantCultureIgnoreCase);

string asmPath, botClass;
if(!DEBUG) {
    //Read the huge bot DLL
    ModuleDefinition botMod = ModuleDefinition.FromFile(args[0], new ModuleReaderParameters(AppDomain.CurrentDomain.BaseDirectory));
    if(botMod.Assembly == null) throw new Exception("No assembly in huge bot DLL!");

    //Tiny-fy module and assembly metadata
    botMod.Name = null;
    botMod.DebugData.Clear();
    botMod.CustomAttributes.Clear();
    botMod.ExportedTypes.Clear();
    botMod.FileReferences.Clear();

    foreach(AssemblyReference asmRef in botMod.AssemblyReferences) {
        asmRef.Culture = null;
        asmRef.HashValue = null;
        asmRef.PublicKeyOrToken = null;
    }

    botMod.Assembly.Name = "B"; //We need an assembly name
    botMod.Assembly.PublicKey = null;
    botMod.Assembly.CustomAttributes.Clear();

    //Tiny-fy types
    TypeDefinition botType = botMod.TopLevelTypes.First(t => t.FullName == "MyBot");
    TypeDefinition? staticType = null;

    bool TinyfyType(TypeDefinition type, ref char nextName) {
        //Tiny-fy the name
        type.Namespace = null;
        type.Name = (nextName++).ToString();

        //Clear attributes
        type.CustomAttributes.Clear();
        foreach(MethodDefinition meth in type.Methods) meth.CustomAttributes.Clear();
        foreach(FieldDefinition field in type.Fields) field.CustomAttributes.Clear();

        //Trim out constants
        foreach(FieldDefinition field in type.Fields.ToArray()) {
            if(field.Constant != null) type.Fields.Remove(field);
        }
        
        //Trim out parameter names
        foreach(MethodDefinition meth in type.Methods.ToArray()) {
            foreach(Parameter param in meth.Parameters) {
                if(param.Definition != null) param.Definition!.Name = null;
            }
        }

        //Tiny-fy member names
        HashSet<string> ifaceNames = type.Interfaces.SelectMany(intf => intf.Interface!.Resolve()!.Methods.Select(m => m.Name!.Value)).ToHashSet();
        foreach(MethodDefinition meth in type.Methods) {
            if(!meth.IsConstructor && meth.DeclaringType == type && !ifaceNames.Contains(meth.Name!.Value)) {
                meth.Name = null;
            }
        }

        foreach(FieldDefinition field in type.Fields) field.Name = null;
        
        char nextNestedName = 'a';
        foreach(TypeDefinition nestedType in type.NestedTypes.ToArray()) {
            if(!TinyfyType(nestedType, ref nextNestedName)) type.NestedTypes.Remove(nestedType);
        }

        // If the type is empty, remove it
        return type.Fields.Count > 0 || type.Methods.Count > 0 || type.Properties.Count > 0 || type.NestedTypes.Count > 0;
    }

    char nextName = 'a';
    foreach(TypeDefinition type in botMod.TopLevelTypes.ToArray()) {
        bool keepType = false;

        if(type == botType || (type.Namespace?.Value?.StartsWith("HugeBot") ?? false)) {
            // Merge static types (there's no concept of "static classes" at the IL level, so we have to cheat a bit)
            if (type.IsSealed) {
                if (staticType != null) {
                    // Merge the types by transfering over fields, methods and properties
                    foreach(FieldDefinition field in type.Fields) staticType.Fields.Add(field);
                    foreach(MethodDefinition method in type.Methods) staticType.Methods.Add(method);
                    foreach(PropertyDefinition prop in type.Properties) staticType.Properties.Add(prop);
                } else {
                    staticType = type;
                    keepType = true;
                }
            } else
                // Tinfy other types
                keepType = TinyfyType(type, ref nextName);
        }

        // Remove the type if we don't need it
        if(!keepType) botMod.TopLevelTypes.Remove(type);
    }

    if(staticType != null) TinyfyType(staticType, ref nextName);

    //Fixup the module
    botMod.GetOrCreateModuleType();
    botType.Name = botClass = "B"; //We need to expose the bot type

    //Build the tiny bot DLL by modifying some other parameters
    PEImageBuildResult tinyBotBuildRes = new ManagedPEImageBuilder().CreateImage(botMod);
    IPEImage tinyBotImg = tinyBotBuildRes.ConstructedImage ?? throw new Exception("No tiny bot PEImage was built!");
    tinyBotImg.PEKind = OptionalHeaderMagic.PE64;
    tinyBotImg.MachineType = MachineType.Amd64;
    tinyBotImg.Resources = null;

    PEFile tinyBot = new ManagedPEFileBuilder().CreateFile(tinyBotImg);
    tinyBot.OptionalHeader.FileAlignment = tinyBot.OptionalHeader.SectionAlignment = 512;

    //Write the tiny bot DLL
    tinyBot.Write(args[1]);

    static long GetDLLSize(string path) {
        //Don't count trailing zeros
        byte[] contents = File.ReadAllBytes(path);
        long size = contents.Length;
        while(size > 0 && contents[size-1] == 0) size--;
        return size;
    }
    Console.WriteLine($"Built tiny bot: {args[0]} ({GetDLLSize(args[0])} bytes) -> {args[1]} ({GetDLLSize(args[1])} bytes)");

    //Ensure that the DLL can still be loaded
    try {
        Assembly.LoadFile(Path.GetFullPath(args[1])).GetType("B", true);
    } catch(Exception e) {
        throw new Exception("TinyBot DLL verification error!", e);
    }

    asmPath = args[1];
} else {
    asmPath = args[0];
    botClass = "MyBot";
    Console.WriteLine("Skipping tiny bot build as --debug flag was given");
}

if(args.Length <= 2) return;

//Encode the TinyBot DLL
byte[] tinyBotData = File.ReadAllBytes(asmPath);
byte GetTinyBotNibble(long idx) => (byte) (idx < tinyBotData.Length*2 ? (tinyBotData[idx / 2] >> (int) (4 * (idx & 1))) & 0xf : 0);
byte GetTinyBotByte(long nibbleIdx) => (byte) (GetTinyBotNibble(nibbleIdx) + (GetTinyBotNibble(nibbleIdx+1) << 4));
int GetTinyBotInt(long nibbleIdx) => GetTinyBotByte(nibbleIdx) + (GetTinyBotByte(nibbleIdx+2) << 8) + (GetTinyBotByte(nibbleIdx+4) << 16) + (GetTinyBotByte(nibbleIdx+6) << 24);

long tinyBotNonZeroNibbles = tinyBotData.Length*2;
while(tinyBotNonZeroNibbles > 0 && GetTinyBotNibble(tinyBotNonZeroNibbles-1) == 0) tinyBotNonZeroNibbles--;

StringBuilder tinyBotEncData = new StringBuilder();
for(long i = 0; i < tinyBotNonZeroNibbles; i += 50) {
    decimal decA = new decimal(GetTinyBotInt(i+00), GetTinyBotInt(i+08), GetTinyBotInt(i+16), false, GetTinyBotNibble(i+49));
    decimal decB = new decimal(GetTinyBotInt(i+24), GetTinyBotInt(i+32), GetTinyBotInt(i+40), false, GetTinyBotNibble(i+48));

    if(tinyBotEncData.Length > 0) tinyBotEncData.Append(',');
    tinyBotEncData.Append(decA.ToString(CultureInfo.InvariantCulture));
    tinyBotEncData.Append('M');
    tinyBotEncData.Append(',');
    tinyBotEncData.Append(decB.ToString(CultureInfo.InvariantCulture));
    tinyBotEncData.Append('M');
}

long tinyBotBufSize = tinyBotData.Length*8;
if(tinyBotBufSize % 200 != 0) tinyBotBufSize += 200 - (tinyBotBufSize % 200);
if(tinyBotBufSize % 8 != 0) tinyBotBufSize += 8 - (tinyBotBufSize % 8);
tinyBotBufSize /= 8;

//Format the launchpad
using Stream launchPadStream = Assembly.GetEntryAssembly()!.GetManifestResourceStream("launchpad") ?? throw new Exception("Couldn't open launchpad resource!");
using StreamReader launchPadReader = new StreamReader(launchPadStream);
string launchpad = launchPadReader.ReadToEnd();

File.WriteAllText(args[2], launchpad.Replace("<TINYASMENCDAT>", tinyBotEncData.ToString()).Replace("<TINYASMSIZE>", tinyBotBufSize.ToString()).Replace("<TINYBOTCLASS>", botClass));
Console.WriteLine($"Wrote launchpad with encoded bot to '{args[2]}'");