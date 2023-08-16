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
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Collections;
using AsmResolver.DotNet.Serialized;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE;
using AsmResolver.PE.DotNet.Builder;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;
using MethodAttributes = AsmResolver.PE.DotNet.Metadata.Tables.Rows.MethodAttributes;

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
    void TinyfyType(TypeDefinition type, ref char nextName) {
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
            if(!meth.IsConstructor && meth.DeclaringType == type && !ifaceNames.Contains(meth.Name!.Value)) meth.Name = null;
        }

        foreach(FieldDefinition field in type.Fields) field.Name = null;
        
        char nextNestedName = 'a';
        foreach(TypeDefinition nestedType in type.NestedTypes.ToArray()) TinyfyType(nestedType, ref nextNestedName);
    }

    TypeDefinition botType = botMod.TopLevelTypes.First(t => t.FullName == "MyBot");
    TypeDefinition? privImplType = botMod.TopLevelTypes.FirstOrDefault(t => t.FullName == "<PrivateImplementationDetails>");
    TypeDefinition? staticType = null;

    char nextName = 'a';
    foreach(TypeDefinition type in botMod.TopLevelTypes.ToArray()) {
        bool keepType = false;

        if(type == botType || type == privImplType || (type.Namespace?.Value?.StartsWith("HugeBot") ?? false)) {
            //Merge static types (there's no concept of "static classes" at the IL level, so we have to cheat a bit)
            if((type.IsSealed && type.IsAbstract) || type == privImplType) {
                if(staticType != null) {
                    //Merge the types by transfering over fields, methods and properties
                    static T[] StealElements<T>(IList<T> list) {
                        T[] elems = list.ToArray();
                        list.Clear();
                        return elems;
                    }
                    foreach(FieldDefinition field in StealElements(type.Fields)) staticType.Fields.Add(field);
                    foreach(MethodDefinition method in StealElements(type.Methods)) staticType.Methods.Add(method);
                    foreach(PropertyDefinition prop in StealElements(type.Properties)) staticType.Properties.Add(prop);
                    foreach(TypeDefinition nestedType in StealElements(type.NestedTypes)) staticType.NestedTypes.Add(nestedType);
                } else {
                    staticType = type;
                    keepType = true;
                }
            } else {
                //Tinfy other types
                TinyfyType(type, ref nextName);
                keepType = true;
            }
        }

        //Remove the type if we don't need it
        if(!keepType) botMod.TopLevelTypes.Remove(type);
    }

    if(staticType != null) {
        //Merge static constructors
        List<MethodDefinition> cctors = new List<MethodDefinition>();

        foreach(MethodDefinition method in staticType.Methods) {
            if(!method.IsConstructor) continue;
            method.Name = $"cctor{cctors.Count}";
            method.IsSpecialName = method.IsRuntimeSpecialName = false;
            cctors.Add(method);
        }

        if(cctors.Count >= 2) {
            MethodDefinition mergedCctor = new MethodDefinition(".cctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RuntimeSpecialName, MethodSignature.CreateStatic(botMod.CorLibTypeFactory.Void));
            mergedCctor.CilMethodBody = new CilMethodBody(mergedCctor);
            
            foreach(MethodDefinition cctor in cctors) {
                mergedCctor.CilMethodBody.Instructions.Add(new CilInstruction(CilOpCodes.Call, cctor));
            }
            mergedCctor.CilMethodBody.Instructions.Add(new CilInstruction(CilOpCodes.Ret));
            
            staticType.Methods.Add(mergedCctor);
        } else if(cctors.Count == 1) {
            MethodDefinition cctor = cctors[0];
            cctor.Name = ".cctor";
            cctor.IsSpecialName = cctor.IsRuntimeSpecialName = true;
        }

        //Tinfy the type
        TinyfyType(staticType, ref nextName);
    }

    //Fixup the module
    botMod.GetOrCreateModuleType();
    botType.Name = botClass = "B"; //We need to expose the bot type

    //Build the tiny bot DLL by modifying some other parameters
    PEImageBuildResult tinyBotBuildRes = new ManagedPEImageBuilder().CreateImage(botMod);
    IPEImage tinyBotImg = tinyBotBuildRes.ConstructedImage ?? throw new Exception("No tiny bot PEImage was built!");
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

List<decimal> tinyBotEncDecs = new List<decimal>();

int curBufOff = 0;
int headerDecIdx = -1;
void EndHeader() {
    if(headerDecIdx < 0) return;
    int numDecs = tinyBotEncDecs.Count - (headerDecIdx+1);

    int[] headerDecBits = decimal.GetBits(tinyBotEncDecs[headerDecIdx]);
    headerDecBits[1] = numDecs;
    tinyBotEncDecs[headerDecIdx] = new decimal(headerDecBits);

    headerDecIdx = -1;
}

for(int i = 0; i < tinyBotData.Length;) {
    //Determine the number of zero bytes
    int numZeroBytes = 0;
    while(i+numZeroBytes < tinyBotData.Length && tinyBotData[i+numZeroBytes] == 0) numZeroBytes++;

    //Check if it would be more efficient to start a new block
    if(numZeroBytes > 12) {
        EndHeader();
        i += numZeroBytes;
    }
    if(i >= tinyBotData.Length) break;

    //Start a new header if we don't have one
    if(headerDecIdx < 0) {
        ushort skip = (ushort) (i - curBufOff);
        curBufOff = i;

        headerDecIdx = tinyBotEncDecs.Count;
        tinyBotEncDecs.Add(new decimal(skip, 0, 0, false, 0));
    }

    //Encode the data block
    tinyBotEncDecs.Add(new decimal(GetTinyBotInt(2*i+00), GetTinyBotInt(2*i+08), GetTinyBotInt(2*i+16), false, GetTinyBotNibble(2*i+49)));
    tinyBotEncDecs.Add(new decimal(GetTinyBotInt(2*i+24), GetTinyBotInt(2*i+32), GetTinyBotInt(2*i+40), false, GetTinyBotNibble(2*i+48)));
    curBufOff = i += 25;
}
EndHeader();

StringBuilder tinyBotEncData = new StringBuilder();
foreach(decimal dec in tinyBotEncDecs) {
    if(tinyBotEncData.Length > 0) tinyBotEncData.Append(',');
    tinyBotEncData.Append(dec.ToString(CultureInfo.InvariantCulture));
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