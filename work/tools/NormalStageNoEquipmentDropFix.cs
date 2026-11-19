using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class NormalStageNoEquipmentDropFix
{
    private const string FilterMethodName = "FilterNormalEquipmentRewards";

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: NormalStageNoEquipmentDropFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var gameplay = RequiredType(module, "YzrLocal.LocalGameplay");
                var filter = gameplay.Methods.SingleOrDefault(method =>
                    method.Name == FilterMethodName &&
                    method.IsStatic &&
                    method.Parameters.Count == 3);
                if (filter == null)
                {
                    filter = AddFilterMethod(module, gameplay);
                }

                var manualFinish = RequiredMethod(
                    gameplay,
                    "OnLevelFinishLocal");
                var sweepFinish = RequiredMethod(
                    gameplay,
                    "GrantCombatReward");

                PatchCallSite(
                    gameplay,
                    manualFinish,
                    filter,
                    true,
                    1,
                    5,
                    6);
                PatchCallSite(
                    gameplay,
                    sweepFinish,
                    filter,
                    false,
                    0,
                    12,
                    13);

                assembly.Write(args[1]);
                Console.WriteLine(
                    "Patched normal campaign reward lists: equipment is "
                    + "removed before BuildRewardArgs; experience books, "
                    + "breakthrough materials, and all other rewards remain.");
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().FullName);
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }

    private static MethodDefinition AddFilterMethod(
        ModuleDefinition module,
        TypeDefinition gameplay)
    {
        var levelManager = RequiredType(module, "LevelManager");
        var specialData = RequiredType(module, "SpecialSceneData");
        var itemManager = RequiredType(module, "ItemManager");
        var item = RequiredType(module, "Item");

        var getLevelManager = levelManager.Methods.Single(method =>
            method.Name == "get_Instance" &&
            method.IsStatic &&
            method.Parameters.Count == 0);
        var getSpecialData = levelManager.Methods.Single(method =>
            method.Name == "get_specialSceneData" &&
            !method.IsStatic &&
            method.Parameters.Count == 0);
        var getSpecialScene = specialData.Methods.Single(method =>
            method.Name == "GetSpecialSceneBySceneIDWithoutCondiction" &&
            !method.IsStatic &&
            method.Parameters.Count == 1);
        var getItemManager = itemManager.Methods.Single(method =>
            method.Name == "get_Instance" &&
            method.IsStatic &&
            method.Parameters.Count == 0);
        var getPrototype = itemManager.Methods.Single(method =>
            method.Name == "GetItemPrototype" &&
            !method.IsStatic &&
            method.Parameters.Count == 1);
        var itemType = item.Fields.Single(field =>
            field.Name == "itemType");

        var listType = module.ImportReference(
            typeof(System.Collections.Generic.List<int>));
        var getCount = module.ImportReference(
            typeof(System.Collections.Generic.List<int>)
                .GetProperty("Count")
                .GetGetMethod());
        var getItem = module.ImportReference(
            typeof(System.Collections.Generic.List<int>)
                .GetProperty("Item")
                .GetGetMethod());
        var removeAt = module.ImportReference(
            typeof(System.Collections.Generic.List<int>)
                .GetMethod(
                    "RemoveAt",
                    new[] { typeof(int) }));

        var filter = new MethodDefinition(
            FilterMethodName,
            MethodAttributes.Private |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        filter.Parameters.Add(new ParameterDefinition(
            "sceneID",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        filter.Parameters.Add(new ParameterDefinition(
            "items",
            ParameterAttributes.None,
            listType));
        filter.Parameters.Add(new ParameterDefinition(
            "amounts",
            ParameterAttributes.None,
            listType));
        gameplay.Methods.Add(filter);

        var managerLocal = new VariableDefinition(levelManager);
        var specialDataLocal = new VariableDefinition(specialData);
        var indexLocal = new VariableDefinition(module.TypeSystem.Int32);
        var prototypeIDLocal = new VariableDefinition(
            module.TypeSystem.Int32);
        var itemLocal = new VariableDefinition(item);
        filter.Body.Variables.Add(managerLocal);
        filter.Body.Variables.Add(specialDataLocal);
        filter.Body.Variables.Add(indexLocal);
        filter.Body.Variables.Add(prototypeIDLocal);
        filter.Body.Variables.Add(itemLocal);
        filter.Body.InitLocals = true;
        filter.Body.MaxStackSize = 3;

        var il = filter.Body.GetILProcessor();
        var checkSpecial = Instruction.Create(OpCodes.Nop);
        var checkItem = Instruction.Create(OpCodes.Nop);
        var loopCondition = Instruction.Create(OpCodes.Nop);
        var loopBody = Instruction.Create(OpCodes.Nop);
        var nextItem = Instruction.Create(OpCodes.Nop);
        var removeAmount = Instruction.Create(OpCodes.Nop);
        var amountRemoved = Instruction.Create(OpCodes.Nop);
        var returnMethod = Instruction.Create(OpCodes.Ret);

        // Training/PVP scenes are not ordinary campaign levels.
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4, 10000));
        il.Append(Instruction.Create(OpCodes.Blt, checkItem));
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4, 10100));
        il.Append(Instruction.Create(OpCodes.Blt, returnMethod));

        // Special scenes retain their native equipment pools.
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Append(Instruction.Create(OpCodes.Ble, returnMethod));
        il.Append(Instruction.Create(OpCodes.Call, getLevelManager));
        il.Append(Instruction.Create(OpCodes.Stloc, managerLocal));
        il.Append(Instruction.Create(OpCodes.Ldloc, managerLocal));
        il.Append(Instruction.Create(OpCodes.Brfalse, checkItem));
        il.Append(Instruction.Create(OpCodes.Ldloc, managerLocal));
        il.Append(Instruction.Create(OpCodes.Callvirt, getSpecialData));
        il.Append(Instruction.Create(OpCodes.Stloc, specialDataLocal));
        il.Append(Instruction.Create(OpCodes.Ldloc, specialDataLocal));
        il.Append(Instruction.Create(OpCodes.Brfalse, checkItem));
        il.Append(Instruction.Create(OpCodes.Br, checkSpecial));

        il.Append(checkSpecial);
        il.Append(Instruction.Create(OpCodes.Ldloc, specialDataLocal));
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Callvirt, getSpecialScene));
        il.Append(Instruction.Create(OpCodes.Brtrue, returnMethod));

        il.Append(checkItem);
        il.Append(Instruction.Create(OpCodes.Ldarg_1));
        il.Append(Instruction.Create(OpCodes.Brfalse, returnMethod));
        il.Append(Instruction.Create(OpCodes.Ldarg_1));
        il.Append(Instruction.Create(OpCodes.Callvirt, getCount));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
        il.Append(Instruction.Create(OpCodes.Sub));
        il.Append(Instruction.Create(OpCodes.Stloc, indexLocal));
        il.Append(Instruction.Create(OpCodes.Br, loopCondition));

        il.Append(loopBody);
        il.Append(Instruction.Create(OpCodes.Ldarg_1));
        il.Append(Instruction.Create(OpCodes.Ldloc, indexLocal));
        il.Append(Instruction.Create(OpCodes.Callvirt, getItem));
        il.Append(Instruction.Create(OpCodes.Stloc, prototypeIDLocal));
        il.Append(Instruction.Create(OpCodes.Ldloc, prototypeIDLocal));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Append(Instruction.Create(OpCodes.Ble, nextItem));
        il.Append(Instruction.Create(OpCodes.Call, getItemManager));
        il.Append(Instruction.Create(OpCodes.Ldloc, prototypeIDLocal));
        il.Append(Instruction.Create(OpCodes.Callvirt, getPrototype));
        il.Append(Instruction.Create(OpCodes.Stloc, itemLocal));
        il.Append(Instruction.Create(OpCodes.Ldloc, itemLocal));
        il.Append(Instruction.Create(OpCodes.Brfalse, nextItem));
        il.Append(Instruction.Create(OpCodes.Ldloc, itemLocal));
        il.Append(Instruction.Create(OpCodes.Ldfld, itemType));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_2));
        il.Append(Instruction.Create(OpCodes.Bne_Un, nextItem));

        il.Append(Instruction.Create(OpCodes.Ldarg_1));
        il.Append(Instruction.Create(OpCodes.Ldloc, indexLocal));
        il.Append(Instruction.Create(OpCodes.Callvirt, removeAt));
        il.Append(Instruction.Create(OpCodes.Ldarg_2));
        il.Append(Instruction.Create(OpCodes.Brfalse, amountRemoved));
        il.Append(Instruction.Create(OpCodes.Ldloc, indexLocal));
        il.Append(Instruction.Create(OpCodes.Ldarg_2));
        il.Append(Instruction.Create(OpCodes.Callvirt, getCount));
        il.Append(Instruction.Create(OpCodes.Blt, removeAmount));
        il.Append(Instruction.Create(OpCodes.Br, amountRemoved));

        il.Append(removeAmount);
        il.Append(Instruction.Create(OpCodes.Ldarg_2));
        il.Append(Instruction.Create(OpCodes.Ldloc, indexLocal));
        il.Append(Instruction.Create(OpCodes.Callvirt, removeAt));

        il.Append(amountRemoved);
        il.Append(nextItem);
        il.Append(Instruction.Create(OpCodes.Ldloc, indexLocal));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
        il.Append(Instruction.Create(OpCodes.Sub));
        il.Append(Instruction.Create(OpCodes.Stloc, indexLocal));

        il.Append(loopCondition);
        il.Append(Instruction.Create(OpCodes.Ldloc, indexLocal));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_M1));
        il.Append(Instruction.Create(OpCodes.Bgt, loopBody));
        il.Append(returnMethod);

        return filter;
    }

    private static void PatchCallSite(
        TypeDefinition gameplay,
        MethodDefinition method,
        MethodDefinition filter,
        bool sceneIsLocal,
        int sceneIndex,
        int itemsIndex,
        int amountsIndex)
    {
        if (CallsFilter(method, filter))
        {
            return;
        }

        var getClassReward = gameplay.Methods.Single(candidate =>
            candidate.Name == "GetClassReward");
        var call = method.Body.Instructions.SingleOrDefault(instruction =>
        {
            if (instruction.OpCode != OpCodes.Call)
            {
                return false;
            }
            var reference = instruction.Operand as MethodReference;
            return reference != null &&
                reference.Name == "GetClassReward";
        });
        if (call == null)
        {
            throw new InvalidOperationException(
                "GetClassReward call was not found in " + method.Name);
        }

        var scene = sceneIsLocal
            ? Instruction.Create(
                OpCodes.Ldloc,
                method.Body.Variables[sceneIndex])
            : Instruction.Create(OpCodes.Ldarg_0);
        var il = method.Body.GetILProcessor();
        var anchor = call.Next;
        il.InsertBefore(anchor, scene);
        il.InsertBefore(anchor, Instruction.Create(
            OpCodes.Ldloc,
            method.Body.Variables[itemsIndex]));
        il.InsertBefore(anchor, Instruction.Create(
            OpCodes.Ldloc,
            method.Body.Variables[amountsIndex]));
        il.InsertBefore(anchor, Instruction.Create(
            OpCodes.Call,
            filter));
        method.Body.MaxStackSize = Math.Max(
            method.Body.MaxStackSize,
            3);
    }

    private static bool CallsFilter(
        MethodDefinition method,
        MethodDefinition filter)
    {
        return method.Body.Instructions.Any(instruction =>
        {
            var reference = instruction.Operand as MethodReference;
            return instruction.OpCode == OpCodes.Call &&
                reference != null &&
                reference.Name == FilterMethodName;
        });
    }

    private static MethodDefinition RequiredMethod(
        TypeDefinition type,
        string name)
    {
        var method = type.Methods.SingleOrDefault(candidate =>
            candidate.Name == name && candidate.HasBody);
        if (method == null)
        {
            throw new InvalidOperationException(
                type.FullName + "." + name + " was not found.");
        }
        return method;
    }

    private static TypeDefinition RequiredType(
        ModuleDefinition module,
        string name)
    {
        var type = module.GetType(name);
        if (type == null)
        {
            throw new InvalidOperationException("Type not found: " + name);
        }
        return type;
    }

}
