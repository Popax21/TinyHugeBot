using System.Linq;
using System.Collections.Generic;
using AsmResolver.DotNet;
using System;

public partial class Tinyfier {
    private readonly Dictionary<TypeDefinition, int> namePrios = new Dictionary<TypeDefinition, int>();

    public Tinyfier WithNamePriority(TypeDefinition type, int prio) {
        if(didTinyfy) throw new InvalidOperationException("Already tinyfied the module");
        namePrios.Add(type, prio);
        return this;
    }

    private void RenameTypes() {
        //Get a sorted list of types to name
        //Earlier types (with higher priority) get shorter names
        TypeDefinition[] sortedTypes = targetTypes.OrderByDescending(t => namePrios.GetValueOrDefault(t)).ToArray();

        //Rename types as suffixes of already existing strings
        string? suffixStr = null;
        int curSuffixStart = -1, nextStringIdx = 0;
        foreach(TypeDefinition type in sortedTypes) {
            //Get a new string if this one ran out
            if(suffixStr == null || curSuffixStart < 0) {
                while(nextStringIdx < Module.AssemblyReferences.Count && Module.AssemblyReferences[nextStringIdx].Name is null) nextStringIdx++;
                if(nextStringIdx >= Module.AssemblyReferences.Count) throw new Exception("Ran out of strings to use as type name suffix donors");

                suffixStr = Module.AssemblyReferences[nextStringIdx++].Name!.ToString();
                curSuffixStart = suffixStr.Length-1;
            }

            //Rename the type
            type.Namespace = null;
            type.Name = suffixStr[curSuffixStart--..];
        }

        Log("Renamed target types");
    }
}