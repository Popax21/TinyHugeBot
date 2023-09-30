using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;

public partial class Tinyfier {
    private void FlattenTypes() {
        //Flatten all top-level target types, and collect nested types as additional targets
        for(int i = 0; i < targetTypes.Count; i++) {
            //Add nested types to the target list
            TypeDefinition targetType = targetTypes[i];
            foreach(TypeDefinition nestedType in targetType.NestedTypes.ToArray()) {
                //Update visibility
                if(nestedType.IsNestedPrivate) {
                    nestedType.Attributes &= ~TypeAttributes.VisibilityMask;
                    nestedType.IsNotPublic = true;
                } else {
                    nestedType.Attributes &= ~TypeAttributes.VisibilityMask;
                    nestedType.IsPublic = targetType.IsPublic;
                    nestedType.IsNotPublic = targetType.IsNotPublic;
                }

                //Ensure every top-level member is at least internal
                foreach(FieldDefinition field in targetType.Fields) {
                    if(!field.IsPrivate) continue;
                    field.IsPrivate = false;
                    field.IsAssembly = true;
                }

                foreach(MethodDefinition method in targetType.Methods) {
                    if(!method.IsPrivate) continue;
                    method.IsPrivate = false;
                    method.IsAssembly = true;
                }

                targetTypes[i].NestedTypes.Remove(nestedType);
                AddTargetType(nestedType);
            }
        }

        Log("Flattened target type hierarchy");
        Log($" - num. target types: {targetTypes.Count}");
    }
}