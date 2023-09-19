using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;

public partial class Tinyfier {
    private void OptimizeArrayInitialization() {
        //Find RuntimeHelpers.InitializeArray calls and keep track of all RVA field types
        HashSet<TypeDefinition> arrayInitFieldTypes = new HashSet<TypeDefinition>();

        foreach(TypeDefinition type in targetTypes) {
            foreach(MethodDefinition method in type.Methods) {
                if(method.CilMethodBody is not { Instructions: CilInstructionCollection instrs }) continue;

                for(int i = 0; i < instrs.Count-1; i++) {
                    if(instrs[i].OpCode != CilOpCodes.Ldtoken || instrs[i+1].OpCode != CilOpCodes.Call) continue;
                    if(instrs[i+1].Operand is not IMethodDescriptor calledMethod) continue;
                    if(calledMethod.DeclaringType?.FullName != "System.Runtime.CompilerServices.RuntimeHelpers" || calledMethod.Name != "InitializeArray") continue;

                    if(instrs[i].Operand is not FieldDefinition { Signature.FieldType: TypeSignature fieldTypeSig }) continue;
                    if(fieldTypeSig?.Resolve() is not TypeDefinition fieldType || !targetTypesSet.Contains(fieldType)) continue;
                    if(!fieldType.IsValueType || fieldType.ClassLayout is not { PackingSize: 1 }) continue;
                    arrayInitFieldTypes.Add(fieldType);
                }
            }
        }

        //If there are multiple of these types, merge them
        if(arrayInitFieldTypes.Count <= 1) return;

        TypeDefinition mergedArrayInitType = new TypeDefinition(null, "<MergedArrayInitPlaceholder>", TypeAttributes.NotPublic, arrayInitFieldTypes.First().BaseType) {
            ClassLayout = new ClassLayout(1, arrayInitFieldTypes.Max(t => t.ClassLayout!.ClassSize)),
            IsExplicitLayout = true
        };
        AddTargetType(mergedArrayInitType);

        foreach(TypeDefinition fieldType in arrayInitFieldTypes) {
            RemoveTargetType(fieldType);
            typeRelinkMap.Add(fieldType, mergedArrayInitType);
        }

        Log($"Merged {arrayInitFieldTypes.Count} array initialization types into one type of size {mergedArrayInitType.ClassLayout.ClassSize}");
    }
}