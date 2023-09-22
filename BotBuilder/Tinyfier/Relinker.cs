using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Cil;

public partial class Tinyfier {
    private readonly HashSet<TypeDefinition> referencedTypes = new HashSet<TypeDefinition>();
    private readonly HashSet<FieldDefinition> referencedFields = new HashSet<FieldDefinition>();
    private readonly HashSet<MethodDefinition> referencedMethods = new HashSet<MethodDefinition>();

    private readonly Dictionary<TypeDefinition, ITypeDefOrRef> typeRelinkMap = new Dictionary<TypeDefinition, ITypeDefOrRef>();

    public Tinyfier AddExternalReference(TypeDefinition typeDef, bool allPublic = true) {
        referencedTypes.Add(typeDef);
        if(allPublic) {
            referencedMethods.UnionWith(typeDef.Methods.Where(m => m.IsPublic));
            referencedFields.UnionWith(typeDef.Fields.Where(f => f.IsPublic));
        }
        return this;
    }

    public Tinyfier AddExternalReference(FieldDefinition fieldDef) {
        referencedFields.Add(fieldDef);
        return this;
    }

    public Tinyfier AddExternalReference(MethodDefinition methodDef) {
        referencedMethods.Add(methodDef);
        return this;
    }

    private void RelinkTargetReferences() {
        //If the module type is a target type, mark it as referenced
        if(Module.TopLevelTypes is [{} modType, ..] && targetTypesSet.Contains(modType)) referencedTypes.Add(modType);

        //Relink non-target types
        void RelinkNonTargetType(TypeDefinition type) {
            RelinkTypeReferences(type);
            foreach(FieldDefinition field in type.Fields) RelinkFieldReferences(field);
            foreach(MethodDefinition method in type.Methods) RelinkMethodReferences(method);
            foreach(PropertyDefinition prop in type.Properties) prop.Signature = Relink(prop.Signature);
            foreach(TypeDefinition nestedType in type.NestedTypes) RelinkNonTargetType(nestedType);
        } 

        foreach(TypeDefinition type in Module.TopLevelTypes){
            if(!targetTypesSet.Contains(type)) RelinkNonTargetType(type);
        }

        //Relink target types incrementally
        HashSet<TypeDefinition> relinkedTypes = new HashSet<TypeDefinition>();
        HashSet<FieldDefinition> relinkedFields = new HashSet<FieldDefinition>();
        HashSet<MethodDefinition> relinkedMethods = new HashSet<MethodDefinition>();
        Dictionary<MethodDefinition, List<MethodDefinition>> methodOverrides = new Dictionary<MethodDefinition, List<MethodDefinition>>();
        while(relinkedTypes.Count < referencedTypes.Count || relinkedFields.Count < referencedFields.Count || relinkedMethods.Count < referencedMethods.Count) {
            //Determine newly discovered types / fields / methods
            TypeDefinition[] newTypes = referencedTypes.Except(relinkedTypes).ToArray();
            FieldDefinition[] newFields = referencedFields.Except(relinkedFields).ToArray();
            MethodDefinition[] newMethods = referencedMethods.Except(relinkedMethods).ToArray();

            //Keep static constructors of types
            foreach(TypeDefinition newType in newTypes) {
                if(newType.GetStaticConstructor() is MethodDefinition cctor) referencedMethods.Add(cctor);
            }

            //Keep override methods / interface implementations of referenced methods
            bool IsMethodReferenced(IMethodDefOrRef? method) => method != null && (
                method is not MethodDefinition { DeclaringType: TypeDefinition declType } ||
                !targetTypesSet.Contains(declType) ||
                referencedMethods.Contains(method)
            );

            void HandleOverride(MethodDefinition @base, MethodDefinition @override) {
                //If the base method is already referenced, then immediately reference the override method
                //Otherwise do so once the base method becomes referenced (if at all)
                if(IsMethodReferenced(@base)) referencedMethods.Add(@override);
                else {
                    if(!methodOverrides.TryGetValue(@base, out List<MethodDefinition>? overrides)) methodOverrides.Add(@base, overrides = new List<MethodDefinition>());
                    overrides.Add(@override);
                }
            }

            foreach(TypeDefinition type in newTypes) {
                //Handle virtual overrides
                if(type.BaseType?.Resolve() is TypeDefinition baseType) {
                    foreach(MethodDefinition baseMethod in baseType.Methods) {
                        if(!baseMethod.IsVirtual || baseMethod.IsFinal) continue;

                        //Try to find the override method
                        MethodDefinition? ovrMethod = type.Methods.FirstOrDefault(m => m.Name == baseMethod.Name && SignatureComparer.Default.Equals(baseMethod.Signature, m.Signature));
                        if(ovrMethod is { IsVirtual: true, IsNewSlot: false }) HandleOverride(baseMethod, ovrMethod);
                    }
                }

                //Handle interfaces
                foreach(InterfaceImplementation interf in type.Interfaces) {
                    if(interf.Interface?.Resolve() is not TypeDefinition interfType) continue;

                    //Handle interface methods
                    foreach(MethodDefinition interfMethod in interfType.Methods) {
                        MethodDefinition? implMethod = type.Methods.FirstOrDefault(m => m.Name == interfMethod.Name && SignatureComparer.Default.Equals(interfMethod.Signature, m.Signature));
                        if(implMethod != null && !type.MethodImplementations.Any(impl => impl.Body == implMethod)) HandleOverride(interfMethod, implMethod);
                    }

                    //Handle explicit interface implementation                  
                    foreach(MethodImplementation impl in type.MethodImplementations) {
                        if(impl.Declaration?.Resolve() is not MethodDefinition baseMethod) continue;
                        if(impl.Body?.Resolve() is not MethodDefinition ovrMethod) continue;
                        HandleOverride(baseMethod, ovrMethod);
                    }
                }
            }

            //Reference overloads of newly discovered methods
            foreach(MethodDefinition newMethod in newMethods) {
                if(!methodOverrides.TryGetValue(newMethod, out List<MethodDefinition>? overrides)) continue;
                referencedMethods.UnionWith(overrides);
            }

            //Relink the new types / methods / fields
            //Note that we don't relink properties, as they'll be stripped later anyway
            Array.ForEach(newTypes, RelinkTypeReferences);
            Array.ForEach(newFields, RelinkFieldReferences);
            Array.ForEach(newMethods, RelinkMethodReferences);

            relinkedTypes.UnionWith(newTypes);
            relinkedFields.UnionWith(newFields);
            relinkedMethods.UnionWith(newMethods);
        }

        Log($"Relinked target references");
        Log($" - num. referenced target types: {referencedTypes.Count}");
        Log($" - num. referenced target fields: {referencedFields.Count}");
        Log($" - num. referenced target methods: {referencedMethods.Count}");
    }

    private void RelinkTypeReferences(TypeDefinition type) {
        //Relink base type / interface / generics type references
        if(type.BaseType != null) type.BaseType = Relink(type.BaseType);
        foreach(InterfaceImplementation impl in type.Interfaces) impl.Interface = Relink(impl.Interface);

        foreach(GenericParameter param in type.GenericParameters) {
            foreach(GenericParameterConstraint constr in param.Constraints) constr.Constraint = Relink(constr.Constraint);
        }

        //Relink custom attributes
        RelinkAll(type.CustomAttributes, Relink);
        foreach(InterfaceImplementation impl in type.Interfaces) RelinkAll(impl.CustomAttributes, Relink);

        foreach(GenericParameter param in type.GenericParameters) {
            RelinkAll(param.CustomAttributes, Relink);
            foreach(GenericParameterConstraint constr in param.Constraints) RelinkAll(constr.CustomAttributes, Relink);
        }
    }

    private void RelinkFieldReferences(FieldDefinition field) {
        field.Signature = Relink(field.Signature);
        RelinkAll(field.CustomAttributes, Relink);
    }

    private void RelinkMethodReferences(MethodDefinition method) {
        method.Signature = Relink(method.Signature);
        RelinkAll(method.CustomAttributes, Relink);
        foreach(ParameterDefinition param in method.ParameterDefinitions) RelinkAll(param.CustomAttributes, Relink);
        
        //Relink references in method bodies
        if(method.CilMethodBody != null) {
            foreach(CilLocalVariable localVar in method.CilMethodBody.LocalVariables) {
                localVar.VariableType = Relink(localVar.VariableType);
            }

            foreach(CilInstruction instr in method.CilMethodBody.Instructions) {
                if(instr.Operand is IMemberDescriptor memberDescr) instr.Operand = Relink(memberDescr);
            }
        }
    }

    private TypeSignature RelinkTarget(TypeDefinition targetType) {
        //Inline target enum types (this removes references and can cause them to be trimmed out)
        if(targetType.IsEnum) return targetType.GetEnumUnderlyingType()!;

        referencedTypes.Add(targetType);
        return targetType.ToTypeSignature();
    }

    private IFieldDescriptor RelinkTarget(FieldDefinition targetField) {
        referencedFields.Add(targetField);
        referencedTypes.Add(targetField.DeclaringType!);
        return targetField;
    }

    private IMethodDefOrRef RelinkTarget(MethodDefinition targetMethod) {
        referencedMethods.Add(targetMethod);
        referencedTypes.Add(targetMethod.DeclaringType!);
        return targetMethod;
    }

#region DefOrRef Relinkers
    [return: NotNullIfNotNull("typeRef")]
    private ITypeDefOrRef? Relink(ITypeDefOrRef? typeRef) => Relink(typeRef?.ToTypeSignature())?.ToTypeDefOrRef();

    [return: NotNullIfNotNull("methodRef")]
    private IMethodDefOrRef? Relink(IMethodDefOrRef? methodRef) {
        if(methodRef == null) return null;

        RelinkAll(methodRef.CustomAttributes, Relink);
        if(methodRef is MethodDefinition methodDef && methodDef.Module == Module) {
            RelinkAll(methodDef.GenericParameters, Relink);
            foreach(ParameterDefinition paramDef in methodDef.ParameterDefinitions) RelinkAll(paramDef.CustomAttributes, Relink);
        }

        switch(methodRef) {
            case MemberReference memberRef: return Relink(memberRef);
            case MethodDefinition { DeclaringType: TypeDefinition declType } targetMethod when targetTypesSet.Contains(declType):
                return RelinkTarget(targetMethod);
            default: return methodRef;
        }
    }
#endregion

#region Member Relinkers
    [return: NotNullIfNotNull("memberRefParent")]
    private IMemberRefParent? Relink(IMemberRefParent? memberRefParent) => memberRefParent switch {
        ITypeDefOrRef typeRef => Relink(typeRef),
        _ => memberRefParent
    };

    [return: NotNullIfNotNull("memberRef")]
    private MemberReference? Relink(MemberReference? memberRef) {
        if(memberRef == null) return null;

        RelinkAll(memberRef.CustomAttributes, Relink);
        memberRef.Signature = Relink(memberRef.Signature);
        memberRef.Parent = Relink(memberRef.Parent);
        return memberRef;
    }
#endregion

#region Descriptor Relinkers
    [return: NotNullIfNotNull("typeDescr")]
    private IMemberDescriptor? Relink(IMemberDescriptor? memberDescr) => memberDescr switch {
        ITypeDescriptor typeDescr => Relink(typeDescr),
        MemberReference memberRef => Relink(memberRef),
        IFieldDescriptor fieldDescr => Relink(fieldDescr),
        IMethodDescriptor methodDescr => Relink(methodDescr),
        _ => memberDescr
    };

    [return: NotNullIfNotNull("typeDescr")]
    private ITypeDescriptor? Relink(ITypeDescriptor? typeDescr) => typeDescr switch {
        ITypeDefOrRef typeRef => Relink(typeRef),
        TypeSignature typeSig => Relink(typeSig),
        _ => typeDescr
    };

    [return: NotNullIfNotNull("fieldDescr")]
    private IFieldDescriptor? Relink(IFieldDescriptor? fieldDescr)  => fieldDescr  switch {
        MemberReference memberRef => Relink(memberRef),
        {} when fieldDescr?.Resolve() is FieldDefinition { DeclaringType: TypeDefinition declType } fieldDef && targetTypesSet.Contains(declType) => RelinkTarget(fieldDef),
        _ => fieldDescr,
    };

    [return: NotNullIfNotNull("methodDescr")]
    private IMethodDescriptor? Relink(IMethodDescriptor? methodDescr) {
        switch(methodDescr) {
            case IMethodDefOrRef methodRef: return Relink(methodRef);
            case MethodSpecification methodSpec: {
                methodSpec.Method = Relink(methodSpec.Method);
                if(methodSpec.Signature != null) RelinkAll(methodSpec.Signature.TypeArguments, Relink);
            } return methodSpec;
            default: return methodDescr;
        }
    }

    [return: NotNullIfNotNull("methodDescr")]
    private ICustomAttributeType? Relink(ICustomAttributeType? customAttrType) => (ICustomAttributeType?) Relink((IMethodDescriptor?) customAttrType);
#endregion

#region Signature Relinkers
    [return: NotNullIfNotNull("typeSig")]
    private CallingConventionSignature? Relink(CallingConventionSignature? memberSig) => memberSig switch {
        FieldSignature fieldSig => Relink(fieldSig),
        MethodSignature methodSig => Relink(methodSig),
        _ => memberSig
    };

    [return: NotNullIfNotNull("typeSig")]
    private TypeSignature? Relink(TypeSignature? typeSig) {
        switch(typeSig) {
            case GenericInstanceTypeSignature instSig: {
                ITypeDefOrRef relinkedGenericType = Relink(instSig.GenericType);
                if(instSig.GenericType != relinkedGenericType) instSig.GenericType = relinkedGenericType; //Only assign if different, otherwise IsValueType is reset
                RelinkAll(instSig.TypeArguments, Relink);
                return instSig;
            }
            case TypeDefOrRefSignature typeRefSig: {
                //Check if we should relink this type
                if(typeRefSig.Type is TypeDefinition relinkTypeDef && typeRelinkMap.TryGetValue(relinkTypeDef, out ITypeDefOrRef? relinkTarget)) typeRefSig.Type = relinkTarget;

                RelinkAll(typeRefSig.Type.CustomAttributes, Relink);
                if(typeRefSig.Type is TypeDefinition typeDef && typeDef.Module == Module) RelinkAll(typeDef.GenericParameters, Relink);

                //Handle target types
                if(typeRefSig.Type is TypeDefinition targetType && targetTypesSet.Contains(targetType)) return RelinkTarget(targetType);

                return typeRefSig;
            };
            default: return typeSig;
        }
    }

    [return: NotNullIfNotNull("fieldSig")]
    private FieldSignature? Relink(FieldSignature? fieldSig) {
        if(fieldSig == null) return null;
        fieldSig.FieldType = Relink(fieldSig.FieldType);
        return fieldSig;
    }

    [return: NotNullIfNotNull("methodSig")]
    private MethodSignature? Relink(MethodSignature? methodSig) {
        if(methodSig == null) return null;
        methodSig.ReturnType = Relink(methodSig.ReturnType);
        RelinkAll(methodSig.ParameterTypes, Relink);
        RelinkAll(methodSig.SentinelParameterTypes, Relink);
        return methodSig;
    }

    [return: NotNullIfNotNull("propSig")]
    private PropertySignature? Relink(PropertySignature? propSig) {
        if(propSig == null) return null;
        propSig.ReturnType = Relink(propSig.ReturnType);
        RelinkAll(propSig.ParameterTypes, Relink);
        RelinkAll(propSig.SentinelParameterTypes, Relink);
        return propSig;
    }
#endregion

    [return: NotNullIfNotNull("param")]
    private GenericParameter? Relink(GenericParameter? param) {
        if(param == null) return null;
        RelinkAll(param.CustomAttributes, Relink);
        foreach(GenericParameterConstraint constr in param.Constraints) RelinkAll(constr.CustomAttributes, Relink);
        return param;
    }

    [return: NotNullIfNotNull("attr")]
    private CustomAttribute? Relink(CustomAttribute? attr) {
        if(attr == null) return null;
        attr.Constructor = Relink(attr.Constructor);
        return attr;
    }

    private void RelinkAll<T>(IList<T> vals, Func<T, T> relinker) {
        for(int i = 0; i < vals.Count; i++) vals[i] = relinker(vals[i]);    
    }
}