using System;
using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;

public partial class Tinyfier {
    private void MergeStaticTypes() {
        //Collect all "static" target types
        //Note that types without instance fields or methods are also treated as static
        TypeDefinition[] staticTypes = targetTypes.Where(t => t.ClassLayout == null && t.Fields.All(f => f.IsStatic) && t.Methods.All(m => m.IsStatic)).ToArray();

        if(staticTypes.Length <= 0) return;

        //Merge static types into on single type
        List<MethodDefinition> staticCctors = new List<MethodDefinition>();
        TypeDefinition mergedStaticType = new TypeDefinition(null, "<MergedStaticType>", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
        foreach(TypeDefinition staticType in staticTypes) {
            //Steal fields and methods (properties are cleared anyway)
            static T[] StealElements<T>(IList<T> list) {
                T[] elems = list.ToArray();
                list.Clear();
                return elems;
            }

            foreach(FieldDefinition field in StealElements(staticType.Fields)) {
                field.IsStatic = true;
                mergedStaticType.Fields.Add(field);
            }

            foreach(MethodDefinition meth in StealElements(staticType.Methods)) {
                //Keep track static constructors
                if(meth.IsConstructor && meth.IsStatic) staticCctors.Add(meth);

                meth.IsStatic = true;
                mergedStaticType.Methods.Add(meth);
            }
        }

        //Merge static constructors
        if(staticCctors.Count > 1) {
            for(int i = 0; i < staticCctors.Count; i++) {
                MethodDefinition cctor = staticCctors[i];
                cctor.Name = $"<StaticCctor{i}>";
                cctor.IsSpecialName = cctor.IsRuntimeSpecialName = false;
                inlineTargetMethods.Add(cctor); //Inline the cctor into the merged cctor
            }

            //Emit a new cctor which calls all others
            MethodDefinition mergedCctor = new MethodDefinition(".cctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RuntimeSpecialName, MethodSignature.CreateStatic(Module.CorLibTypeFactory.Void));
            mergedCctor.IsSpecialName = mergedCctor.IsRuntimeSpecialName = true;
            mergedStaticType.Methods.Add(mergedCctor);

            CilMethodBody mergedCctorBody = mergedCctor.CilMethodBody = new CilMethodBody(mergedCctor);
            foreach(MethodDefinition cctor in staticCctors) mergedCctorBody.Instructions.Add(CilOpCodes.Call, cctor);
            mergedCctorBody.Instructions.Add(new CilInstruction(CilOpCodes.Ret));
        }

        //Add the merged type from the module, and remove the unmerged types
        //If we merged the module type, then make the new type the module type
        AddTargetType(mergedStaticType, staticTypes.Any(t => t.IsModuleType));
        Array.ForEach(staticTypes, RemoveTargetType);

        Log($"Merged {staticTypes.Length} static types");
    }
}