using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class OfflineOldActivityFilterFix
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: OfflineOldActivityFilterFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var controller = module.GetType("ActivityController");
                if (controller == null)
                {
                    throw new InvalidOperationException(
                        "ActivityController was not found.");
                }

                var selected = EnsureSelectedMethod(module, controller);
                PatchUpdateList(controller, selected);
                assembly.Write(args[1]);
            }

            Console.WriteLine("Filtered old activity catalog to 12 entries.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName);
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static MethodDefinition EnsureSelectedMethod(
        ModuleDefinition module,
        TypeDefinition controller)
    {
        var method = controller.Methods.SingleOrDefault(
            candidate => candidate.Name == "IsSelectedOfflineActivity");
        if (method != null)
        {
            return method;
        }

        method = new MethodDefinition(
            "IsSelectedOfflineActivity",
            MethodAttributes.Public |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        method.Parameters.Add(new ParameterDefinition(
            "activityId",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        method.Body.MaxStackSize = 2;
        controller.Methods.Add(method);

        var secondRange = Instruction.Create(OpCodes.Nop);
        var selected = Instruction.Create(OpCodes.Nop);
        var rejected = Instruction.Create(OpCodes.Nop);
        var il = method.Body.GetILProcessor();

        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4, 11834));
        il.Append(Instruction.Create(OpCodes.Blt, secondRange));
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4, 11841));
        il.Append(Instruction.Create(OpCodes.Ble, selected));

        il.Append(secondRange);
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4, 11853));
        il.Append(Instruction.Create(OpCodes.Blt, rejected));
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4, 11856));
        il.Append(Instruction.Create(OpCodes.Ble, selected));

        il.Append(rejected);
        il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Append(Instruction.Create(OpCodes.Ret));

        il.Append(selected);
        il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
        il.Append(Instruction.Create(OpCodes.Ret));
        return method;
    }

    private static void PatchUpdateList(
        TypeDefinition controller,
        MethodDefinition selected)
    {
        var updateList = controller.Methods.Single(
            method => method.Name == "UpdateActivityList" &&
                      method.Parameters.Count == 1);
        if (updateList.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand == selected))
        {
            return;
        }

        var metadataLocal = updateList.Body.Variables.Single(
            variable => variable.VariableType.FullName == "ActivityMetaData");
        var loopLocal = updateList.Body.Variables[0];
        var metadataStore = updateList.Body.Instructions.First(
            instruction => StoresLocal(instruction, metadataLocal));
        var finalLoopStore = updateList.Body.Instructions
            .Where(instruction => StoresLocal(instruction, loopLocal))
            .Last();
        var increment = finalLoopStore.Previous.Previous.Previous;
        var metadataType = metadataLocal.VariableType.Resolve();
        var idField = metadataType.Fields.Single(field => field.Name == "id");

        var il = updateList.Body.GetILProcessor();
        var loadMetadata = Instruction.Create(OpCodes.Ldloc, metadataLocal);
        var loadId = Instruction.Create(OpCodes.Ldfld, idField);
        var callSelected = Instruction.Create(OpCodes.Call, selected);
        var skip = Instruction.Create(OpCodes.Brfalse, increment);
        il.InsertAfter(metadataStore, loadMetadata);
        il.InsertAfter(loadMetadata, loadId);
        il.InsertAfter(loadId, callSelected);
        il.InsertAfter(callSelected, skip);
    }

    private static bool StoresLocal(
        Instruction instruction,
        VariableDefinition variable)
    {
        if (instruction.Operand == variable)
        {
            return true;
        }

        switch (instruction.OpCode.Code)
        {
            case Code.Stloc_0:
                return variable.Index == 0;
            case Code.Stloc_1:
                return variable.Index == 1;
            case Code.Stloc_2:
                return variable.Index == 2;
            case Code.Stloc_3:
                return variable.Index == 3;
            default:
                return false;
        }
    }
}
