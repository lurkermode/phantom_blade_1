using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class CombatPowerQuestProgressFix
{
    private const int CombatPowerCondition = 37;

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: CombatPowerQuestProgressFix <input> <output>");
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
                ValidateCondition(module);

                var compute = gameplay.Methods.Single(method =>
                    method.Name == "ComputeRewardQuestProgress" &&
                    method.IsStatic &&
                    method.Parameters.Count == 2);
                var helper = EnsureProgressHelper(module, gameplay);
                PatchCombatPowerBranch(compute, helper);

                assembly.Write(args[1]);
                Console.WriteLine(
                    "Combat-power reward tasks now use CalculatePowerScore.");
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

    private static void ValidateCondition(ModuleDefinition module)
    {
        var field = RequiredType(
            module,
            "RewardQuestConditionType").Fields.Single(candidate =>
                candidate.Name == "REACH_COMBAT" &&
                candidate.IsLiteral);
        if (Convert.ToInt32(field.Constant) != CombatPowerCondition)
        {
            throw new InvalidOperationException(
                "RewardQuestConditionType.REACH_COMBAT changed.");
        }
    }

    private static void PatchCombatPowerBranch(
        MethodDefinition compute,
        MethodDefinition helper)
    {
        var switchInstruction = compute.Body.Instructions
            .Single(instruction => instruction.OpCode == OpCodes.Switch);
        var targets = switchInstruction.Operand as Instruction[];
        if (targets == null ||
            targets.Length <= CombatPowerCondition)
        {
            throw new InvalidOperationException(
                "Unexpected reward quest condition switch.");
        }

        var call = compute.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand == helper);
        Instruction branch;
        if (call == null)
        {
            var lastReturn = compute.Body.Instructions.Last(instruction =>
                instruction.OpCode == OpCodes.Ret);
            branch = Instruction.Create(OpCodes.Nop);
            var il = compute.Body.GetILProcessor();
            var loadMax = Instruction.Create(OpCodes.Ldarg_1);
            var callHelper = Instruction.Create(OpCodes.Call, helper);
            var returnValue = Instruction.Create(OpCodes.Ret);
            il.InsertAfter(lastReturn, branch);
            il.InsertAfter(branch, loadMax);
            il.InsertAfter(loadMax, callHelper);
            il.InsertAfter(callHelper, returnValue);
        }
        else
        {
            branch = call.Previous != null &&
                     call.Previous.OpCode == OpCodes.Nop
                ? call.Previous
                : call;
        }

        targets[CombatPowerCondition] = branch;
        compute.Body.MaxStackSize = Math.Max(
            compute.Body.MaxStackSize,
            1);
    }

    private static MethodDefinition EnsureProgressHelper(
        ModuleDefinition module,
        TypeDefinition gameplay)
    {
        var existing = gameplay.Methods.FirstOrDefault(candidate =>
            candidate.Name == "GetCombatPowerQuestProgress" &&
            candidate.IsStatic &&
            candidate.Parameters.Count == 1);
        if (existing != null)
        {
            return existing;
        }

        var method = new MethodDefinition(
            "GetCombatPowerQuestProgress",
            MethodAttributes.Private |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Int32);
        method.Parameters.Add(new ParameterDefinition(
            "maxProgress",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        gameplay.Methods.Add(method);

        var playerType = RequiredType(module, "PlayerDataStruct");
        var dataManagerType = RequiredType(module, "DataManager");
        var getInstance = dataManagerType.Methods.Single(candidate =>
            candidate.Name == "get_Instance" &&
            candidate.IsStatic &&
            candidate.Parameters.Count == 0);
        var playerData = dataManagerType.Fields.Single(field =>
            field.Name == "playerDataStruct");
        var calculatePower = playerType.Methods.Single(candidate =>
            candidate.Name == "CalculatePowerScore" &&
            !candidate.IsStatic &&
            candidate.Parameters.Count == 0);
        var mathMin = module.ImportReference(typeof(Math).GetMethod(
            "Min",
            new[] { typeof(int), typeof(int) }));
        var mathMax = module.ImportReference(typeof(Math).GetMethod(
            "Max",
            new[] { typeof(int), typeof(int) }));

        var player = new VariableDefinition(playerType);
        var total = new VariableDefinition(module.TypeSystem.Int32);
        method.Body.Variables.Add(player);
        method.Body.Variables.Add(total);
        method.Body.InitLocals = true;
        method.Body.MaxStackSize = 3;

        var il = method.Body.GetILProcessor();
        var noPlayer = Instruction.Create(OpCodes.Nop);
        Append(
            il,
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, total),
            Instruction.Create(OpCodes.Call, getInstance),
            Instruction.Create(OpCodes.Ldfld, playerData),
            Instruction.Create(OpCodes.Stloc, player),
            Instruction.Create(OpCodes.Ldloc, player),
            Instruction.Create(OpCodes.Brfalse, noPlayer),
            Instruction.Create(OpCodes.Ldloc, player),
            Instruction.Create(OpCodes.Call, calculatePower),
            Instruction.Create(OpCodes.Stloc, total),
            noPlayer,
            Instruction.Create(OpCodes.Ldloc, total),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Call, mathMax),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, mathMin),
            Instruction.Create(OpCodes.Ret));

        return method;
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
