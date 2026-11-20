using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class ChallengeHeartDoubleRollFix
{
    private const int HitWeight = 9803;
    private const int TotalWeight = 9999;
    private const int RollCount = 2;
    private const int MinimumAmount = 1;
    private const int MaximumAmount = 4;

    private static readonly KeyValuePair<int, int>[] Scenes =
    {
        new KeyValuePair<int, int>(21004, 920014),
        new KeyValuePair<int, int>(21504, 920016),
        new KeyValuePair<int, int>(22004, 920015),
        new KeyValuePair<int, int>(30504, 920032),
        new KeyValuePair<int, int>(31004, 920011),
        new KeyValuePair<int, int>(31504, 920027),
        new KeyValuePair<int, int>(32004, 920030),
        new KeyValuePair<int, int>(40504, 920032),
        new KeyValuePair<int, int>(40703, 920003),
        new KeyValuePair<int, int>(40804, 920007),
        new KeyValuePair<int, int>(41203, 920009),
        new KeyValuePair<int, int>(41504, 920031),
        new KeyValuePair<int, int>(41703, 920001),
        new KeyValuePair<int, int>(41904, 920028),
        new KeyValuePair<int, int>(60403, 920044),
        new KeyValuePair<int, int>(62003, 920043),
        new KeyValuePair<int, int>(62503, 920042),
        new KeyValuePair<int, int>(64003, 920045),
    };

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: ChallengeHeartDoubleRollFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var combat = RequiredType(module, "CombatDataManager");
                var rewardType = RequiredType(module, "QuestReward");
                var helperType = EnsureHelperType(module);
                var fragmentMethod = EnsureFragmentMethod(
                    module,
                    helperType);
                var applyMethod = EnsureApplyMethod(
                    module,
                    helperType,
                    fragmentMethod,
                    rewardType);

                PatchFinishVirtualCombat(combat, applyMethod);

                assembly.Write(args[1]);
                Console.WriteLine(
                    "Challenge heart fragments now use two independent "
                    + "98.0398% rolls with a random amount of 1-4.");
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

    private static TypeDefinition EnsureHelperType(
        ModuleDefinition module)
    {
        const string typeName =
            "YzrLocal.ChallengeHeartDoubleRollFix";
        var type = module.GetType(typeName);
        if (type != null)
        {
            return type;
        }

        type = new TypeDefinition(
            "YzrLocal",
            "ChallengeHeartDoubleRollFix",
            TypeAttributes.Public |
            TypeAttributes.Abstract |
            TypeAttributes.Sealed |
            TypeAttributes.Class |
            TypeAttributes.BeforeFieldInit,
            module.TypeSystem.Object);
        module.Types.Add(type);
        return type;
    }

    private static MethodDefinition EnsureFragmentMethod(
        ModuleDefinition module,
        TypeDefinition helperType)
    {
        var existing = helperType.Methods.FirstOrDefault(candidate =>
            candidate.Name == "GetChallengeHeartFragment" &&
            candidate.IsStatic &&
            candidate.Parameters.Count == 1);
        if (existing != null)
        {
            return existing;
        }

        var fragmentMethod = new MethodDefinition(
            "GetChallengeHeartFragment",
            MethodAttributes.Private |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Int32);
        fragmentMethod.Parameters.Add(new ParameterDefinition(
            "sceneId",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        helperType.Methods.Add(fragmentMethod);

        var result = new VariableDefinition(module.TypeSystem.Int32);
        fragmentMethod.Body.Variables.Add(result);
        fragmentMethod.Body.InitLocals = true;
        fragmentMethod.Body.MaxStackSize = 2;

        var il = fragmentMethod.Body.GetILProcessor();
        var loadResult = Instruction.Create(OpCodes.Ldloc, result);
        Append(
            il,
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, result));

        foreach (var scene in Scenes)
        {
            var next = Instruction.Create(OpCodes.Nop);
            Append(
                il,
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldc_I4, scene.Key),
                Instruction.Create(OpCodes.Bne_Un, next),
                Instruction.Create(OpCodes.Ldc_I4, scene.Value),
                Instruction.Create(OpCodes.Stloc, result));
            il.Append(Instruction.Create(OpCodes.Br, loadResult));
            il.Append(next);
        }

        il.Append(loadResult);
        il.Append(Instruction.Create(OpCodes.Ret));
        return fragmentMethod;
    }

    private static MethodDefinition EnsureApplyMethod(
        ModuleDefinition module,
        TypeDefinition helperType,
        MethodDefinition fragmentMethod,
        TypeDefinition rewardType)
    {
        var existing = helperType.Methods.FirstOrDefault(candidate =>
            candidate.Name == "Apply" &&
            candidate.IsStatic &&
            candidate.Parameters.Count == 2);
        if (existing != null)
        {
            return existing;
        }

        var listType = module.ImportReference(
            typeof(List<int>));
        var applyMethod = new MethodDefinition(
            "Apply",
            MethodAttributes.Public |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        applyMethod.Parameters.Add(new ParameterDefinition(
            "sceneId",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        applyMethod.Parameters.Add(new ParameterDefinition(
            "reward",
            ParameterAttributes.None,
            rewardType));
        helperType.Methods.Add(applyMethod);

        var itemsField = rewardType.Fields.Single(field =>
            field.Name == "items_general");
        var amountsField = rewardType.Fields.Single(field =>
            field.Name == "amounts_general");
        var randomRange = FindUnityRandomRange(module);
        var getCount = module.ImportReference(
            typeof(List<int>).GetProperty("Count").GetGetMethod());
        var getItem = module.ImportReference(
            typeof(List<int>).GetProperty("Item").GetGetMethod());
        var removeAt = module.ImportReference(
            typeof(List<int>).GetMethod(
                "RemoveAt",
                new[] { typeof(int) }));
        var add = module.ImportReference(
            typeof(List<int>).GetMethod(
                "Add",
                new[] { typeof(int) }));

        var fragment = new VariableDefinition(module.TypeSystem.Int32);
        var index = new VariableDefinition(module.TypeSystem.Int32);
        var roll = new VariableDefinition(module.TypeSystem.Int32);
        var amount = new VariableDefinition(module.TypeSystem.Int32);
        var items = new VariableDefinition(listType);
        var amounts = new VariableDefinition(listType);
        applyMethod.Body.Variables.Add(fragment);
        applyMethod.Body.Variables.Add(index);
        applyMethod.Body.Variables.Add(roll);
        applyMethod.Body.Variables.Add(amount);
        applyMethod.Body.Variables.Add(items);
        applyMethod.Body.Variables.Add(amounts);
        applyMethod.Body.InitLocals = true;
        applyMethod.Body.MaxStackSize = 3;

        var il = applyMethod.Body.GetILProcessor();
        var invalidScene = Instruction.Create(OpCodes.Ret);
        var missingLists = Instruction.Create(OpCodes.Ret);
        var removeLoop = Instruction.Create(OpCodes.Nop);
        var removeCondition = Instruction.Create(OpCodes.Nop);
        var removeNext = Instruction.Create(OpCodes.Nop);
        var removeAmount = Instruction.Create(OpCodes.Nop);
        var addRoll = Instruction.Create(OpCodes.Nop);
        var rollLoop = Instruction.Create(OpCodes.Nop);
        var rollCondition = Instruction.Create(OpCodes.Nop);
        var rollNext = Instruction.Create(OpCodes.Nop);
        var done = Instruction.Create(OpCodes.Ret);

        Append(
            il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, fragmentMethod),
            Instruction.Create(OpCodes.Stloc, fragment),
            Instruction.Create(OpCodes.Ldloc, fragment),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Ble, invalidScene),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldfld, itemsField),
            Instruction.Create(OpCodes.Stloc, items),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldfld, amountsField),
            Instruction.Create(OpCodes.Stloc, amounts),
            Instruction.Create(OpCodes.Ldloc, items),
            Instruction.Create(OpCodes.Brfalse, missingLists),
            Instruction.Create(OpCodes.Ldloc, amounts),
            Instruction.Create(OpCodes.Brfalse, missingLists),
            Instruction.Create(OpCodes.Ldloc, items),
            Instruction.Create(OpCodes.Callvirt, getCount),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, removeCondition),
            removeLoop,
            Instruction.Create(OpCodes.Ldloc, items),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Callvirt, getItem),
            Instruction.Create(OpCodes.Ldloc, fragment),
            Instruction.Create(OpCodes.Bne_Un, removeNext),
            Instruction.Create(OpCodes.Ldloc, items),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Callvirt, removeAt),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldloc, amounts),
            Instruction.Create(OpCodes.Callvirt, getCount),
            Instruction.Create(OpCodes.Blt, removeAmount),
            Instruction.Create(OpCodes.Br, removeNext),
            removeAmount,
            Instruction.Create(OpCodes.Ldloc, amounts),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Callvirt, removeAt),
            removeNext,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(OpCodes.Stloc, index),
            removeCondition,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_M1),
            Instruction.Create(OpCodes.Bgt, removeLoop),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, roll),
            Instruction.Create(OpCodes.Br, rollCondition),
            rollLoop,
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Ldc_I4, TotalWeight),
            Instruction.Create(OpCodes.Call, randomRange),
            Instruction.Create(OpCodes.Ldc_I4, HitWeight),
            Instruction.Create(OpCodes.Bge, rollNext),
            Instruction.Create(OpCodes.Ldc_I4, MinimumAmount),
            Instruction.Create(OpCodes.Ldc_I4, MaximumAmount + 1),
            Instruction.Create(OpCodes.Call, randomRange),
            Instruction.Create(OpCodes.Stloc, amount),
            addRoll,
            Instruction.Create(OpCodes.Ldloc, items),
            Instruction.Create(OpCodes.Ldloc, fragment),
            Instruction.Create(OpCodes.Callvirt, add),
            Instruction.Create(OpCodes.Ldloc, amounts),
            Instruction.Create(OpCodes.Ldloc, amount),
            Instruction.Create(OpCodes.Callvirt, add),
            rollNext,
            Instruction.Create(OpCodes.Ldloc, roll),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, roll),
            rollCondition,
            Instruction.Create(OpCodes.Ldloc, roll),
            Instruction.Create(OpCodes.Ldc_I4, RollCount),
            Instruction.Create(OpCodes.Blt, rollLoop),
            done,
            invalidScene,
            missingLists);

        return applyMethod;
    }

    private static void PatchFinishVirtualCombat(
        TypeDefinition combat,
        MethodDefinition applyMethod)
    {
        var finish = combat.Methods.Single(method =>
            method.Name == "FinishVirtualCombat" &&
            !method.IsStatic &&
            method.Parameters.Count == 2);
        if (!finish.HasBody)
        {
            throw new InvalidOperationException(
                "FinishVirtualCombat has no body.");
        }

        var instructions = finish.Body.Instructions;
        if (instructions.Any(instruction =>
            (instruction.OpCode == OpCodes.Call ||
             instruction.OpCode == OpCodes.Callvirt) &&
            instruction.Operand == applyMethod))
        {
            return;
        }

        var finalReturn = instructions.Last(instruction =>
            instruction.OpCode == OpCodes.Ret);
        var rewardLoad = finalReturn.Previous;
        if (rewardLoad == null ||
            (rewardLoad.OpCode != OpCodes.Ldloc_3 &&
             rewardLoad.OpCode != OpCodes.Ldloc_S &&
             rewardLoad.OpCode != OpCodes.Ldloc))
        {
            throw new InvalidOperationException(
                "Unexpected FinishVirtualCombat reward return.");
        }

        var il = finish.Body.GetILProcessor();
        il.InsertBefore(
            finalReturn,
            Instruction.Create(OpCodes.Ldarg_1));
        il.InsertBefore(
            finalReturn,
            CloneLocalLoad(rewardLoad));
        il.InsertBefore(
            finalReturn,
            Instruction.Create(OpCodes.Call, applyMethod));
        finish.Body.MaxStackSize = Math.Max(
            finish.Body.MaxStackSize,
            3);
    }

    private static Instruction CloneLocalLoad(
        Instruction instruction)
    {
        var variable = instruction.Operand as VariableDefinition;
        if (variable != null)
        {
            return Instruction.Create(
                instruction.OpCode,
                variable);
        }

        if (instruction.Operand is int)
        {
            return Instruction.Create(
                instruction.OpCode,
                (int)instruction.Operand);
        }

        return Instruction.Create(instruction.OpCode);
    }

    private static MethodReference FindUnityRandomRange(
        ModuleDefinition module)
    {
        var randomRange = module.Types
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .FirstOrDefault(candidate =>
                candidate.Name == "Range" &&
                candidate.DeclaringType.FullName ==
                    "UnityEngine.Random" &&
                candidate.Parameters.Count == 2 &&
                candidate.Parameters[0].ParameterType.MetadataType ==
                    MetadataType.Int32 &&
                candidate.Parameters[1].ParameterType.MetadataType ==
                    MetadataType.Int32);
        if (randomRange == null)
        {
            throw new InvalidOperationException(
                "UnityEngine.Random.Range(int,int) was not found.");
        }
        return module.ImportReference(randomRange);
    }

    private static TypeDefinition RequiredType(
        ModuleDefinition module,
        string name)
    {
        var type = module.GetType(name);
        if (type == null)
        {
            throw new InvalidOperationException(
                "Type not found: " + name);
        }
        return type;
    }

    private static void Append(
        ILProcessor il,
        params Instruction[] instructions)
    {
        foreach (var instruction in instructions)
        {
            il.Append(instruction);
        }
    }
}
