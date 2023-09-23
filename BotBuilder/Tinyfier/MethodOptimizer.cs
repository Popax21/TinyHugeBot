using System;
using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;

public partial class Tinyfier {
    private void OptimizeMethods() {
        int numMethodBodies = 0;
        foreach(TypeDefinition type in targetTypes) {
            foreach(MethodDefinition method in type.Methods) {
                if(method.CilMethodBody is not { Instructions: CilInstructionCollection instrs } body) continue;
                RemoveUnnecessaryCasts(body, instrs);
                RemoveBaseCtorCalls(body, instrs);
                DetectDeadBranches(body, instrs);
                TrimDeadCode(body, instrs);
                TrimUnnecessaryBranches(body, instrs);
                TrimNOPs(body, instrs);
                instrs.OptimizeMacros();
                numMethodBodies++;
            }
        }
        Log($"Optimized {numMethodBodies} method bodies");
    }

    private void RemoveUnnecessaryCasts(CilMethodBody body, CilInstructionCollection instrs) {
        CilInstruction? lastCast = null;
        int lastCastSize = 0;
        foreach(CilInstruction instr in instrs) {
            int castSize;
            switch(instr) {
                case {} when instr.OpCode == CilOpCodes.Conv_I1:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_I1:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_I1_Un:
                case {} when instr.OpCode == CilOpCodes.Conv_U1:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_U1:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_U1_Un:
                    castSize = 1;
                    break;

                case {} when instr.OpCode == CilOpCodes.Conv_I2:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_I2:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_I2_Un:
                case {} when instr.OpCode == CilOpCodes.Conv_U2:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_U2:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_U2_Un:
                    castSize = 2;
                    break;

                case {} when instr.OpCode == CilOpCodes.Conv_I4:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_I4:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_I4_Un:
                case {} when instr.OpCode == CilOpCodes.Conv_U4:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_U4:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_U4_Un:
                    castSize = 4;
                    break;

                case {} when instr.OpCode == CilOpCodes.Conv_I8:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_I8:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_I8_Un:
                case {} when instr.OpCode == CilOpCodes.Conv_U8:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_U8:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_U8_Un:
                    castSize = 8;
                    break;

                case {} when instr.OpCode == CilOpCodes.Conv_I:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_I:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_I_Un:
                case {} when instr.OpCode == CilOpCodes.Conv_U:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_U:
                case {} when instr.OpCode == CilOpCodes.Conv_Ovf_U_Un:
                    castSize = -1;
                    break;

                case {} when instr.OpCode == CilOpCodes.Nop: continue;
                default:
                    lastCast = null;
                    continue;
            }

            if(lastCast != null && lastCastSize == castSize) lastCast.ReplaceWithNop();
            lastCast = instr;
            lastCastSize = castSize;
        }
    }

    private void RemoveBaseCtorCalls(CilMethodBody body, CilInstructionCollection instrs) {
        for(int i = 0; i < instrs.Count-1; i++) {
            if(instrs[i].OpCode != CilOpCodes.Ldarg_0 || instrs[i+1].OpCode != CilOpCodes.Call) continue;
            if(instrs[i+1].Operand is not IMethodDescriptor calledMethod) continue;
            if(calledMethod.DeclaringType?.FullName != "System.Object" || calledMethod.Name != ".ctor") continue;
            instrs[i].ReplaceWithNop();
            instrs[i+1].ReplaceWithNop();
        }
    }

    private void DetectDeadBranches(CilMethodBody body, CilInstructionCollection instrs) {
        for(int i = 0; i < instrs.Count-1; i++) {
            if(!instrs[i].IsLdcI4() || !instrs[i+1].IsConditionalBranch()) continue;

            //Determine if the branch is taken
            bool takesBranch;
            CilOpCode branchOpCode = instrs[i+1].OpCode;
            if(branchOpCode == CilOpCodes.Brfalse || branchOpCode == CilOpCodes.Brfalse_S) {
                takesBranch = instrs[i].GetLdcI4Constant() == 0;
            } else if(branchOpCode == CilOpCodes.Brtrue || branchOpCode == CilOpCodes.Brtrue_S) {
                takesBranch = instrs[i].GetLdcI4Constant() != 0;
            } else continue;

            //Check if there are jumps
            bool jumpRuleOut = false;
            instrs.CalculateOffsets();
            for(int j = 0; j < instrs.Count-1; j++) {
                if(instrs[j+1].OpCode.OperandType is not CilOperandType.InlineBrTarget and not CilOperandType.ShortInlineBrTarget) continue;

                //Determine and check the jump target
                int jumpOff = instrs[j+1].Operand switch {
                    ICilLabel label => label.Offset,
                    int off => off,
                    sbyte off => off,
                    _ => throw new Exception($"Invalid jump instruction operand: {instrs[j+1]}")
                };
                if(jumpOff != instrs[i+1].Offset) continue;

                jumpRuleOut = true;
                break;
            }
            if(jumpRuleOut) continue;

            //Update the instructions
            instrs[i].ReplaceWithNop();
            if(takesBranch) instrs[i+1].OpCode = CilOpCodes.Br;
            else instrs[i+1].ReplaceWithNop();
        }
    }

    private void TrimDeadCode(CilMethodBody body, CilInstructionCollection instrs) {
        instrs.CalculateOffsets();

        //Run a DFS over the IL graph
        bool[] visited = new bool[instrs.Count];

        Stack<int> dfsStack = new Stack<int>();
        dfsStack.Push(0);
        while(dfsStack.TryPop(out int instrIdx)) {
            if(visited[instrIdx]) continue;
            visited[instrIdx] = true;      

            //Find the next jump
            while(!instrs[instrIdx].IsBranch() && instrs[instrIdx].OpCode != CilOpCodes.Ret && instrs[instrIdx].OpCode != CilOpCodes.Throw) visited[++instrIdx] = true;
            if(!instrs[instrIdx].IsBranch()) continue;

            //Add to the DFS stack
            int jumpOff = instrs[instrIdx].Operand switch {
                ICilLabel label => label.Offset,
                int off => off,
                sbyte off => off,
                _ => throw new Exception($"Invalid jump instruction operand: {instrs[instrIdx]}")
            };
            dfsStack.Push(Enumerable.Range(0, instrs.Count).First(idx => instrs[idx].Offset == jumpOff));
            if(instrs[instrIdx].IsConditionalBranch()) dfsStack.Push(instrIdx + 1);
        }

        //Trim out dead instructions
        int newIdx = 0;
        for(int i = 0; i < visited.Length; i++) {
            if(!visited[i]) instrs.RemoveAt(newIdx);
            else newIdx++;
        }
    }

    private void TrimUnnecessaryBranches(CilMethodBody body, CilInstructionCollection instrs) {
        TrimNOPs(body, instrs);
        foreach(CilInstruction instr in instrs) {
            if(!instr.IsUnconditionalBranch()) continue;

            int jumpOff = instr.Operand switch {
                ICilLabel label => label.Offset,
                int off => off,
                sbyte off => off,
                _ => throw new Exception($"Invalid jump instruction operand: {instr}")
            };
            if(jumpOff == instr.Offset + instr.Size) instr.ReplaceWithNop();
        }
    }

    private void TrimNOPs(CilMethodBody body, CilInstructionCollection instrs) {
        CilInstructionLabel? nextInstrLabel = null;
        foreach(CilInstruction instr in instrs.ToArray()) {
            if(instr.OpCode == CilOpCodes.Nop) {
                //Fixup jumps
                instrs.CalculateOffsets();
                foreach(CilInstruction jumpInstr in instrs) {
                    if(jumpInstr.OpCode.OperandType is not CilOperandType.InlineBrTarget and not CilOperandType.ShortInlineBrTarget) continue;

                    //Determine and check the jump target
                    int jumpOff = jumpInstr.Operand switch {
                        ICilLabel label => label.Offset,
                        int off => off,
                        sbyte off => off,
                        _ => throw new Exception($"Invalid jump instruction operand: {jumpInstr}")
                    };
                    if(jumpOff != instr.Offset) continue;

                    jumpInstr.Operand = nextInstrLabel ??= new CilInstructionLabel();
                }
                instrs.Remove(instr);
            } else if(nextInstrLabel != null) {
                nextInstrLabel.Instruction = instr;
                nextInstrLabel = null;
            }
        }
    }
}