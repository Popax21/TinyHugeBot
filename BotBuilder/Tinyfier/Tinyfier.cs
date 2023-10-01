using System;
using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.PE.File;

public partial class Tinyfier {
    public readonly ModuleDefinition Module;

    private readonly List<TypeDefinition> targetTypes = new List<TypeDefinition>();
    private readonly HashSet<TypeDefinition> targetTypesSet = new HashSet<TypeDefinition>();
    private bool didTinyfy;

    public Tinyfier(ModuleDefinition module) => Module = module;

    public Tinyfier TinfyModuleMeta() {
        if(didTinyfy) throw new InvalidOperationException("Already tinyfied the module");

        //Tiny-fy module and assembly metadata
        Module.Name = null;
        Module.DebugData.Clear();
        Module.CustomAttributes.Clear();
        Module.ExportedTypes.Clear();
        Module.FileReferences.Clear();

        foreach(AssemblyReference asmRef in Module.AssemblyReferences) {
            asmRef.Culture = null;
            asmRef.HashValue = null;
            asmRef.PublicKeyOrToken = null;
        }

        if(Module.Assembly != null) {
            Module.Assembly.Name = Module.AssemblyReferences[0].Name!.ToString()[^1..]; //We need a non-empty assembly name, so use the prefix of another string
            Module.Assembly.PublicKey = null;
            Module.Assembly.CustomAttributes.Clear();
        }

        Log("Tinyfied module metadata");
        return this;
    }

    private void AddTargetType(TypeDefinition type, bool moduleType = false) {
        if(!targetTypesSet.Add(type)) throw new ArgumentException($"Type {type} is already a target type");
        targetTypes.Add(type);

        //Add the type to the module
        if(!moduleType) Module.TopLevelTypes.Add(type);
        else Module.TopLevelTypes.Insert(0, type);
    }

    private void RemoveTargetType(TypeDefinition type) {
        if(!targetTypesSet.Remove(type)) throw new ArgumentException($"Type {type} is not a target type");
        targetTypes.Remove(type);

        //Remove the type from the module
        Module.TopLevelTypes.Remove(type);
    }

    public Tinyfier TinyfyType(TypeDefinition type) {
        if(didTinyfy) throw new InvalidOperationException("Already tinyfied the module");
        if(targetTypesSet.Add(type)) targetTypes.Add(type);
        return this;
    }

    public Tinyfier TinyfyTypes(params TypeDefinition[] types) => TinyfyTypes((IEnumerable<TypeDefinition>) types);
    public Tinyfier TinyfyTypes(IEnumerable<TypeDefinition> types) {
        foreach(TypeDefinition type in types) TinyfyType(type);
        return this;
    }

    public Tinyfier TinyfyEverything() => TinfyModuleMeta().TinyfyTypes(Module.TopLevelTypes);

    public Tinyfier TinyfyNamespace(string @namespace)
        => TinyfyTypes(Module.TopLevelTypes.Where(t => t.Namespace?.ToString().StartsWith(@namespace) ?? false));

    public Tinyfier TinyfyModuleTypes() {
        if(didTinyfy) throw new InvalidOperationException("Already tinyfied the module");

        //Tinyfy the module type
        if(Module.GetModuleType() is TypeDefinition moduleType) {
            TinyfyType(moduleType);
        }

        //Tinyfy the <PrivateImplementationDetails> type
        if(Module.TopLevelTypes.FirstOrDefault(t => t.Name == "<PrivateImplementationDetails>") is TypeDefinition privImplDetailsType) {
            TinyfyType(privImplDetailsType);
        }

        return this;
    }

    public PEFile Build(bool removeOtherTypes = true) {
        if(didTinyfy) throw new InvalidOperationException("Already tinyfied the module");
        didTinyfy = true;

        //Do tinyfication passes
        FlattenTypes();
        MergeStaticTypes();
        ClearCustomAttributes();
        OptimizeArrayInitialization();
        InlineMethodCalls();
        RelinkTargetReferences();
        TrimTypeMembers();
        // OptimizeMethods();
        RenameTypes();

        //Remove other types if requested
        if(removeOtherTypes) {
            foreach(TypeDefinition type in Module.TopLevelTypes.ToArray()) {
                if(!targetTypes.Contains(type)) Module.TopLevelTypes.Remove(type);
            }
        }

        return BuildPE();
    }

    protected virtual void Log(string msg) => Console.WriteLine("TINYFIER | " + msg);
}