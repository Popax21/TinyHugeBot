using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Serialized;
using AsmResolver.PE;
using AsmResolver.PE.DotNet.Builder;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;

if (args.Length < 2) throw new ArgumentException("Usage: <huge bot DLL> <tiny bot DLL>");

//Read the huge bot DLL
ModuleDefinition botMod = ModuleDefinition.FromFile(args[0], new ModuleReaderParameters(AppDomain.CurrentDomain.BaseDirectory));
if(botMod.Assembly == null) throw new Exception("No assembly in huge bot DLL!");

//Tiny-fy module and assembly metadata
botMod.Name = "B";
botMod.DebugData.Clear();
botMod.CustomAttributes.Clear();
botMod.ExportedTypes.Clear();
botMod.FileReferences.Clear();

foreach(AssemblyReference asmRef in botMod.AssemblyReferences) {
    asmRef.Culture = null;
    asmRef.HashValue = null;
    asmRef.PublicKeyOrToken = null;
}

botMod.Assembly.Name = "B";
botMod.Assembly.PublicKey = null;
botMod.Assembly.CustomAttributes.Clear();

//Tiny-fy types
TypeDefinition botType = botMod.TopLevelTypes.First(t => t.FullName == "HugeBot.ChessBot");

void TinyfyType(TypeDefinition type, ref char nextName) {
    //Tiny-fy the name
    type.Namespace = null;
    type.Name = (nextName++).ToString();

    //Clear attributes
    type.CustomAttributes.Clear();

    //Tiny-fy members
    HashSet<string> ifaceNames = type.Interfaces.SelectMany(intf => intf.Interface!.Resolve()!.Methods.Select(m => m.Name!.Value)).ToHashSet();
    
    char nextMemberName = 'A';
    foreach(FieldDefinition field in type.Fields) field.Name = (nextMemberName++).ToString();
    foreach(MethodDefinition meth in type.Methods) {
        if(!meth.IsConstructor && meth.DeclaringType == type && !ifaceNames.Contains(meth.Name!.Value)) {
            meth.Name = (nextMemberName++).ToString();
        }
    }
    foreach(TypeDefinition nestedType in type.NestedTypes) TinyfyType(nestedType, ref nextMemberName);
}

char nextName = 'a';
foreach(TypeDefinition type in botMod.TopLevelTypes.ToArray()) {
    if(type.Namespace?.Value?.StartsWith("HugeBot") ?? false) {
        TinyfyType(type, ref nextName);
    } else {
        botMod.TopLevelTypes.Remove(type);
    }
}

botMod.GetOrCreateModuleType();

//Only expose the bot type
botType.Name = "B";

//Build the tiny bot DLL by modifying some other parameters
PEImageBuildResult tinyBotBuildRes = new ManagedPEImageBuilder().CreateImage(botMod);
IPEImage tinyBotImg = tinyBotBuildRes.ConstructedImage ?? throw new Exception("No tiny bot PEImage was built!");
tinyBotImg.PEKind = OptionalHeaderMagic.PE64;
tinyBotImg.MachineType = MachineType.Amd64;

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