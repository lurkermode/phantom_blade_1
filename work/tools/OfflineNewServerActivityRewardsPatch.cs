using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class OfflineNewServerActivityRewardsPatch
{
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine(
                "Usage: OfflineNewServerActivityRewardsPatch <input> <helper> <output>");
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
                    "YzrLocal.NewServerActivityRewards");
                if (helper == null)
                {
                    throw new InvalidOperationException(
                        "The new-server activity helper was not found.");
                }

                PatchServerFirstCharge(module, helper);
                PatchVipPrize(module, helper);
                PatchWindmill(module, helper);
                PatchActivityDates(module, helper);
                PatchServerFirstChargeLabel(module, helper);

                assembly.Write(args[2]);
            }

            Console.WriteLine(
                "Connected server-first charge, windmill and VIP prizes to daily local logic.");
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

    private static void PatchServerFirstCharge(
        ModuleDefinition module,
        TypeDefinition helper)
    {
        var content = module.GetType("ServerFirstChargeActivityContent");
        var getAmount = Import(
            module,
            helper,
            "GetDailyRechargeAmount");
        var getStatus = Import(
            module,
            helper,
            "GetServerFirstChargeStatus");
        var claim = Import(
            module,
            helper,
            "ClaimServerFirstCharge");

        var chargeAmount = content.Methods.Single(method =>
            method.Name == "get_chargeMemCount");
        ClearBody(chargeAmount, 1);
        Append(
            chargeAmount.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Call, getAmount),
            Instruction.Create(OpCodes.Conv_U4),
            Instruction.Create(OpCodes.Ret));

        var normalStatus = content.Methods.Single(method =>
            method.Name == "GetRewardStatus");
        ClearBody(normalStatus, 2);
        Append(
            normalStatus.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Call, getStatus),
            Instruction.Create(OpCodes.Ret));

        var vipStatus = content.Methods.Single(method =>
            method.Name == "GetVIPRewardStatus");
        ClearBody(vipStatus, 2);
        Append(
            vipStatus.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Call, getStatus),
            Instruction.Create(OpCodes.Ret));

        var claimMethod = content.Methods.Single(method =>
            method.Name == "GetRewardRequestByPID");
        ClearBody(claimMethod, 3);
        Append(
            claimMethod.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldarg_2),
            Instruction.Create(OpCodes.Call, claim),
            Instruction.Create(OpCodes.Ret));
    }

    private static void PatchVipPrize(
        ModuleDefinition module,
        TypeDefinition helper)
    {
        var content = module.GetType("VIPPrizeActivityContent");
        var getStatus = Import(
            module,
            helper,
            "GetVipPrizeStatus");
        var buy = Import(
            module,
            helper,
            "BuyVipPrize");

        var statusMethod = content.Methods.Single(method =>
            method.Name == "GetRewardStatus");
        ClearBody(statusMethod, 1);
        Append(
            statusMethod.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Call, getStatus),
            Instruction.Create(OpCodes.Ret));

        var purchaseMethod = content.Methods.Single(method =>
            method.Name == "GetRewardRequestByPID");
        ClearBody(purchaseMethod, 2);
        Append(
            purchaseMethod.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Call, buy),
            Instruction.Create(OpCodes.Ret));
    }

    private static void PatchWindmill(
        ModuleDefinition module,
        TypeDefinition helper)
    {
        var content = module.GetType("WindmillActivityContent");
        var getRemain = Import(
            module,
            helper,
            "GetWindmillRemainCount");
        var prepare = Import(module, helper, "PrepareWindmill");
        var request = Import(module, helper, "RequestWindmill");
        var commit = Import(module, helper, "CommitWindmillStep");

        var remainMethod = content.Methods.Single(method =>
            method.Name == "get_remainCount");
        ClearBody(remainMethod, 1);
        Append(
            remainMethod.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, getRemain),
            Instruction.Create(OpCodes.Ret));

        var requestMethod = content.Methods.Single(method =>
            method.Name == "GetRewardRequest");
        ClearBody(requestMethod, 1);
        Append(
            requestMethod.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, request),
            Instruction.Create(OpCodes.Ret));

        var advanceMethod = content.Methods.Single(method =>
            method.Name == "SetNextRewardData");
        var advanceRet = advanceMethod.Body.Instructions.Last(
            instruction => instruction.OpCode == OpCodes.Ret);
        var advanceIl = advanceMethod.Body.GetILProcessor();
        advanceIl.InsertBefore(
            advanceRet,
            Instruction.Create(OpCodes.Ldarg_0));
        advanceIl.InsertBefore(
            advanceRet,
            Instruction.Create(OpCodes.Call, commit));

        var panel = module.GetType("UIWindmillActivityContentPanel");
        var showPanel = panel.Methods.Single(method =>
            method.Name == "OnShowPanel");
        var update = showPanel.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference &&
            ((MethodReference)instruction.Operand).Name == "UpdateUIImp");
        var showIl = showPanel.Body.GetILProcessor();
        showIl.InsertBefore(
            update,
            Instruction.Create(OpCodes.Ldarg_0));
        showIl.InsertBefore(
            update,
            Instruction.Create(
                OpCodes.Call,
                panel.Methods.Single(method =>
                    method.Name == "get_theActivityContent")));
        showIl.InsertBefore(
            update,
            Instruction.Create(OpCodes.Call, prepare));
    }

    private static void PatchActivityDates(
        ModuleDefinition module,
        TypeDefinition helper)
    {
        var info = module.GetType("NewServerActivityInfo");
        ReplaceWithHelperCall(
            info.Methods.Single(method =>
                method.Name == "get_beginTime"),
            Import(module, helper, "GetActivityBeginTime"));
        ReplaceWithHelperCall(
            info.Methods.Single(method =>
                method.Name == "get_endTime"),
            Import(module, helper, "GetActivityEndTime"));
    }

    private static void PatchServerFirstChargeLabel(
        ModuleDefinition module,
        TypeDefinition helper)
    {
        var normalize = Import(
            module,
            helper,
            "NormalizeServerFirstChargePanel");
        var panel = module.GetType("UIServerFirstChargeContentPanel");
        var tagStatus = panel.Fields.Single(field =>
            field.Name == "tagStatus");
        var showPanel = panel.Methods.Single(method =>
            method.Name == "OnShowPanel");
        var statusLoad = showPanel.Body.Instructions.First(instruction =>
            instruction.OpCode == OpCodes.Ldfld &&
            instruction.Operand == tagStatus);
        var insertionPoint = statusLoad.Previous != null &&
                             statusLoad.Previous.OpCode == OpCodes.Ldarg_0
            ? statusLoad.Previous
            : statusLoad;
        var il = showPanel.Body.GetILProcessor();
        il.InsertBefore(
            insertionPoint,
            Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(
            insertionPoint,
            Instruction.Create(OpCodes.Call, normalize));
    }

    private static void ReplaceWithHelperCall(
        MethodDefinition method,
        MethodReference helper)
    {
        ClearBody(method, 1);
        Append(
            method.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Call, helper),
            Instruction.Create(OpCodes.Ret));
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

    private static void ClearBody(MethodDefinition method, int maxStack)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;
        method.Body.MaxStackSize = maxStack;
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
