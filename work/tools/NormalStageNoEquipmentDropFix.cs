using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class NormalStageNoEquipmentDropFix
{
    private const string RuleTypeName = "YzrLocal.NormalStageDropRules";
    private const string RuleMethodName = "ShouldSkipEquipmentDrop";

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
                var combat = RequiredType(module, "CombatDataManager");
                var finish = combat.Methods.SingleOrDefault(method =>
                    method.Name == "FinishVirtualCombat" &&
                    method.HasBody &&
                    method.Parameters.Count == 2);
                if (finish == null)
                {
                    throw new InvalidOperationException(
                        "CombatDataManager.FinishVirtualCombat was not found.");
                }

                var rules = module.GetType(RuleTypeName);
                if (rules == null)
                {
                    rules = AddRulesType(module);
                }

                var ruleMethod = rules.Methods.SingleOrDefault(method =>
                    method.Name == RuleMethodName &&
                    method.IsStatic &&
                    method.ReturnType.MetadataType == MetadataType.Boolean &&
                    method.Parameters.Count == 2);
                if (ruleMethod == null)
                {
                    throw new InvalidOperationException(
                        RuleMethodName + " was not found.");
                }

                if (!CallsRuleMethod(finish, ruleMethod))
                {
                    InjectRuleCall(finish, ruleMethod);
                }

                assembly.Write(args[1]);
                Console.WriteLine(
                    "Patched normal campaign settlement: equipment drops are "
                    + "discarded after their normal roll; all non-equipment "
                    + "rewards remain unchanged.");
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

    private static TypeDefinition AddRulesType(ModuleDefinition module)
    {
        var rules = new TypeDefinition(
            "YzrLocal",
            "NormalStageDropRules",
            TypeAttributes.Public |
            TypeAttributes.Abstract |
            TypeAttributes.Sealed |
            TypeAttributes.BeforeFieldInit,
            module.TypeSystem.Object);
        module.Types.Add(rules);

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
            method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.MetadataType ==
                MetadataType.Int32);
        var getItemManager = itemManager.Methods.Single(method =>
            method.Name == "get_Instance" &&
            method.IsStatic &&
            method.Parameters.Count == 0);
        var getPrototype = itemManager.Methods.Single(method =>
            method.Name == "GetItemPrototype" &&
            !method.IsStatic &&
            method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.MetadataType ==
                MetadataType.Int32);
        var itemType = item.Fields.Single(field =>
            field.Name == "itemType");

        var shouldSkip = new MethodDefinition(
            RuleMethodName,
            MethodAttributes.Public |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        shouldSkip.Parameters.Add(new ParameterDefinition(
            "sceneID",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        shouldSkip.Parameters.Add(new ParameterDefinition(
            "itemID",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        rules.Methods.Add(shouldSkip);

        var managerLocal = new VariableDefinition(levelManager);
        var specialDataLocal = new VariableDefinition(specialData);
        var itemLocal = new VariableDefinition(item);
        shouldSkip.Body.Variables.Add(managerLocal);
        shouldSkip.Body.Variables.Add(specialDataLocal);
        shouldSkip.Body.Variables.Add(itemLocal);
        shouldSkip.Body.InitLocals = true;
        shouldSkip.Body.MaxStackSize = 3;

        var il = shouldSkip.Body.GetILProcessor();
        var checkSpecial = Instruction.Create(OpCodes.Nop);
        var checkItem = Instruction.Create(OpCodes.Nop);
        var returnFalse = Instruction.Create(OpCodes.Nop);

        // Training/PVP scene IDs are not campaign levels.
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4, 10000));
        il.Append(Instruction.Create(OpCodes.Blt, checkItem));
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4, 10100));
        il.Append(Instruction.Create(OpCodes.Blt, returnFalse));

        // Special scenes keep their original equipment pools.
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Append(Instruction.Create(OpCodes.Ble, returnFalse));
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
        il.Append(Instruction.Create(OpCodes.Brtrue, returnFalse));

        il.Append(checkItem);
        il.Append(Instruction.Create(OpCodes.Call, getItemManager));
        il.Append(Instruction.Create(OpCodes.Ldarg_1));
        il.Append(Instruction.Create(OpCodes.Callvirt, getPrototype));
        il.Append(Instruction.Create(OpCodes.Stloc, itemLocal));
        il.Append(Instruction.Create(OpCodes.Ldloc, itemLocal));
        il.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
        il.Append(Instruction.Create(OpCodes.Ldloc, itemLocal));
        il.Append(Instruction.Create(OpCodes.Ldfld, itemType));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_2));
        il.Append(Instruction.Create(OpCodes.Ceq));
        il.Append(Instruction.Create(OpCodes.Ret));

        il.Append(returnFalse);
        il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Append(Instruction.Create(OpCodes.Ret));
        return rules;
    }

    private static bool CallsRuleMethod(
        MethodDefinition finish,
        MethodDefinition ruleMethod)
    {
        return finish.Body.Instructions.Any(instruction =>
        {
            if (instruction.OpCode != OpCodes.Call)
            {
                return false;
            }
            var reference = instruction.Operand as MethodReference;
            return reference != null &&
                reference.Name == RuleMethodName &&
                reference.DeclaringType.FullName == RuleTypeName;
        });
    }

    private static void InjectRuleCall(
        MethodDefinition finish,
        MethodDefinition ruleMethod)
    {
        var addItem = finish.Body.Instructions.FirstOrDefault(instruction =>
        {
            var reference = instruction.Operand as MethodReference;
            var declaringType = reference == null
                ? null
                : reference.DeclaringType as GenericInstanceType;
            var previous = instruction.Previous;
            return instruction.OpCode == OpCodes.Callvirt &&
                reference != null &&
                reference.Name == "Add" &&
                declaringType != null &&
                declaringType.ElementType.FullName ==
                    "System.Collections.Generic.List`1" &&
                declaringType.GenericArguments.Count == 1 &&
                declaringType.GenericArguments[0].MetadataType ==
                    MetadataType.Int32 &&
                previous != null &&
                (previous.OpCode == OpCodes.Ldloc_S ||
                 previous.OpCode == OpCodes.Ldloc);
        });
        if (addItem == null)
        {
            throw new InvalidOperationException(
                "The item-list Add call was not found.");
        }

        var itemLoad = addItem.Previous;
        var listLoad = itemLoad == null ? null : itemLoad.Previous;
        if (itemLoad == null ||
            listLoad == null ||
            (itemLoad.OpCode != OpCodes.Ldloc_S &&
             itemLoad.OpCode != OpCodes.Ldloc) ||
            (listLoad.OpCode != OpCodes.Ldloc_S &&
             listLoad.OpCode != OpCodes.Ldloc &&
             listLoad.OpCode != OpCodes.Ldloc_0 &&
             listLoad.OpCode != OpCodes.Ldloc_1 &&
             listLoad.OpCode != OpCodes.Ldloc_2 &&
             listLoad.OpCode != OpCodes.Ldloc_3) ||
            !(itemLoad.Operand is VariableDefinition))
        {
            throw new InvalidOperationException(
                "Unexpected item-list Add sequence.");
        }

        var il = finish.Body.GetILProcessor();
        var addIt = Instruction.Create(OpCodes.Nop);
        il.InsertBefore(listLoad, Instruction.Create(OpCodes.Ldarg_1));
        il.InsertBefore(listLoad, Instruction.Create(
            OpCodes.Ldloc,
            itemLoad.Operand as VariableDefinition));
        il.InsertBefore(listLoad, Instruction.Create(
            OpCodes.Call,
            ruleMethod));
        il.InsertBefore(listLoad, Instruction.Create(
            OpCodes.Brfalse,
            addIt));
        il.InsertBefore(listLoad, Instruction.Create(
            OpCodes.Br,
            addItem.Next));
        il.InsertBefore(listLoad, addIt);
        finish.Body.MaxStackSize = Math.Max(
            finish.Body.MaxStackSize,
            3);
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
