using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;

public partial class Tinyfier {
    private readonly HashSet<MethodDefinition> inlineTargetMethods = new HashSet<MethodDefinition>();
    private readonly HashSet<CilMethodBody> inlineHandledMethods = new HashSet<CilMethodBody>();

    private void InlineMethodCalls() {
        //Discover inline target methods
        foreach(TypeDefinition type in targetTypes) {
            foreach(MethodDefinition method in type.Methods) {
                if(method.CilMethodBody != null && (method.Name?.ToString().EndsWith("_I") ?? false)) inlineTargetMethods.Add(method);
            }
        }
        Log($"Discovered {inlineTargetMethods.Count} inline target methods");


        //Make all members of types with inline target methods at least internal
        //Afterwards, remove inline target methods from their types
        foreach(MethodDefinition inlineTarget in inlineTargetMethods) {
            if(inlineTarget.DeclaringType is not TypeDefinition declType) continue;

            foreach(FieldDefinition field in declType.Fields) {
                if(!field.IsPrivate) continue;
                field.IsPrivate = false;
                field.IsAssembly = true;
            }

            foreach(MethodDefinition method in declType.Methods) {
                if(!method.IsPrivate) continue;
                method.IsPrivate = false;
                method.IsAssembly = true;
            }

            declType.Methods.Remove(inlineTarget);
        }

        //Inline selected method calls in target types
        int numInlinedCalls = 0;
        foreach(TypeDefinition type in targetTypes) {
            foreach(MethodDefinition method in type.Methods) {
                if(method.CilMethodBody != null) numInlinedCalls += InlineMethodCalls(method.CilMethodBody);
            }
        }
        Log($"Inlined {numInlinedCalls} method calls");
    }

    private int InlineMethodCalls(CilMethodBody body) {
        if(!inlineHandledMethods.Add(body)) return 0;

        int numInlinedCalls = 0;

        foreach(CilInstruction instr in body.Instructions.ToArray()) {
            //Check if this is a method call to be inlined
            if(instr.OpCode != CilOpCodes.Call) continue;
            if((instr.Operand as IMethodDescriptor)?.Resolve() is not MethodDefinition inlineMethod) continue;
            if(!inlineTargetMethods.Contains(inlineMethod)) continue;
            if(inlineMethod.CilMethodBody is not CilMethodBody inlineBody) continue;

            //Ensure that the inlined method itself has had its inlines handled (for recursive inlining support)
            InlineMethodCalls(inlineBody);

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

                if(argInstr.OpCode.FlowControl is not CilFlowControl.Next and not CilFlowControl.Meta and not CilFlowControl.Call) {
                    throw new InvalidCilInstructionException($"Invalid inline argument flow control: {argInstr.OpCode.FlowControl} [{argInstr}]");
                }

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
                        _ => throw new InvalidCilInstructionException($"Invalid jump instruction operand: {jumpInstr}")
                    };

                    if(jumpOff < argsStartOff || jumpOff >= argsEndOff) continue;
                    if(jumpOff != argsStartOff) {
                        throw new InvalidCilInstructionException($"Detected jump into middle of inlined function call: {jumpInstr} [0x{argsStartOff:x} <= 0x{jumpOff:x} <= 0x{argsEndOff:x}]");
                    }

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
                        foreach(CilInstruction argInstr in argInstrs[inlineInstr.GetParameter(inlineMethod.Parameters).MethodSignatureIndex]) {
                            body.Instructions.Insert(idx++, new CilInstruction(argInstr.OpCode, argInstr.Operand));
                        }
                    } break;

                    case {} when inlineInstr.IsStarg():
                        throw new InvalidCilInstructionException("Starg is not supported in inlined methods");

                    case {} when inlineInstr.OpCode == CilOpCodes.Ldarga || inlineInstr.OpCode == CilOpCodes.Ldarga_S: {
                        //Check if the argument instructions consist of just an Ldloc
                        CilInstruction[] instrs = argInstrs[inlineInstr.GetParameter(inlineMethod.Parameters).MethodSignatureIndex];
                        if(instrs.Length != 1 || !instrs[0].IsLdloc()) throw new InvalidCilInstructionException("Ldarga is not supported for non-trivial arguments in inlined methods");

                        body.Instructions.Insert(idx++, CilOpCodes.Ldloca, instrs[0].GetLocalVariable(body.LocalVariables));
                    } break;

                    case {} when inlineInstr.IsLdloc() || inlineInstr.IsStloc() || inlineInstr.OpCode.OperandType is CilOperandType.InlineVar or CilOperandType.ShortInlineVar:
                        //Remap the local
                        body.Instructions.Insert(idx++, 0 switch {
                            {} when inlineInstr.IsLdloc() => CilOpCodes.Ldloc,
                            {} when inlineInstr.IsStloc() => CilOpCodes.Stloc,
                            _ => inlineInstr.OpCode,
                        }, newLocals[inlineInstr.GetLocalVariable(inlineBody.LocalVariables).Index]);
                        break;

                    case {} when inlineInstr.OpCode.OperandType is CilOperandType.InlineBrTarget or CilOperandType.ShortInlineBrTarget: {
                        //Remap the jump
                        int origJumpOff = inlineInstr.Operand switch {
                            ICilLabel label => label.Offset,
                            int off => off,
                            sbyte off => off,
                            _ => throw new InvalidCilInstructionException($"Invalid jump instruction operand: {inlineInstr}")
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
                if(!origOffMap.TryGetValue(origOff, out CilInstruction? targetInstr)) {
                    throw new InvalidCilInstructionException($"Encountered invalid original offset 0x{origOff} when remapping inlined jumps");
                }

                label.Instruction = targetInstr;
            }

            //Mark the start and end label
            if(startLabel != null) startLabel.Instruction = body.Instructions[startIdx];
            if(endLabel != null) endLabel.Instruction = body.Instructions[idx];

            //Cleanup
            body.Instructions.CalculateOffsets();
            body.VerifyLabels();
            body.ComputeMaxStack();

            numInlinedCalls++;
        }

        return numInlinedCalls;
    }
}