using System;
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
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE;
using AsmResolver.PE.DotNet.Builder;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.File;
using MethodAttributes = AsmResolver.PE.DotNet.Metadata.Tables.Rows.MethodAttributes;

if(args.Length < 2) throw new ArgumentException("Usage: <huge bot DLL> <tiny bot DLL> [tiny bot CS] [--debug]");

bool DEBUG = args.Length >= 4 && args[3].Equals("--debug", StringComparison.InvariantCultureIgnoreCase);

string asmPath, botClass;
if(!DEBUG) {
    //Read the huge bot DLL
    ModuleDefinition botMod = ModuleDefinition.FromFile(args[0], new ModuleReaderParameters(AppDomain.CurrentDomain.BaseDirectory));
    if(botMod.Assembly == null) throw new Exception("No assembly in huge bot DLL!");

    TypeDefinition botType = botMod.TopLevelTypes.First(t => t.FullName == "MyBot");
    TypeDefinition? privImplType = botMod.TopLevelTypes.FirstOrDefault(t => t.FullName == "<PrivateImplementationDetails>");

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
    Dictionary<IMemberDescriptor, string> origNames = new Dictionary<IMemberDescriptor, string>();
    string GetOrigName(IMemberDescriptor descr) => origNames.GetValueOrDefault(descr, descr.Name!);

    bool ShouldTinyfy(TypeDefinition type) {
        if(type == botType || type == privImplType || (type.Namespace?.Value?.StartsWith("HugeBot") ?? false)) return true;
        if(origNames.ContainsKey(type)) return true;
        if(type.DeclaringType != null) return ShouldTinyfy(type.DeclaringType);
        return false;
    }

    TypeSignature RelinkType(TypeSignature typeSig) {
        if(!(typeSig is TypeDefOrRefSignature)) return typeSig;

        //Handle bot types
        if(typeSig.Resolve() is TypeDefinition typeDef && ShouldTinyfy(typeDef)) {
            //Inline enum types
            if(typeDef.IsEnum) return botMod.CorLibTypeFactory.Int32;
        }

        return typeSig;
    }

    bool TinyfyType(TypeDefinition type, ref char nextName, bool rename = true) {
        //Enum types are inlined
        if(type.IsEnum) return false;

        //Clear attributes
        type.CustomAttributes.Clear();
        foreach(MethodDefinition meth in type.Methods) {
            meth.CustomAttributes.Clear();
            foreach(ParameterDefinition param in meth.ParameterDefinitions) param.CustomAttributes.Clear();
        }
        foreach(FieldDefinition field in type.Fields) field.CustomAttributes.Clear();

        //Trim out constants
        foreach(FieldDefinition field in type.Fields.ToArray()) {
            if(field.Constant != null) type.Fields.Remove(field);
        }

        //Trim out parameter names
        foreach(MethodDefinition meth in type.Methods) {
            foreach(ParameterDefinition param in meth.ParameterDefinitions) param.Name = null;
        }

        //Trim out to-be-inlined methods
        foreach(MethodDefinition meth in type.Methods.ToArray()) {
            if(meth.Name!.ToString().EndsWith("_I")) type.Methods.Remove(meth);
        }

        //Trim out properties (the underlying getter/setter methods are kept, but we don't need the metadata)
        type.Properties.Clear();

        //Relink type references
        foreach(MethodDefinition meth in type.Methods) {
            foreach(Parameter param in meth.Parameters) param.ParameterType = RelinkType(param.ParameterType); 
        }
        foreach(FieldDefinition field in type.Fields) {
            if(field.Signature is FieldSignature fieldSig) fieldSig.FieldType = RelinkType(fieldSig.FieldType);
        }

        //Tiny-fy and relink method bodies
        foreach(MethodDefinition meth in type.Methods) {
            if(meth.CilMethodBody != null) {
                InlineMethodCalls(meth.CilMethodBody);
                RelinkMethodBody(meth.CilMethodBody);
            }
        }

        //Tiny-fy names
        if(rename) {
            origNames.Add(type, type.Name!);
            type.Namespace = null;
            type.Name = (nextName++).ToString();
        }

        HashSet<string> ifaceNames = type.Interfaces.SelectMany(intf => intf.Interface!.Resolve()!.Methods.Select(m => m.Name!.Value)).ToHashSet();
        foreach(MethodDefinition meth in type.Methods) {
            origNames.Add(meth, meth.Name!);
            if(!meth.IsConstructor && meth.DeclaringType == type && !ifaceNames.Contains(meth.Name!.Value)) meth.Name = null;
        }

        foreach(FieldDefinition field in type.Fields) {
            origNames.Add(field, field.Name!);
            field.Name = null;
        }

        //Tiny-fy nested types
        char nextNestedName = 'a';
        foreach(TypeDefinition nestedType in type.NestedTypes.ToArray()) {
            if(!TinyfyType(nestedType, ref nextNestedName)) type.NestedTypes.Remove(nestedType);
        }

        return true;
    }

    void InlineMethodCalls(CilMethodBody body) {
        try {
            foreach(CilInstruction instr in body.Instructions.ToArray()) {
                //Check if this is a method call to be inlined
                if(instr.OpCode != CilOpCodes.Call) continue;
                if((instr.Operand as IMethodDescriptor)?.Resolve() is not MethodDefinition inlineMethod) continue;
                if(!GetOrigName(inlineMethod).EndsWith("_I")) continue;
                if(inlineMethod.CilMethodBody is not CilMethodBody inlineBody) continue;

                int idx = body.Instructions.IndexOf(instr);

                //Copy locals
                CilLocalVariable[] newLocals = new CilLocalVariable[inlineBody.LocalVariables.Count];
                for(int i = 0; i < newLocals.Length; i++) body.LocalVariables.Add(newLocals[i] = new CilLocalVariable(inlineBody.LocalVariables[i].VariableType));

                //Parse arguments
                int numArgs = inlineMethod.Signature!.GetTotalParameterCount();
                CilInstruction[][] argInstrs = new CilInstruction[numArgs][];

                int curArg = numArgs - 1, stackDepth = 1;
                List<CilInstruction> curArgInstrs = new List<CilInstruction>();

                int argsEndIdx = idx-1;
                for(; curArg >= 0; argsEndIdx--) {
                    CilInstruction argInstr = body.Instructions[argsEndIdx];
                    curArgInstrs.Add(argInstr);

                    if(argInstr.OpCode.FlowControl is not CilFlowControl.Next and not CilFlowControl.Meta and not CilFlowControl.Call) throw new Exception($"Invalid inline argument flow control: {argInstr.OpCode.FlowControl} [{argInstr}]");

                    //Handle stack pushes / pops
                    stackDepth -= argInstr.GetStackPushCount();
                    stackDepth += argInstr.GetStackPopCount(body);

                    //Check if we are at the end of this argument's code
                    if(stackDepth <= 0) {
                        curArgInstrs.Reverse();
                        argInstrs[curArg--] = curArgInstrs.ToArray();
                        curArgInstrs.Clear();
                        stackDepth = 1;
                    }
                }

                //Fixup jumps to the inlined method call
                CilInstructionLabel? startLabel = null;
                if(argsEndIdx+1 < idx) {
                    body.Instructions.CalculateOffsets();
                    int argsStartOff = body.Instructions[argsEndIdx+1].Offset, argsEndOff = instr.Offset + instr.Size;

                    foreach(CilInstruction jumpInstr in body.Instructions) {
                        if(jumpInstr.OpCode.OperandType is not CilOperandType.InlineBrTarget and not CilOperandType.ShortInlineBrTarget) continue;

                        //Determine and check the jump target
                        int jumpOff = jumpInstr.Operand switch {
                            ICilLabel label => label.Offset,
                            int off => off,
                            sbyte off => off,
                            _ => throw new Exception($"Invalid jump instruction operand: {jumpInstr}")
                        };

                        if(jumpOff < argsStartOff || jumpOff >= argsEndOff) continue;
                        if(jumpOff != argsStartOff) throw new Exception($"Detected jump into middle of inlined function call: {jumpInstr} [0x{argsStartOff:x} <= 0x{jumpOff:x} <= 0x{argsEndOff:x}]");

                        //Remap the jump
                        jumpInstr.Operand = startLabel ??= new CilInstructionLabel();
                    }
                }

                //Remove original instructions
                body.Instructions.RemoveRange(argsEndIdx+1, idx - argsEndIdx);
                int startIdx = idx = argsEndIdx+1;

                //Give frequently used arguments their own local variables
                int[] numArgUsages = new int[argInstrs.Length];
                foreach(CilInstruction inlineInstr in inlineBody.Instructions) {
                    if(!inlineInstr.IsLdarg()) continue;
                    numArgUsages[inlineInstr.GetParameter(inlineMethod.Parameters).MethodSignatureIndex]++;
                }

                for(int i = 0; i < argInstrs.Length; i++) {
                    //Check if this argument "deserves its own variable"
                    int inlineSize = numArgUsages[i] * argInstrs[i].Sum(instr => instr.Size);
                    int varSize = 4 + argInstrs[i].Sum(instr => instr.Size) + 3 + 3 * numArgUsages[i];
                    if(inlineSize < varSize) continue;

                    //Give the argument its own variable
                    CilLocalVariable argLocal = new CilLocalVariable(inlineMethod.Parameters.GetBySignatureIndex(i).ParameterType);
                    body.LocalVariables.Add(argLocal);

                    foreach(CilInstruction argInstr in argInstrs[i]) body.Instructions.Insert(idx++, new CilInstruction(argInstr.OpCode, argInstr.Operand));
                    body.Instructions.Insert(idx++, CilOpCodes.Stloc, argLocal);

                    argInstrs[i] = new CilInstruction[] { new CilInstruction(CilOpCodes.Ldloc, argLocal) };
                }

                //Inline the inlined method body
                Dictionary<int, CilInstruction> origOffMap = new Dictionary<int, CilInstruction>();
                Dictionary<int, CilInstructionLabel> remapJumpTargets = new Dictionary<int, CilInstructionLabel>();

                CilInstructionLabel? endLabel = null;
                foreach(CilInstruction inlineInstr in inlineBody.Instructions) {
                    int startInstrIdx = idx;

                    switch(inlineInstr!) {
                        case {} when inlineInstr.IsLdarg(): {
                            foreach(CilInstruction argInstr in argInstrs[inlineInstr.GetParameter(inlineMethod.Parameters).MethodSignatureIndex]) body.Instructions.Insert(idx++, new CilInstruction(argInstr.OpCode, argInstr.Operand));
                        } break;

                        case {} when inlineInstr.IsStarg():
                            throw new Exception("Starg is not supported in inlined methods");

                        case {} when inlineInstr.OpCode == CilOpCodes.Ldarga || inlineInstr.OpCode == CilOpCodes.Ldarga_S:
                            throw new Exception("Ldarga is not supported in inlined methods");

                        case {} when inlineInstr.IsLdloc() || inlineInstr.IsStloc() || inlineInstr.OpCode.OperandType is CilOperandType.InlineVar or CilOperandType.ShortInlineVar:
                            //Remap the local
                            body.Instructions.Insert(idx++, 0 switch {
                                {} when inlineInstr.IsLdloc() => CilOpCodes.Ldloc,
                                {} when inlineInstr.IsStloc() => CilOpCodes.Stloc,
                                _ => CilOpCodes.Stloc,
                            }, newLocals[inlineInstr.GetLocalVariable(inlineBody.LocalVariables).Index]);
                            break;

                        case {} when inlineInstr.OpCode.OperandType is CilOperandType.InlineBrTarget or CilOperandType.ShortInlineBrTarget: {
                            //Remap the jump
                            int origJumpOff = inlineInstr.Operand switch {
                                ICilLabel label => label.Offset,
                                int off => off,
                                sbyte off => off,
                                _ => throw new Exception($"Invalid jump instruction operand: {inlineInstr}")
                            };

                            if(!remapJumpTargets.TryGetValue(origJumpOff, out CilInstructionLabel? remapJumplabel)) {
                                remapJumpTargets.Add(origJumpOff, remapJumplabel = new CilInstructionLabel());
                            }
                            body.Instructions.Insert(idx++, inlineInstr.OpCode, remapJumplabel);
                        } break;

                        case {} when inlineInstr.OpCode == CilOpCodes.Ret:
                            //Check if this is the last instruction
                            if(inlineBody.Instructions.Last() == inlineInstr) break;

                            //Insert a jump to the end
                            endLabel ??= new CilInstructionLabel();
                            body.Instructions.Insert(idx++, CilOpCodes.Br, endLabel);
                            break;

                        default:
                            body.Instructions.Insert(idx++, new CilInstruction(inlineInstr.OpCode, inlineInstr.Operand));
                            break;
                    }

                    origOffMap.Add(inlineInstr.Offset, body.Instructions[startInstrIdx]);
                }

                //Remap jumps
                foreach((int origOff, CilInstructionLabel label) in remapJumpTargets) {
                    if(!origOffMap.TryGetValue(origOff, out CilInstruction? targetInstr)) throw new Exception($"Encountered invalid original offset 0x{origOff} when remapping inlined jumps");
                    label.Instruction = targetInstr;
                }

                //Mark the start and end label
                if(startLabel != null) startLabel.Instruction = body.Instructions[startIdx];
                if(endLabel != null) endLabel.Instruction = body.Instructions[idx];
            }

            //Cleanup
            body.Instructions.OptimizeMacros();
            body.VerifyLabels();
            body.ComputeMaxStack();
        } catch {
            Console.WriteLine($"Messed up while inlining calls in '{body.Owner}' - dumping IL:");
            body.Instructions.CalculateOffsets();
            foreach(CilInstruction dumpInstr in body.Instructions) Console.WriteLine(dumpInstr);
            throw;
        }
    }

    void RelinkMethodBody(CilMethodBody body) {
        //Relink locals
        foreach(CilLocalVariable local in body.LocalVariables) local.VariableType = RelinkType(local.VariableType);

        //Relink type references in operands
        foreach(CilInstruction instr in body.Instructions) {
            if((instr.Operand as IMethodDescriptor)?.Signature is MethodSignature methodSig) {
                methodSig.ReturnType = RelinkType(methodSig.ReturnType);
                for(int i = 0; i < methodSig.ParameterTypes.Count; i++) methodSig.ParameterTypes[i] = RelinkType(methodSig.ParameterTypes[i]);
            }
            if((instr.Operand as IFieldDescriptor)?.Signature is FieldSignature fieldSig) fieldSig.FieldType = RelinkType(fieldSig.FieldType);
        }
    }

    TypeDefinition staticType = botMod.GetOrCreateModuleType(); //We need a static type anyway, so why not .-.
    char nextName = 'a';
    foreach(TypeDefinition type in botMod.TopLevelTypes.ToArray()) {
        bool keepType = false;

        if(ShouldTinyfy(type)) {
            //Merge static types (there's no concept of "static classes" at the IL level, so we have to cheat a bit)
            if((type.IsSealed && type.IsAbstract) || type == privImplType) {
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
                //Tinfy other types
                keepType = TinyfyType(type, ref nextName);
            }

            //Enum types are inlined
            if(type.IsEnum) keepType = false;
        }

        //Remove the type if we don't need it
        if(!keepType && type != staticType) botMod.TopLevelTypes.Remove(type);
    }

    if(staticType != null) {
        //Merge static constructors
        List<MethodDefinition> cctors = new List<MethodDefinition>();

        foreach(MethodDefinition method in staticType.Methods) {
            if(!method.IsConstructor) continue;
            method.Name = $"cctor{cctors.Count}_I"; //_I suffix will trigger method inlining
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
        TinyfyType(staticType, ref nextName, rename: false);
    }

    //We need to expose the bot type
    botType.Name = botClass = "B"; 

    //Build the tiny bot DLL by modifying some other parameters
    PEImageBuildResult tinyBotBuildRes = new ManagedPEImageBuilder().CreateImage(botMod);
    IPEImage tinyBotImg = tinyBotBuildRes.ConstructedImage ?? throw new Exception("No tiny bot PEImage was built!");
    tinyBotImg.Resources = null;

    PEFile tinyBot = new ManagedPEFileBuilder().CreateFile(tinyBotImg);
    tinyBot.OptionalHeader.FileAlignment = tinyBot.OptionalHeader.SectionAlignment = 512;

    //Write the tiny bot DLL
    tinyBot.Write(args[1]);

    static long GetDLLSize(string path) => new FileInfo(path).Length;
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

if (args.Length <= 2) return;

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

Console.WriteLine($"Encoded {tinyBotData.Length} bytes into {tinyBotEncDecs.Count} tokens");

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