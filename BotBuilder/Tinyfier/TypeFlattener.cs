using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;

public partial class Tinyfier {
    private void FlattenTypes() {
        //Flatten all top-level target types, and collect nested types as additional targets
        for(int i = 0; i < targetTypes.Count; i++) {
            //Add nested types to the target list
            foreach(TypeDefinition nestedType in targetTypes[i].NestedTypes.ToArray()) {
                //Update visibility
                if(nestedType.IsNestedPrivate) {
                    nestedType.Attributes &= ~TypeAttributes.VisibilityMask;
                    nestedType.IsNotPublic = true;
                } else {
                    nestedType.Attributes &= ~TypeAttributes.VisibilityMask;
                    nestedType.IsPublic = targetTypes[i].IsPublic;
                    nestedType.IsNotPublic = targetTypes[i].IsNotPublic;
                }

                targetTypes[i].NestedTypes.Remove(nestedType);
                AddTargetType(nestedType);
            }
        }

        Log("Flattened target type hierarchy");
        Log($" - num. target types: {targetTypes.Count}");
    }
}