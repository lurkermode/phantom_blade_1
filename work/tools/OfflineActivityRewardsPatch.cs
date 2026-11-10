using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class OfflineActivityRewardsPatch
{
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine(
                "Usage: OfflineActivityRewardsPatch <input> <helper> <output>");
            return 2;
        }

        try
        {
            using (var helperAssembly =
                   AssemblyDefinition.ReadAssembly(args[1]))
            using (var assembly =
                   AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var helper = helperAssembly.MainModule.GetType(
                    "YzrLocal.ActivityRewards");
                if (helper == null)
                {
                    throw new InvalidOperationException(
                        "The activity reward helper was not found.");
                }

                var addStamina = Import(
                    module,
                    helper,
                    "AddStaminaUsed");
                var addConsumption = Import(
                    module,
                    helper,
                    "AddYuanBaoConsumption");
                var addRecharge = Import(
                    module,
                    helper,
                    "AddRecharge");
                var refreshAll = Import(
                    module,
                    helper,
                    "RefreshAll");
                var claimStamina = Import(
                    module,
                    helper,
                    "ClaimStaminaAndRefresh");
                var claimPay = Import(
                    module,
                    helper,
                    "ClaimPayAndRefresh");
                var claimConsumption = Import(
                    module,
                    helper,
                    "ClaimConsumptionAndRefresh");

                PatchStaminaProgress(module, addStamina);
                PatchSweepStaminaProgress(module, addStamina);
                PatchConsumptionProgress(module, addConsumption);
                PatchRechargeProgress(module, addRecharge);
                PatchActivityRefresh(module, refreshAll);
                PatchClaimButtons(
                    module,
                    claimStamina,
                    claimPay,
                    claimConsumption);

                assembly.Write(args[2]);
            }

            Console.WriteLine("Patched offline progress and claims.");
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

    private static MethodReference Import(
        ModuleDefinition module,
        TypeDefinition helper,
        string name)
    {
        var method = helper.Methods.Single(candidate =>
            candidate.Name == name);
        return module.ImportReference(method);
    }

    private static void PatchStaminaProgress(
        ModuleDefinition module,
        MethodReference addStamina)
    {
        var method = module.GetType("YzrLocal.LocalGameplay").Methods.Single(
            candidate => candidate.Name == "ConsumeStamina");
        if (Calls(method, addStamina))
        {
            return;
        }

        var messageCtor = method.Body.Instructions.First(instruction =>
            instruction.OpCode == OpCodes.Newobj &&
            instruction.Operand is MethodReference &&
            ((MethodReference)instruction.Operand).DeclaringType.FullName ==
            "MessageEventArgs");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(messageCtor, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(messageCtor, Instruction.Create(OpCodes.Call, addStamina));
    }

    private static void PatchConsumptionProgress(
        ModuleDefinition module,
        MethodReference addConsumption)
    {
        var method = module.GetType("YzrLocal.LocalProgression").Methods.Single(
            candidate => candidate.Name == "UseCurrency");
        if (Calls(method, addConsumption))
        {
            return;
        }

        var setCurrency = method.Body.Instructions.First(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference &&
            ((MethodReference)instruction.Operand).Name == "SetCurrency" &&
            ((MethodReference)instruction.Operand).DeclaringType.FullName ==
            "YzrLocal.LocalProfileFixes");
        var il = method.Body.GetILProcessor();
        il.InsertAfter(setCurrency, Instruction.Create(OpCodes.Ldarg_1));
        il.InsertAfter(
            setCurrency.Next,
            Instruction.Create(OpCodes.Ldarg_2));
        il.InsertAfter(
            setCurrency.Next.Next,
            Instruction.Create(OpCodes.Call, addConsumption));
    }

    private static void PatchSweepStaminaProgress(
        ModuleDefinition module,
        MethodReference addStamina)
    {
        var method = module.GetType("YzrLocal.LocalFeatures").Methods.Single(
            candidate => candidate.Name == "OnSweapDungeonReqLocal");
        if (Calls(method, addStamina))
        {
            return;
        }

        var staminaField = module.GetType("PlayerDataStruct").Fields.Single(
            field => field.Name == "curStamina");
        var staminaStore = method.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Stfld &&
            instruction.Operand is FieldReference &&
            ((FieldReference)instruction.Operand).Resolve() == staminaField);
        var multiply = staminaStore.Previous.Previous;
        var multiplierLoad = multiply.Previous.Previous;
        var countLoad = multiply.Previous;
        if (multiply.OpCode != OpCodes.Mul)
        {
            throw new InvalidOperationException(
                "Unexpected sweep stamina deduction sequence.");
        }

        var multiplier = multiplierLoad.Operand as VariableDefinition;
        var count = countLoad.OpCode == OpCodes.Ldloc_1
            ? method.Body.Variables[1]
            : countLoad.Operand as VariableDefinition;
        if (multiplier == null || count == null)
        {
            throw new InvalidOperationException(
                "Unable to identify sweep stamina values.");
        }
        var il = method.Body.GetILProcessor();
        var loadMultiplier = Instruction.Create(OpCodes.Ldloc, multiplier);
        var loadCount = Instruction.Create(OpCodes.Ldloc, count);
        var multiplyAgain = Instruction.Create(OpCodes.Mul);
        var call = Instruction.Create(OpCodes.Call, addStamina);
        il.InsertAfter(staminaStore, loadMultiplier);
        il.InsertAfter(loadMultiplier, loadCount);
        il.InsertAfter(loadCount, multiplyAgain);
        il.InsertAfter(multiplyAgain, call);
    }

    private static void PatchRechargeProgress(
        ModuleDefinition module,
        MethodReference addRecharge)
    {
        var method = module.GetType("YzrLocal.LocalRecharge").Methods.Single(
            candidate => candidate.Name == "Deliver");
        if (Calls(method, addRecharge))
        {
            return;
        }

        var addCurrency = method.Body.Instructions.First(instruction =>
            instruction.OpCode == OpCodes.Callvirt &&
            instruction.Operand is MethodReference &&
            ((MethodReference)instruction.Operand).Name == "AddCurrency" &&
            ((MethodReference)instruction.Operand).DeclaringType.FullName ==
            "PlayerDataStruct");
        var pop = addCurrency.Next;
        if (pop == null || pop.OpCode != OpCodes.Pop)
        {
            throw new InvalidOperationException(
                "Unexpected local recharge delivery sequence.");
        }

        var purchaseType = module.GetType("YuanBaoPurchase");
        var rmb = purchaseType.Fields.Single(field =>
            field.Name == "rmb");
        var il = method.Body.GetILProcessor();
        var loadPurchase = Instruction.Create(OpCodes.Ldarg_2);
        var loadRmb = Instruction.Create(OpCodes.Ldfld, rmb);
        var call = Instruction.Create(OpCodes.Call, addRecharge);
        il.InsertAfter(pop, loadPurchase);
        il.InsertAfter(loadPurchase, loadRmb);
        il.InsertAfter(loadRmb, call);
    }

    private static void PatchActivityRefresh(
        ModuleDefinition module,
        MethodReference refreshAll)
    {
        var method = module.GetType("ActivityController").Methods.Single(
            candidate => candidate.Name == "PrepareOfflinePages");
        if (Calls(method, refreshAll))
        {
            return;
        }

        var ret = method.Body.Instructions.Last(
            instruction => instruction.OpCode == OpCodes.Ret);
        method.Body.GetILProcessor().InsertBefore(
            ret,
            Instruction.Create(OpCodes.Call, refreshAll));
    }

    private static void PatchClaimButtons(
        ModuleDefinition module,
        MethodReference claimStamina,
        MethodReference claimPay,
        MethodReference claimConsumption)
    {
        var staminaPanel = module.GetType("ActivityStaminaUsedPanel");
        ReplaceTwoArgumentClaim(
            staminaPanel.Methods.Single(method =>
                method.Name == "OnGetReward"),
            claimStamina);

        var payPanel = module.GetType("ActivityAccumulatedPayPanel");
        ReplaceTwoArgumentClaim(
            payPanel.Methods.Single(method =>
                method.Name == "OnGetReward"),
            claimPay);

        var consumptionPanel =
            module.GetType("ActivityConsumptionPeriodPanel");
        var consumptionClaim =
            consumptionPanel.Methods.Single(method =>
                method.Name == "OnGetReward");
        var cardType = module.GetType("ActivityLoginCard");
        var indexField = cardType.Fields.Single(field =>
            field.Name == "index");
        ClearBody(consumptionClaim, 2);
        var consumptionIl = consumptionClaim.Body.GetILProcessor();
        Append(consumptionIl,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldfld, indexField),
            Instruction.Create(OpCodes.Call, claimConsumption),
            Instruction.Create(OpCodes.Ret));
    }

    private static void ReplaceTwoArgumentClaim(
        MethodDefinition method,
        MethodReference claim)
    {
        ClearBody(method, 3);
        var il = method.Body.GetILProcessor();
        Append(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldarg_2),
            Instruction.Create(OpCodes.Call, claim),
            Instruction.Create(OpCodes.Ret));
    }

    private static void ClearBody(MethodDefinition method, int maxStack)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;
        method.Body.MaxStackSize = maxStack;
    }

    private static bool Calls(
        MethodDefinition method,
        MethodReference target)
    {
        return method.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference &&
            ((MethodReference)instruction.Operand).Name ==
            target.Name &&
            ((MethodReference)instruction.Operand).DeclaringType.FullName ==
            target.DeclaringType.FullName);
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
