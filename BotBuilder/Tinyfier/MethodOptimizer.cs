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

    private void OptimizeMethods() {
        int numMethodBodies = 0;
        foreach(TypeDefinition type in targetTypes) {
            foreach(MethodDefinition method in type.Methods) {
                if(method.CilMethodBody is not { Instructions: CilInstructionCollection instrs } body) continue;
                RemoveUnnecessaryCasts(body, instrs);
                RemoveBaseCtorCalls(body, instrs);
                DetectDeadBranches(body, instrs);
                TrimPoppedValues(body, instrs);
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
        //Parse the IL expression trees
        Stack<ExprValue> evalStack = new Stack<ExprValue>();
        ExprValue PopOrAny() => evalStack.TryPop(out ExprValue val) ? val : default;

        for(int instrIdx = 0; instrIdx < instrs.Count; instrIdx++) {
            CilInstruction instr = instrs[instrIdx];

            //Check if a jump lands here, and if so reset the eval stack
            if(instrs.Any(i => i.IsBranch() && GetJumpOffset(i) == instr.Offset)) evalStack.Clear();

            //Handle the instruction behavior
            void HandleDeadBranch(bool takeBranch) {
                for(int i = instr.GetStackPopCount(body); i > 0; i--) instrs.Insert(++instrIdx, CilOpCodes.Pop);
                if(takeBranch) instrs.Insert(++instrIdx, new CilInstruction(CilOpCodes.Br, instr.Operand));
                instr.ReplaceWithNop();
            }

            CilOpCode instrOpCode = instr.OpCode;
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
    }

    private void TrimPoppedValues(CilMethodBody body, CilInstructionCollection instrs) {
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

                continue;
            }

            //Handle instruction stack behavior
            int startIdx = instrIdx;
            for(int i = instr.GetStackPopCount(body); i > 0; i--) {
                if(!exprStartInstrIdx.TryPop(out startIdx)) startIdx = -1;
            }

            if(instr.OpCode.FlowControl == CilFlowControl.Call) startIdx = -1; //Calls might have side effects

            for(int i = instr.GetStackPushCount(); i > 0; i--) exprStartInstrIdx.Push(startIdx);
        }
    }

    private void TrimDeadCode(CilMethodBody body, CilInstructionCollection instrs) {
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
        int newIdx = 0;
        for(int i = 0; i < visited.Length; i++) {
            if(!visited[i]) instrs.RemoveAt(newIdx);
            else newIdx++;
        }
    }

    private void TrimUnnecessaryBranches(CilMethodBody body, CilInstructionCollection instrs) {
        TrimNOPs(body, instrs);
        foreach(CilInstruction instr in instrs) {
            if(instr.OpCode != CilOpCodes.Br && instr.OpCode != CilOpCodes.Br_S) continue;

            instrs.CalculateOffsets();
            if(GetJumpOffset(instr) == instr.Offset + instr.Size) instr.ReplaceWithNop();
        }
    }

    private void TrimNOPs(CilMethodBody body, CilInstructionCollection instrs) {
        CilInstructionLabel? nextInstrLabel = null;
        foreach(CilInstruction instr in instrs.ToArray()) {
            if(instr.OpCode == CilOpCodes.Nop) {
                //Fixup jumps
                instrs.CalculateOffsets();
                foreach(CilInstruction jumpInstr in instrs) {
                    if(!jumpInstr.IsBranch()) continue;
                    if(GetJumpOffset(jumpInstr) != instr.Offset) continue;
                    jumpInstr.Operand = nextInstrLabel ??= new CilInstructionLabel();
                }
                instrs.Remove(instr);
            } else if(nextInstrLabel != null) {
                nextInstrLabel.Instruction = instr;
                nextInstrLabel = null;
            }
        }
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
                left ^= is64Bit.Value ? (ExprValue) 0x8000000000000000UL : (ExprValue) 0x8000000000U;
                right ^= is64Bit.Value ? (ExprValue) 0x8000000000000000UL : (ExprValue) 0x8000000000U;
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