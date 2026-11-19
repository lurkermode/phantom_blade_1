using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class HideRewardQuestConditionsFix
{
    private static readonly int[] HiddenConditions =
    {
        7,
        10,
        45,
        46,
    };

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: HideRewardQuestConditionsFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var gameplay = RequiredType(
                    module,
                    "YzrLocal.LocalGameplay");
                ValidateConditions(module);

                var shouldShow = gameplay.Methods.Single(method =>
                    method.Name == "ShouldShowRewardQuest" &&
                    method.IsStatic &&
                    method.Parameters.Count == 2);
                var helper = EnsureHelper(module, gameplay);
                PatchShouldShow(shouldShow, helper);

                assembly.Write(args[1]);
                Console.WriteLine(
                    "Hidden reward quest conditions: 7, 10, 45, 46.");
            }

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

    private static void ValidateConditions(ModuleDefinition module)
    {
        var expected = new[]
        {
            "BuyYuanbao",
            "VIPLevel",
            "UNLOCK_ALL_JIMI",
            "ConsumptionLoopTimes",
        };
        var type = RequiredType(
            module,
            "RewardQuestConditionType");
        for (var index = 0; index < expected.Length; index++)
        {
            var field = type.Fields.Single(candidate =>
                candidate.Name == expected[index] &&
                candidate.IsLiteral);
            if (Convert.ToInt32(field.Constant) !=
                HiddenConditions[index])
            {
                throw new InvalidOperationException(
                    "RewardQuestConditionType." + expected[index] +
                    " changed.");
            }
        }
    }

    private static void PatchShouldShow(
        MethodDefinition shouldShow,
        MethodDefinition helper)
    {
        var existingCall = shouldShow.Body.Instructions.FirstOrDefault(
            instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand == helper);
        if (existingCall != null)
        {
            return;
        }

        var rewardQuestType = RequiredType(
            shouldShow.Module,
            "RewardQuest");
        var conditionType = RequiredType(
            shouldShow.Module,
            "RewardQuestCondition");
        var rewardQuestCondition = rewardQuestType.Fields.Single(field =>
            field.Name == "condition");
        var conditionValue = conditionType.Fields.Single(field =>
            field.Name == "condition");
        var original = shouldShow.Body.Instructions[0];
        var proceed = Instruction.Create(OpCodes.Nop);
        var il = shouldShow.Body.GetILProcessor();
        il.InsertBefore(original, proceed);
        il.InsertBefore(
            proceed,
            Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(
            proceed,
            Instruction.Create(OpCodes.Ldfld, rewardQuestCondition));
        il.InsertBefore(
            proceed,
            Instruction.Create(OpCodes.Ldfld, conditionValue));
        il.InsertBefore(
            proceed,
            Instruction.Create(OpCodes.Call, helper));
        il.InsertBefore(
            proceed,
            Instruction.Create(OpCodes.Brfalse, proceed));
        il.InsertBefore(
            proceed,
            Instruction.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(
            proceed,
            Instruction.Create(OpCodes.Ret));
        shouldShow.Body.MaxStackSize = Math.Max(
            shouldShow.Body.MaxStackSize,
            1);
    }

    private static MethodDefinition EnsureHelper(
        ModuleDefinition module,
        TypeDefinition gameplay)
    {
        var existing = gameplay.Methods.FirstOrDefault(candidate =>
            candidate.Name == "IsHiddenRewardQuestCondition" &&
            candidate.IsStatic &&
            candidate.Parameters.Count == 1);
        if (existing != null)
        {
            return existing;
        }

        var conditionType = RequiredType(
            module,
            "RewardQuestConditionType");
        var method = new MethodDefinition(
            "IsHiddenRewardQuestCondition",
            MethodAttributes.Private |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        method.Parameters.Add(new ParameterDefinition(
            "condition",
            ParameterAttributes.None,
            conditionType));
        gameplay.Methods.Add(method);

        var il = method.Body.GetILProcessor();
        var hidden = Instruction.Create(OpCodes.Ldc_I4_1);
        foreach (var condition in HiddenConditions)
        {
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            AppendInt(il, condition);
            il.Append(Instruction.Create(OpCodes.Beq, hidden));
        }
        il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Append(Instruction.Create(OpCodes.Ret));
        il.Append(hidden);
        il.Append(Instruction.Create(OpCodes.Ret));
        method.Body.MaxStackSize = 2;
        return method;
    }

    private static void AppendInt(ILProcessor il, int value)
    {
        if (value >= -1 && value <= 8)
        {
            il.Append(Instruction.Create(
                OpCodes.Ldc_I4,
                value));
            return;
        }
        if (value <= sbyte.MaxValue)
        {
            il.Append(Instruction.Create(
                OpCodes.Ldc_I4_S,
                (sbyte)value));
            return;
        }
        il.Append(Instruction.Create(OpCodes.Ldc_I4, value));
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
}
