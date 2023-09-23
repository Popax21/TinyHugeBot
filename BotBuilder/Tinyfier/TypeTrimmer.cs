using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

public partial class Tinyfier {
    private void ClearCustomAttributes() {
        int numClearedAttrs = 0;

        void ClearAttrHaver(IHasCustomAttribute attrsHaver) {
            numClearedAttrs += attrsHaver.CustomAttributes.Count;
            attrsHaver.CustomAttributes.Clear();
        } 

        void ClearAttrHavers(IEnumerable<IHasCustomAttribute> attrsHavers) {
            foreach(IHasCustomAttribute attrHaver in attrsHavers) ClearAttrHaver(attrHaver);
        } 

        foreach(TypeDefinition type in targetTypes) {
            ClearAttrHaver(type);
            ClearAttrHavers(type.Fields);
            ClearAttrHavers(type.Methods);
            ClearAttrHavers(type.Properties);
            ClearAttrHavers(type.Interfaces);
            ClearAttrHavers(type.GenericParameters);

            foreach(MethodDefinition method in type.Methods) ClearAttrHavers(method.ParameterDefinitions);
            foreach(GenericParameter param in type.GenericParameters) ClearAttrHavers(param.Constraints);
        }

        Log($"Cleared {numClearedAttrs} custom attributes");
    }

    private void TrimTypeMembers() {
        int numTrimmedTypes = 0, numTrimmedFields = 0, numTrimmedMethods = 0, numTrimmedProperties = 0;
        foreach(TypeDefinition type in targetTypes.ToArray()) {
            //Check if the type is referenced
            if(!referencedTypes.Contains(type)) {
                RemoveTargetType(type);
                numTrimmedTypes++;
                continue;
            }

            //Trim unreferenced / constant fields
            foreach(FieldDefinition field in type.Fields.ToArray()) {
                if(field.Constant == null && referencedFields.Contains(field)) continue;
                type.Fields.Remove(field);
                numTrimmedFields++;
            }

            //Trim unreferenced methods
            foreach(MethodDefinition method in type.Methods.ToArray()) {
                if(referencedMethods.Contains(method)) continue;
                type.Methods.Remove(method);
                numTrimmedMethods++;
            }

            //Trim all properties (they are only metadata on a CIL level)
            numTrimmedProperties += type.Properties.Count;
            type.Properties.Clear();

            //Trim parameter definitions
            foreach(MethodDefinition meth in type.Methods) meth.ParameterDefinitions.Clear();

            //Trim field and method names, except for special name methods and overrides
            //Don't trim delegate type methods, that will confuse the runtime
            if(!type.IsDelegate) {
                foreach(FieldDefinition field in type.Fields) field.Name = null;
                foreach(MethodDefinition method in type.Methods) {
                    if(method.IsSpecialName || method.IsRuntimeSpecialName) continue;

                    //Check if this is a virtual override
                    if(method.IsVirtual && !method.IsNewSlot && type.BaseType?.Resolve() is TypeDefinition baseType) {
                        if(baseType.Methods.Any(m => m.IsVirtual && !m.IsFinal && m.Name == method.Name && SignatureComparer.Default.Equals(m.Signature, method.Signature))) continue;
                    }

                    //Check if this is an interface implementation (note that explicit interface implementations don't count)
                    if(!type.MethodImplementations.Any(impl => impl.Body == this)) {
                        bool isInterfImpl = false;
                        foreach(InterfaceImplementation interfImpl in type.Interfaces) {
                            if(interfImpl.Interface?.Resolve() is not TypeDefinition interfType) continue;

                            MethodDefinition? interfMethod = interfType.Methods.FirstOrDefault(m => m.Name == method.Name && SignatureComparer.Default.Equals(m.Signature, method.Signature));
                            if(interfMethod != null && !type.MethodImplementations.Any(impl => impl.Declaration == interfMethod)) {
                                isInterfImpl = true;
                                break;
                            }
                        }
                        if(isInterfImpl) continue;
                    }

                    method.Name = null;
                }
            }
        }

        Log($"Trimmed {numTrimmedTypes + numTrimmedFields + numTrimmedMethods + numTrimmedProperties} members");
        Log($" - trimmed types: {numTrimmedTypes}");
        Log($" - trimmed fields: {numTrimmedFields}");
        Log($" - trimmed methods: {numTrimmedMethods}");
        Log($" - trimmed properties: {numTrimmedProperties}");
    }
}