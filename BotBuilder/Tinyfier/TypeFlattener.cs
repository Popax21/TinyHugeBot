using System.Linq;
using AsmResolver.DotNet;

public partial class Tinyfier {
    private void FlattenTypes() {
        //Flatten all top-level target types, and collect nested types as additional targets
        for(int i = 0; i < targetTypes.Count; i++) {
            //Add nested types to the target list
            foreach(TypeDefinition nestedType in targetTypes[i].NestedTypes.ToArray()) {
                //Make private nested types internal
                if(nestedType.IsNestedPrivate) {
                    nestedType.IsNestedPrivate = false;
                    nestedType.IsNotPublic = true;
                }

                targetTypes[i].NestedTypes.Remove(nestedType);
                AddTargetType(nestedType);
            }
        }

        Log("Flattened target type hierarchy");
        Log($" - num. target types: {targetTypes.Count}");
    }
}