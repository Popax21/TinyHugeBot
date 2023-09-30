using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;

public partial class Tinyfier {
    private static int GetJumpOffset(CilInstruction instr) => instr.Operand switch {
        ICilLabel label => label.Offset,
        int off => off,
        sbyte off => off,
        _ => throw new Exception($"Invalid jump instruction operand: {instr}")
    };
    private void RedirectJumps(CilMethodBody body, int targetOffset, CilInstructionLabel newLabel) {
        foreach(CilExceptionHandler handler in body.ExceptionHandlers) {
            if(handler.TryStart?.Offset == targetOffset) handler.TryStart = newLabel;
            if(handler.TryEnd?.Offset == targetOffset) handler.TryEnd = newLabel;
            if(handler.HandlerStart?.Offset == targetOffset) handler.HandlerStart = newLabel;
            if(handler.HandlerEnd?.Offset == targetOffset) handler.HandlerEnd = newLabel;
            if(handler.FilterStart?.Offset == targetOffset) handler.FilterStart = newLabel;
        }

        body.Instructions.CalculateOffsets();
        foreach(CilInstruction jumpInstr in body.Instructions) {
            if(!jumpInstr.IsBranch()) continue;
            if(GetJumpOffset(jumpInstr) != targetOffset) continue;
            jumpInstr.Operand = newLabel;
        }
    }

    private void OptimizeMethods() {
        int numMethodBodies = 0;
        foreach(TypeDefinition type in targetTypes) {
            foreach(MethodDefinition method in type.Methods) {
                if(method.CilMethodBody is not { Instructions: CilInstructionCollection instrs } body) continue;

                RemoveBaseCtorCalls(body, instrs);
                while(true) {
                    bool didModify = false;
                    didModify |= RemoveUnnecessaryCasts(body, instrs);
                    didModify |= RemoveUnnecessaryIndirection(body, instrs);
                    didModify |= RemoveProxyVariables(body, instrs);
                    didModify |= SimplifyStaticBranchConditions(body, instrs);
                    didModify |= FlattenChainedBranches(body, instrs);
                    didModify |= TrimUnnecessaryBranches(body, instrs);
                    didModify |= TrimPoppedExpressions(body, instrs);
                    didModify |= TrimDeadCode(body, instrs);
                    didModify |= TrimNOPs(body, instrs);

                    if(!didModify) break;
                }

                instrs.OptimizeMacros();
                numMethodBodies++;
            }
        }
        Log($"Optimized {numMethodBodies} method bodies");
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

    private bool RemoveUnnecessaryCasts(CilMethodBody body, CilInstructionCollection instrs) {
        bool didModify = false;
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

            if(lastCast != null && lastCastSize == castSize) {
                lastCast.ReplaceWithNop();
                didModify = true;
            }
            lastCast = instr;
            lastCastSize = castSize;
        }
        return didModify;
    }

    private static Dictionary<CilOpCode, CilOpCode> Ldind_Ldelem_Map = new Dictionary<CilOpCode, CilOpCode>() {
        [CilOpCodes.Ldind_I]   = CilOpCodes.Ldelem_I,
        [CilOpCodes.Ldind_I1]  = CilOpCodes.Ldelem_I1,
        [CilOpCodes.Ldind_I2]  = CilOpCodes.Ldelem_I2,
        [CilOpCodes.Ldind_I4]  = CilOpCodes.Ldelem_I4,
        [CilOpCodes.Ldind_I8]  = CilOpCodes.Ldelem_I8,
        [CilOpCodes.Ldind_U1]  = CilOpCodes.Ldelem_U1,
        [CilOpCodes.Ldind_U2]  = CilOpCodes.Ldelem_U2,
        [CilOpCodes.Ldind_U4]  = CilOpCodes.Ldelem_U4,
        [CilOpCodes.Ldind_R4]  = CilOpCodes.Ldelem_R4,
        [CilOpCodes.Ldind_R8]  = CilOpCodes.Ldelem_R8,
        [CilOpCodes.Ldind_Ref] = CilOpCodes.Ldelem_Ref,
    };
    private static Dictionary<CilOpCode, CilOpCode> Stind_Stelem_Map = new Dictionary<CilOpCode, CilOpCode>() {
        [CilOpCodes.Stind_I]   = CilOpCodes.Stelem_I,
        [CilOpCodes.Stind_I1]  = CilOpCodes.Stelem_I1,
        [CilOpCodes.Stind_I2]  = CilOpCodes.Stelem_I2,
        [CilOpCodes.Stind_I4]  = CilOpCodes.Stelem_I4,
        [CilOpCodes.Stind_I8]  = CilOpCodes.Stelem_I8,
        [CilOpCodes.Stind_R4]  = CilOpCodes.Stelem_R4,
        [CilOpCodes.Stind_R8]  = CilOpCodes.Stelem_R8,
        [CilOpCodes.Stind_Ref] = CilOpCodes.Stelem_Ref,
    };

    private bool RemoveUnnecessaryIndirection(CilMethodBody body, CilInstructionCollection instrs) {
        bool didModify = false;
        Stack<CilInstruction> evalEmitterStack = new Stack<CilInstruction>();
        foreach(CilInstruction instr in instrs) {
            //Check if a jump lands here
            if(instrs.Any(i => i.IsBranch() && GetJumpOffset(i) == instr.Offset)) evalEmitterStack.Clear();

            //Handle indirect loads/stores
            if(Ldind_Ldelem_Map.TryGetValue(instr.OpCode, out CilOpCode ldelemOp)) {
                if(evalEmitterStack.TryPop(out CilInstruction? emitterInstr)) {
                    if(emitterInstr.OpCode == CilOpCodes.Ldelema) {
                        instr.OpCode = ldelemOp;
                        emitterInstr.ReplaceWithNop();
                        didModify = true;
                    } else if(emitterInstr.OpCode == CilOpCodes.Ldarga || emitterInstr.OpCode == CilOpCodes.Ldarga_S) {
                        instr.OpCode = CilOpCodes.Ldarg;
                        instr.Operand = emitterInstr.GetParameter(body.Owner.Parameters);
                        emitterInstr.ReplaceWithNop();
                        didModify = true;
                    } else if(emitterInstr.OpCode == CilOpCodes.Ldloca || emitterInstr.OpCode == CilOpCodes.Ldloca_S) {
                        instr.OpCode = CilOpCodes.Ldloc;
                        instr.Operand = emitterInstr.GetLocalVariable(body.LocalVariables);
                        emitterInstr.ReplaceWithNop();
                        didModify = true;
                    }
                }
                evalEmitterStack.Push(instr);
                continue;
            } else if(Stind_Stelem_Map.TryGetValue(instr.OpCode, out CilOpCode stelemOp)) {
                evalEmitterStack.TryPop(out _);
                if(evalEmitterStack.TryPop(out CilInstruction? emitterInstr)) {
                    if(emitterInstr.OpCode == CilOpCodes.Ldelema) {
                        instr.OpCode = stelemOp;
                        emitterInstr.ReplaceWithNop();
                        didModify = true;
                    } else if(emitterInstr.OpCode == CilOpCodes.Ldarga || emitterInstr.OpCode == CilOpCodes.Ldarga_S) {
                        instr.OpCode = CilOpCodes.Starg;
                        instr.Operand = emitterInstr.GetParameter(body.Owner.Parameters);
                        emitterInstr.ReplaceWithNop();
                        didModify = true;
                    } else if(emitterInstr.OpCode == CilOpCodes.Ldloca || emitterInstr.OpCode == CilOpCodes.Ldloca_S) {
                        instr.OpCode = CilOpCodes.Stloc;
                        instr.Operand = emitterInstr.GetLocalVariable(body.LocalVariables);
                        emitterInstr.ReplaceWithNop();
                        didModify = true;
                    }
                }
                continue;
            }

            //Handle stack behavior
            for(int i = instr.GetStackPopCount(body); i > 0; i--) evalEmitterStack.TryPop(out _);
            for(int i = instr.GetStackPushCount(); i > 0; i--) evalEmitterStack.Push(instr);
        }
        return didModify;
    }

    private bool RemoveProxyVariables(CilMethodBody body, CilInstructionCollection instrs) {
        instrs.ExpandMacros(); //Ensure that removing variables does not mess things up

        bool didModify = false;
        foreach(CilLocalVariable local in body.LocalVariables.ToArray()) {
            //Ensure there's only one load and no address loads for this local
            if(instrs.Any(instr => (instr.OpCode == CilOpCodes.Ldloca || instr.OpCode == CilOpCodes.Ldloca_S) && instr.GetLocalVariable(body.LocalVariables) == local)) continue;

            int numLoads = 0;
            int loadInstrIdx = -1;
            for(int i = 0; i < instrs.Count; i++) {
                if(instrs[i].IsLdloc() && instrs[i].GetLocalVariable(body.LocalVariables) == local) {
                    numLoads++;
                    loadInstrIdx = i;
                }
            }
            if(numLoads > 1) continue;

            //Determine the variable being proxied (if any)
            CilLocalVariable? proxiedLocal = null;
            if(loadInstrIdx >= 0 && loadInstrIdx < instrs.Count-1) {
                if(instrs[loadInstrIdx + 1].IsStloc()) proxiedLocal = instrs[loadInstrIdx + 1].GetLocalVariable(body.LocalVariables);
            }

            //Ensure that replacing the variable would cause no harm by running a DFS from all loads points
            if(proxiedLocal != null) {
                Stack<int> dfsStack = new Stack<int>();
                for(int i = 0; i < instrs.Count; i++) {
                    if(!instrs[i].IsStloc() || instrs[i].GetLocalVariable(body.LocalVariables) != local) continue;
                    dfsStack.Push(i);
                }

                bool replaceIsSafe = true;
                bool[] visited = new bool[instrs.Count];
                while(dfsStack.TryPop(out int instrIdx)) {
                    if(visited[instrIdx]) continue;
                    visited[instrIdx] = true;      

                    //Find the next jump
                    while(!instrs[instrIdx].IsBranch() && instrs[instrIdx].OpCode != CilOpCodes.Ret && instrs[instrIdx].OpCode != CilOpCodes.Throw) {
                        if(instrIdx == loadInstrIdx) break;
                        if(instrs[instrIdx].OpCode.OperandType is CilOperandType.InlineVar or CilOperandType.ShortInlineVar && instrs[instrIdx].GetLocalVariable(body.LocalVariables) == proxiedLocal) {
                            replaceIsSafe = false;
                            break;
                        }

                        visited[++instrIdx] = true;
                    }
                    if(!replaceIsSafe) break;
                    if(!instrs[instrIdx].IsBranch()) continue;

                    //Add to the DFS stack
                    dfsStack.Push(instrs.GetIndexByOffset(GetJumpOffset(instrs[instrIdx])));
                    if(instrs[instrIdx].IsConditionalBranch()) dfsStack.Push(instrIdx + 1);
                }
                if(!replaceIsSafe) continue;
            } else if(loadInstrIdx >= 0) {
                //This is only safe if all stores directly branch to the load
                bool replaceIsSafe = true;
                for(int i = 0; i < instrs.Count-1; i++) {
                    if(!instrs[i].IsStloc() || instrs[i].GetLocalVariable(body.LocalVariables) != local) continue;
                    if(i+1 == loadInstrIdx) continue;
                    if((instrs[i+1].OpCode == CilOpCodes.Br || instrs[i+1].OpCode == CilOpCodes.Br_S) && GetJumpOffset(instrs[i+1]) == instrs[loadInstrIdx].Offset) continue;

                    replaceIsSafe = false;
                    break;
                }
                if(!replaceIsSafe) continue;
            }

            //Replace all store references
            foreach(CilInstruction instr in instrs) {
                if(instr.IsStloc() && instr.GetLocalVariable(body.LocalVariables) == local) {
                    if(proxiedLocal != null) instr.Operand = proxiedLocal;
                    else if(loadInstrIdx >= 0) instr.ReplaceWithNop();
                    else instr.ReplaceWith(CilOpCodes.Pop);
                }
            }

            //Remove the proxy instruction pair
            if(loadInstrIdx >= 0) {
                instrs[loadInstrIdx].ReplaceWithNop();
                if(proxiedLocal != null) instrs[loadInstrIdx+1].ReplaceWithNop();
            }

            //Remove the local
            body.LocalVariables.Remove(local);
            didModify = true;
        }
        return didModify;
    }

    private bool SimplifyStaticBranchConditions(CilMethodBody body, CilInstructionCollection instrs) {
        bool didModify = false;

        //Parse the IL expression trees
        Stack<ExprValue> evalStack = new Stack<ExprValue>();
        ExprValue PopOrAny() => evalStack.TryPop(out ExprValue val) ? val : default;

        instrs.CalculateOffsets();
        for(int instrIdx = 0; instrIdx < instrs.Count; instrIdx++) {
            CilInstruction instr = instrs[instrIdx];

            //Check if a jump lands here, and if so reset the eval stack
            if(instrs.Any(i => i.IsBranch() && GetJumpOffset(i) == instr.Offset)) evalStack.Clear();

            //Handle the instruction behavior
            CilOpCode instrOpCode = instr.OpCode;
            CilInstruction branchInstr = instr;
            if(instrOpCode == CilOpCodes.Br || instrOpCode == CilOpCodes.Br_S) {
                //If we have a fixed branch to another conditional branch, proxy the conditional branch to this branch
                if(instrs.GetByOffset(GetJumpOffset(instr)) is CilInstruction targetInstr && targetInstr.IsConditionalBranch()) {
                    instrOpCode = targetInstr.OpCode;
                    branchInstr = targetInstr;
                }
            }

            void HandleDeadBranch(bool takeBranch) {
                //Pop branch condition arguments (the pop expression trimmer will remove them)
                for(int i = branchInstr.GetStackPopCount(body); i > 0; i--) instrs.Insert(++instrIdx, CilOpCodes.Pop);

                //Insert a fixed branch if the branch is taken
                //Additionally, if the branch instruction is different, insert a jump to the actual next instruction
                if(takeBranch) instrs.Insert(++instrIdx, new CilInstruction(CilOpCodes.Br, branchInstr.Operand));
                else if(branchInstr != instr) instrs.Insert(++instrIdx, new CilInstruction(CilOpCodes.Br, new CilInstructionLabel(instrs[instrs.IndexOf(branchInstr)+1])));

                //NOP out the old instruction (it remains at the start to anchor branches in places)
                instr.ReplaceWithNop();
                didModify = true;
            }

            if(instr.IsLdcI4()) evalStack.Push(instr.GetLdcI4Constant());
            else if(instrOpCode == CilOpCodes.Ldc_I8) evalStack.Push((long) instr.Operand!);
            else if(instrOpCode == CilOpCodes.Conv_I1) evalStack.Push(PopOrAny().Convert(8, true));
            else if(instrOpCode == CilOpCodes.Conv_U1) evalStack.Push(PopOrAny().Convert(8, false));
            else if(instrOpCode == CilOpCodes.Conv_I2) evalStack.Push(PopOrAny().Convert(16, true));
            else if(instrOpCode == CilOpCodes.Conv_U2) evalStack.Push(PopOrAny().Convert(16, false));
            else if(instrOpCode == CilOpCodes.Conv_I4) evalStack.Push(PopOrAny().Convert(32, true));
            else if(instrOpCode == CilOpCodes.Conv_U4) evalStack.Push(PopOrAny().Convert(32, false));
            else if(instrOpCode == CilOpCodes.Conv_I8) evalStack.Push(PopOrAny().Convert(64, true));
            else if(instrOpCode == CilOpCodes.Conv_U8) evalStack.Push(PopOrAny().Convert(64, false));
            else if(instrOpCode == CilOpCodes.Neg) evalStack.Push(-PopOrAny());
            else if(instrOpCode == CilOpCodes.Add) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                evalStack.Push(left + right);
            } else if(instrOpCode == CilOpCodes.Sub) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                evalStack.Push(left - right);
            } else if(instrOpCode == CilOpCodes.Mul) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                evalStack.Push(left * right);
            } else if(instrOpCode == CilOpCodes.Div) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                evalStack.Push(ExprValue.Divide(left, right, true));
            } else if(instrOpCode == CilOpCodes.Div_Un) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                evalStack.Push(ExprValue.Divide(left, right, false));
            } else if(instrOpCode == CilOpCodes.Not) evalStack.Push(~PopOrAny());
            else if(instrOpCode == CilOpCodes.And) evalStack.Push(PopOrAny() & PopOrAny());
            else if(instrOpCode == CilOpCodes.Or) evalStack.Push(PopOrAny() | PopOrAny());
            else if(instrOpCode == CilOpCodes.Xor) evalStack.Push(PopOrAny() ^ PopOrAny());
            else if(instrOpCode == CilOpCodes.Shl) {
                ExprValue amnt = PopOrAny(), val = PopOrAny();
                evalStack.Push(val << amnt);
            } else if(instrOpCode == CilOpCodes.Shr) {
                ExprValue amnt = PopOrAny(), val = PopOrAny();
                evalStack.Push(ExprValue.ShiftRight(amnt, val, true));
            } else if(instrOpCode == CilOpCodes.Shr_Un) {
                ExprValue amnt = PopOrAny(), val = PopOrAny();
                evalStack.Push(ExprValue.ShiftRight(amnt, val, false));
            } else if(instrOpCode == CilOpCodes.Ceq) evalStack.Push(ExprValue.CmpEqual(PopOrAny(), PopOrAny()));
            else if(instrOpCode == CilOpCodes.Clt) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                evalStack.Push(ExprValue.CmpLess(left, right, true));
            } else if(instrOpCode == CilOpCodes.Clt_Un) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                evalStack.Push(ExprValue.CmpLess(left, right, false));
            } else if(instrOpCode == CilOpCodes.Cgt) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                evalStack.Push(ExprValue.CmpGreater(left, right, true));
            } else if(instrOpCode == CilOpCodes.Brfalse || instrOpCode == CilOpCodes.Brfalse_S) {
                if(((bool?) PopOrAny()) is bool branchCond) HandleDeadBranch(branchCond == false);
            } else if(instrOpCode == CilOpCodes.Brtrue || instrOpCode == CilOpCodes.Brtrue_S) {
                if(((bool?) PopOrAny()) is bool branchCond) HandleDeadBranch(branchCond == true);
            } else if(instrOpCode == CilOpCodes.Beq || instrOpCode == CilOpCodes.Beq_S) {
                if(ExprValue.CmpEqual(PopOrAny(), PopOrAny()) is bool takeBranch) HandleDeadBranch(takeBranch);
            } else if(instrOpCode == CilOpCodes.Blt || instrOpCode == CilOpCodes.Blt_S) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                if(ExprValue.CmpLess(left, right, true) is bool takeBranch) HandleDeadBranch(takeBranch);
            } else if(instrOpCode == CilOpCodes.Blt_Un || instrOpCode == CilOpCodes.Blt_Un_S) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                if(ExprValue.CmpLess(left, right, false) is bool takeBranch) HandleDeadBranch(takeBranch);
            } else if(instrOpCode == CilOpCodes.Bgt || instrOpCode == CilOpCodes.Bgt_S) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                if(ExprValue.CmpGreater(left, right, true) is bool takeBranch) HandleDeadBranch(takeBranch);
            } else if(instrOpCode == CilOpCodes.Bgt_Un || instrOpCode == CilOpCodes.Bgt_Un_S) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                if(ExprValue.CmpGreater(left, right, false) is bool takeBranch) HandleDeadBranch(takeBranch);
            } else if(instrOpCode == CilOpCodes.Ble || instrOpCode == CilOpCodes.Ble_S) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                if(ExprValue.CmpLessEqual(left, right, true) is bool takeBranch) HandleDeadBranch(takeBranch);
            } else if(instrOpCode == CilOpCodes.Ble_Un || instrOpCode == CilOpCodes.Ble_Un_S) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                if(ExprValue.CmpLessEqual(left, right, false) is bool takeBranch) HandleDeadBranch(takeBranch);
            } else if(instrOpCode == CilOpCodes.Bge || instrOpCode == CilOpCodes.Bge_S) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                if(ExprValue.CmpGreaterEqual(left, right, true) is bool takeBranch) HandleDeadBranch(takeBranch);
            } else if(instrOpCode == CilOpCodes.Bge_Un || instrOpCode == CilOpCodes.Bge_Un_S) {
                ExprValue right = PopOrAny(), left = PopOrAny();
                if(ExprValue.CmpGreaterEqual(left, right, false) is bool takeBranch) HandleDeadBranch(takeBranch);
            } else {
                for(int i = instr.GetStackPopCount(body); i > 0; i--) evalStack.TryPop(out _);
                for(int i = instr.GetStackPushCount(); i > 0; i--) evalStack.Push(default);
            }

            //Handle branches
            if(instr.IsBranch()) evalStack.Clear();
        }

        return didModify;
    }

    private bool FlattenChainedBranches(CilMethodBody body, CilInstructionCollection instrs) {
        instrs.CalculateOffsets();

        bool didModify = false;
        foreach(CilInstruction instr in instrs) {
            if(!instr.IsBranch()) continue;

            CilInstruction targetInstr = instrs.GetByOffset(GetJumpOffset(instr)) ?? throw new Exception($"Invalid jump target offset: {instr}");
            if(!targetInstr.IsUnconditionalBranch() || targetInstr.OpCode == CilOpCodes.Leave || targetInstr.OpCode == CilOpCodes.Leave_S) continue;

            instr.Operand = targetInstr.Operand;
            instrs.CalculateOffsets();
            didModify = true;
        }
        return didModify;
    }

    private bool TrimPoppedExpressions(CilMethodBody body, CilInstructionCollection instrs) {
        bool didModify = false;

        Stack<int> exprStartInstrIdx = new Stack<int>();
        for(int instrIdx = 0; instrIdx < instrs.Count; instrIdx++) {
            CilInstruction instr = instrs[instrIdx];

            //Check if a jump lands here, and if so reset the eval stack
            if(instrs.Any(i => i.IsBranch() && GetJumpOffset(i) == instr.Offset)) exprStartInstrIdx.Clear();

            //Handle branches / special instructions
            if(instr.IsBranch() || instr.OpCode.FlowControl is CilFlowControl.Return or CilFlowControl.Throw) {
                exprStartInstrIdx.Clear();
                continue;
            }

            //Handle pops
            if(instr.OpCode == CilOpCodes.Pop) {
                if(!exprStartInstrIdx.TryPop(out int exprStartIdx) || exprStartIdx < 0) continue;

                //NOP out the expression
                for(int i = exprStartIdx; i <= instrIdx; i++) instrs[i].ReplaceWithNop();
                instrs.CalculateOffsets();

                didModify = true;
                continue;
            }

            //Handle instruction stack behavior
            int startIdx = instrIdx;
            for(int i = instr.GetStackPopCount(body); i > 0; i--) {
                if(!exprStartInstrIdx.TryPop(out startIdx)) startIdx = -1;
            }

            //Calls might have side effects (assume that property getters have no side effects)
            if(instr.OpCode.FlowControl == CilFlowControl.Call && instr.Operand is IMethodDescriptor calledMethod) {
                if(!calledMethod.Name?.ToString()?.StartsWith("get_") ?? true) startIdx = -1;
            }

            for(int i = instr.GetStackPushCount(); i > 0; i--) exprStartInstrIdx.Push(startIdx);
        }

        return didModify;
    }

    private bool TrimDeadCode(CilMethodBody body, CilInstructionCollection instrs) {
        instrs.CalculateOffsets();

        //Run a DFS over the IL graph
        bool[] visited = new bool[instrs.Count];

        Stack<int> dfsStack = new Stack<int>();
        dfsStack.Push(0);
        foreach(CilExceptionHandler handler in body.ExceptionHandlers) {
            if(handler.HandlerStart is { Offset: int handlerOff }) dfsStack.Push(instrs.GetIndexByOffset(handlerOff));
            if(handler.FilterStart is { Offset: int filterOff }) dfsStack.Push(instrs.GetIndexByOffset(filterOff));
        }

        while(dfsStack.TryPop(out int instrIdx)) {
            if(instrIdx < 0) throw new Exception($"Invalid instruction offset in method {body.Owner}");
            if(visited[instrIdx]) continue;
            visited[instrIdx] = true;      

            //Find the next jump
            while(!instrs[instrIdx].IsBranch() && instrs[instrIdx].OpCode != CilOpCodes.Ret && instrs[instrIdx].OpCode != CilOpCodes.Throw) visited[++instrIdx] = true;
            if(!instrs[instrIdx].IsBranch()) continue;

            //Add to the DFS stack
            dfsStack.Push(instrs.GetIndexByOffset(GetJumpOffset(instrs[instrIdx])));
            if(instrs[instrIdx].IsConditionalBranch()) dfsStack.Push(instrIdx + 1);
        }

        //Trim out dead instructions
        bool didModify = false;
        int newIdx = 0;
        for(int i = 0; i < visited.Length; i++) {
            if(!visited[i]) {
                instrs.RemoveAt(newIdx);
                didModify = true;
            } else newIdx++;
        }
        return didModify;
    }

    private bool TrimUnnecessaryBranches(CilMethodBody body, CilInstructionCollection instrs) {
        bool didModify = false;
        didModify |= TrimNOPs(body, instrs);

        instrs.CalculateOffsets();
        for(int instrIdx = 0; instrIdx < instrs.Count; instrIdx++) {
            CilInstruction instr = instrs[instrIdx];
            if(!instr.IsBranch() || instr.OpCode == CilOpCodes.Leave || instr.OpCode == CilOpCodes.Leave_S) continue;

            bool isUnnecessary = false;
            isUnnecessary |= GetJumpOffset(instr) == instr.Offset + instr.Size;
            if(instr.IsConditionalBranch() && instrIdx < instrs.Count-1 && instrs[instrIdx+1] is CilInstruction nextInstr) {
                isUnnecessary |= nextInstr.IsUnconditionalBranch() && GetJumpOffset(instr) == GetJumpOffset(nextInstr);
            }

            if(isUnnecessary) {
                for(int i = instr.GetStackPopCount(body); i > 0; i--) instrs.Insert(++instrIdx, CilOpCodes.Pop);
                instr.ReplaceWithNop();
                instrs.CalculateOffsets();
                didModify = true;
            }
        }

        return didModify;
    }

    private bool TrimNOPs(CilMethodBody body, CilInstructionCollection instrs) {
        bool didModify = false;
        CilInstructionLabel? nextInstrLabel = null;
        foreach(CilInstruction instr in instrs.ToArray()) {
            if(instr.OpCode == CilOpCodes.Nop) {
                //Fixup jumps
                body.Instructions.CalculateOffsets();
                RedirectJumps(body, instr.Offset, nextInstrLabel ??= new CilInstructionLabel());
                instrs.Remove(instr);
                didModify = true;
            } else if(nextInstrLabel != null) {
                nextInstrLabel.Instruction = instr;
                nextInstrLabel = null;
            }
        }
        return didModify;
    }

    private readonly record struct ExprValue {
        public const ulong All32BitsMask = 0xffffffffU, All64BitsMask = 0xffffffffffffffffUL;

        private static bool? Check64BitConistency(ExprValue left, ExprValue right) {
            if(!left.HasValue) return right.Is64Bit;
            if(!right.HasValue) return left.Is64Bit;
            if(left.Is64Bit != right.Is64Bit) throw new ArgumentException("Mixed usage of 32 and 64 bit expression values");
            return left.Is64Bit;
        }

        public static ExprValue SupersetOf(ExprValue val1, ExprValue val2) => new ExprValue(
            Check64BitConistency(val1, val2),
            val1.KnownBitsMask & val2.KnownBitsMask & ~(val1.KnownBitsValue ^ val2.KnownBitsValue),
            val1.KnownBitsValue
        );

        public static ExprValue Divide(ExprValue left, ExprValue right, bool isSigned) {
            if(Check64BitConistency(left, right) is not bool is64Bit) return default;
            if(left.HasUnknownBits || right.HasUnknownBits) return new ExprValue(is64Bit);

            if(!is64Bit && !isSigned) return   (int) left.KnownBitsValue /   (int) right.KnownBitsValue;
            if(!is64Bit &&  isSigned) return  (uint) left.KnownBitsValue /  (uint) right.KnownBitsValue;
            if( is64Bit && !isSigned) return  (long) left.KnownBitsValue /  (long) right.KnownBitsValue;
            if( is64Bit &&  isSigned) return (ulong) left.KnownBitsValue / (ulong) right.KnownBitsValue;
            throw new UnreachableException();
        }

        public static ExprValue ShiftRight(ExprValue left, ExprValue right, bool signed) {
            if(!left.HasValue) return default;
            bool is64Bit = left.Is64Bit.Value;
            ulong knownShiftBits = right.KnownBitsMask & (is64Bit ? 0b111U : 0b011U);

            //Determine all possible shift results
            ExprValue? res = null;
            for(int i = 0; i < (is64Bit ? 64 : 32); i++) {
                //Check if this is a valid shift amount
                if(((ulong) i & knownShiftBits) != (right.KnownBitsValue & knownShiftBits)) continue;

                //Update the known masks by this shift result
                ulong shiftedMask = left.KnownBitsMask >> i, shiftedBits = left.KnownBitsValue >> i;
                if(!signed || (left.KnownBitsMask >> (is64Bit ? 63 : 31)) != 0) {
                    ulong shiftInMask = ((1UL << i) - 1) << ((is64Bit ? 64 : 32) - i);
                    shiftedMask |= shiftInMask;
                    if(signed && (left.KnownBitsValue >> (is64Bit ? 63 : 31)) != 0) shiftedBits |= shiftInMask;
                }
                ExprValue shiftedVal = new ExprValue(left.Is64Bit, shiftedMask, shiftedBits);
                res = SupersetOf(res ?? shiftedVal, shiftedVal);
            }
            return res!.Value;
        }

        private static bool? HandleSignedCompare(ref ExprValue left, ref ExprValue right, bool isSigned) {
            bool? is64Bit = Check64BitConistency(left, right);
            if(isSigned && is64Bit.HasValue) {
                left ^= is64Bit.Value ? (ExprValue) 0x8000000000000000UL : (ExprValue) 0x80000000U;
                right ^= is64Bit.Value ? (ExprValue) 0x8000000000000000UL : (ExprValue) 0x80000000U;
            }
            return is64Bit;
        }

        public static bool? CmpEqual(ExprValue left, ExprValue right) {
            if(Check64BitConistency(left, right) == null || left.HasUnknownBits || right.HasUnknownBits) return null;
            return left.KnownBitsValue == right.KnownBitsValue;
        }

        public static bool? CmpLess(ExprValue left, ExprValue right, bool isSigned) {
            if(HandleSignedCompare(ref left, ref right, isSigned) is not bool is64Bit) return null;

            bool? prevRes = false;
            for(ulong bitMask = 1; (bitMask & (is64Bit ? All64BitsMask : All32BitsMask)) != 0; bitMask <<= 1) {
                ulong lKnowsBit = left.KnownBitsMask & bitMask, rKnowsBit = right.KnownBitsMask & bitMask;
                ulong lBit = left.KnownBitsValue & bitMask, rBit = right.KnownBitsValue & bitMask;

                //If both bits are unknown, we can obtain any result
                if(lKnowsBit == 0 && rKnowsBit == 0) {
                    prevRes = null;
                    continue;
                }

                //If both bits are known, we can determine a fixed result
                if(lKnowsBit != 0 && rKnowsBit != 0) {
                    if(lBit != rBit) prevRes = lBit < rBit;
                    continue;
                }

                //One of the bits is unknown, the other one is known; we could always fall back to the previous result
                //However, if the non-fallthrough case's result is consistent with the fallthrough case, we can keep it
                bool knownRes = (lKnowsBit == 0) ? (rBit != 0) : (lBit == 0);
                if(knownRes != prevRes) prevRes = null;
            }
            return prevRes;
        }
        public static bool? CmpGreater(ExprValue left, ExprValue right, bool isSigned) => CmpLess(right, left, isSigned);
        public static bool? CmpLessEqual(ExprValue left, ExprValue right, bool isSigned) => !CmpGreater(left, right, isSigned);
        public static bool? CmpGreaterEqual(ExprValue left, ExprValue right, bool isSigned) => !CmpLess(left, right, isSigned);

        public readonly bool? Is64Bit;
        public readonly ulong KnownBitsMask, KnownBitsValue;

        [MemberNotNullWhen(true, nameof(Is64Bit))]
        public bool HasValue => Is64Bit.HasValue;
        public bool HasUnknownBits => !HasValue || (KnownBitsMask != (Is64Bit.Value ? All64BitsMask : All32BitsMask));

        public ExprValue(bool? is64Bit) : this(is64Bit, 0, 0) {}
        public ExprValue(bool? is64Bit, ulong knownBitsMask, ulong knownBitsVal) {
            if(is64Bit == null) knownBitsMask = knownBitsVal = 0;
            if(is64Bit == false && knownBitsMask > uint.MaxValue) throw new ArgumentException("Attempted to create 32 bit expression value with more than 32 known bits");
            (Is64Bit, KnownBitsMask, KnownBitsValue) = (is64Bit, knownBitsMask, knownBitsVal & knownBitsMask);
        }

        public ExprValue Convert(int numBits, bool signed) {
            if(!HasValue) return default;

            ulong convMask = (1UL << numBits) - 1;
            ulong resMask = KnownBitsMask & convMask, resVal = KnownBitsValue & convMask;
            if(signed) {
                ulong signBitMask = 1UL << (numBits-1);
                if((KnownBitsMask & signBitMask) != 0) {
                    resMask |= ~convMask;
                    if((KnownBitsValue & signBitMask) != 0) resVal |= ~convMask;
                }
            } else resMask |= ~convMask;

            if(numBits <= 32) {
                resMask &= All32BitsMask;
                resVal &= All32BitsMask;
            }

            return new ExprValue(numBits > 32, resMask, resVal);
        }

        public static explicit operator bool?(ExprValue val) => (val.KnownBitsMask != 0 && val.KnownBitsValue != 0) | (val.HasUnknownBits ? null : false);
        public static implicit operator ExprValue(bool? val) => val.HasValue ? (val.Value ? 1 : 0) : new ExprValue(false, ~1U, 0);
        public static implicit operator ExprValue(int val) => (uint) val;
        public static implicit operator ExprValue(uint val) => new ExprValue(false, All32BitsMask, val);
        public static implicit operator ExprValue(long val) => (ulong) val;
        public static implicit operator ExprValue(ulong val) => new ExprValue(true, All64BitsMask, val);

        public static ExprValue operator -(ExprValue val) => val.HasValue ? (~val + (val.Is64Bit.Value ? (ExprValue) 1UL : (ExprValue) 1)) : default;
        public static ExprValue operator +(ExprValue left, ExprValue right) {
            if(Check64BitConistency(left, right) is not bool is64Bit) return default;

            ulong resKnownMask = 0, resKnownBits = 0;

            bool? carry = false;
            for(ulong bitMask = 1; (bitMask & (is64Bit ? All64BitsMask : All32BitsMask)) != 0; bitMask <<= 1) {
                ulong lKnowsBit = left.KnownBitsMask & bitMask, rKnowsBit = right.KnownBitsMask & bitMask;
                ulong lBit = left.KnownBitsValue & bitMask, rBit = right.KnownBitsValue & bitMask;

                //If both bits are unknown, we can obtain any result
                if(lKnowsBit == 0 && rKnowsBit == 0) {
                    carry = null;
                    continue;
                }

                //If both bits are known, we *might* be able determine a fixed result / carry
                if(lKnowsBit != 0 && rKnowsBit != 0) {
                    if(carry.HasValue) {
                        ulong sum = lBit + rBit + (carry.Value ? bitMask : 0);
                        resKnownMask |= bitMask;
                        resKnownBits |= sum & bitMask;
                        carry = sum > bitMask;
                    } else {
                        //We might able to trap the carry
                        if(lBit == 0 && rBit == 0) carry = false;
                    }
                    continue;
                }

                ulong knownBit = lBit | rBit;

                //If we have a fixed zero-carry and our known bit is a zero, we can ensure the carry is preserved
                if(carry == false && knownBit == 0) continue;

                //If we have a fixed one-carry and our known bit is a one, we can ensure the carry is preserved
                if(carry == true && knownBit != 0) continue;

                //Our carry could have any value
                carry = null;
            }
            return new ExprValue(is64Bit, resKnownMask, resKnownBits);
        }
        public static ExprValue operator -(ExprValue left, ExprValue right) => left + -right;
        public static ExprValue operator *(ExprValue left, ExprValue right) {
            if(Check64BitConistency(left, right) is not bool is64Bit) return default;
            if(left.HasUnknownBits || right.HasUnknownBits) return new ExprValue(is64Bit);

            if(!is64Bit) return (uint) left.KnownBitsValue *  (uint) right.KnownBitsValue;
            else return left.KnownBitsValue * right.KnownBitsValue;
        }

        public static ExprValue operator ~(ExprValue val) => new ExprValue(val.Is64Bit, val.KnownBitsMask, ~val.KnownBitsValue);
        public static ExprValue operator |(ExprValue left, ExprValue right) => new ExprValue(
            left.Is64Bit | right.Is64Bit,
            (left.KnownBitsMask & right.KnownBitsMask) | (left.KnownBitsMask & left.KnownBitsValue) | (right.KnownBitsMask & right.KnownBitsValue),
            left.KnownBitsValue | right.KnownBitsValue
        );
        public static ExprValue operator &(ExprValue left, ExprValue right) => new ExprValue(
            left.Is64Bit | right.Is64Bit,
            (left.KnownBitsMask & right.KnownBitsMask) | (left.KnownBitsMask & ~left.KnownBitsValue) | (right.KnownBitsMask & ~right.KnownBitsValue),
            left.KnownBitsValue & right.KnownBitsValue
        );
        public static ExprValue operator ^(ExprValue left, ExprValue right) => new ExprValue(
            left.Is64Bit | right.Is64Bit,
            left.KnownBitsMask & right.KnownBitsMask,
            left.KnownBitsValue ^ right.KnownBitsValue
        );

        public static ExprValue operator <<(ExprValue left, ExprValue right) {
            if(!left.HasValue) return default;
            bool is64Bit = left.Is64Bit.Value;
            ulong knownShiftBits = right.KnownBitsMask & (is64Bit ? 0b111U : 0b011U);

            //Determine all possible shift results
            ExprValue? res = null;
            for(int i = 0; i < (is64Bit ? 64 : 32); i++) {
                //Check if this is a valid shift amount
                if(((ulong) i & knownShiftBits) != (right.KnownBitsValue & knownShiftBits)) continue;

                //Update the known masks by this shift result
                ulong shiftedMask = (left.KnownBitsMask << i) | ((1UL << i) - 1);
                if(!is64Bit) shiftedMask &= All32BitsMask;
                ExprValue shiftedVal = new ExprValue(left.Is64Bit, shiftedMask, left.KnownBitsValue << i);

                res = SupersetOf(res ?? shiftedVal, shiftedVal);
            }
            return res!.Value;
        }
    }
}