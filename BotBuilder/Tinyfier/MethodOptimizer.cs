using System;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;

public partial class Tinyfier {
    private void OptimizeMethods() {
        int numMethodBodies = 0;
        foreach(TypeDefinition type in targetTypes) {
            foreach(MethodDefinition method in type.Methods) {
                if(method.CilMethodBody != null) {
                    OptimizeMethodBody(method.CilMethodBody);
                    numMethodBodies++;
                }
            }
        }
        Log($"Optimized {numMethodBodies} method bodies");
    }

    private void OptimizeMethodBody(CilMethodBody body) {
        CilInstructionCollection instrs = body.Instructions;

        //Optimize instructions
        instrs.OptimizeMacros();

        //Remove unnecessary casts
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

        //Remove base object constructor calls
        for(int i = 0; i < instrs.Count-1; i++) {
            if(instrs[i].OpCode != CilOpCodes.Ldarg_0 || instrs[i+1].OpCode != CilOpCodes.Call) continue;
            if(instrs[i+1].Operand is not IMethodDescriptor calledMethod) continue;
            if(calledMethod.DeclaringType?.FullName != "System.Object" || calledMethod.Name != ".ctor") continue;
            instrs[i].ReplaceWithNop();
            instrs[i+1].ReplaceWithNop();
        }

        //Trim out NOPs
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