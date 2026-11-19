using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BlackStoreUnlimitedFix
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: BlackStoreUnlimitedFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var store = module.GetType("YzrLocal.LocalBlackStore");
                if (store == null)
                {
                    throw new InvalidOperationException(
                        "YzrLocal.LocalBlackStore was not found.");
                }

                PatchSavedCounts(store);
                PatchExchange(store);
                assembly.Write(args[1]);
            }

            Console.WriteLine(
                "Disabled Black Store exchange-count consumption and " +
                "cleared persisted exchange counters.");
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

    private static void PatchSavedCounts(TypeDefinition store)
    {
        var reset = store.Methods.Single(
            method =>
                method.Name == "ResetForLoad" &&
                method.IsStatic &&
                method.Parameters.Count == 1);
        var restore = store.Methods.Single(
            method =>
                method.Name == "RestoreCounts" &&
                method.IsStatic &&
                method.Parameters.Count == 2);

        if (!restore.HasBody)
        {
            throw new InvalidOperationException(
                "LocalBlackStore.RestoreCounts has no body.");
        }

        var instructions = restore.Body.Instructions;
        if (instructions.Count >= 3 &&
            instructions[0].OpCode == OpCodes.Ldarg_0 &&
            instructions[1].Operand is MethodReference &&
            ((MethodReference)instructions[1].Operand).Resolve() == reset &&
            instructions[2].OpCode == OpCodes.Ret)
        {
            Console.WriteLine(
                "Saved Black Store counters are already cleared on load.");
            return;
        }

        restore.Body.ExceptionHandlers.Clear();
        restore.Body.Variables.Clear();
        restore.Body.InitLocals = false;
        restore.Body.MaxStackSize = 1;

        var il = restore.Body.GetILProcessor();
        while (restore.Body.Instructions.Count > 0)
        {
            il.Remove(restore.Body.Instructions[0]);
        }

        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Call, reset));
        il.Append(Instruction.Create(OpCodes.Ret));
        Console.WriteLine(
            "Old exchanged counts now reset to zero whenever save data loads.");
    }

    private static void PatchExchange(TypeDefinition store)
    {
        var exchange = store.Methods.Single(
            method =>
                method.Name == "OnExchangeReqLocal" &&
                method.IsStatic &&
                method.Parameters.Count == 3);
        if (!exchange.HasBody)
        {
            throw new InvalidOperationException(
                "LocalBlackStore.OnExchangeReqLocal has no body.");
        }

        var instructions = exchange.Body.Instructions;
        var remainCall = instructions.First(
            instruction =>
                instruction.OpCode == OpCodes.Callvirt &&
                instruction.Operand is MethodReference &&
                ((MethodReference)instruction.Operand).Name ==
                    "get_RemainCount" &&
                ((MethodReference)instruction.Operand)
                    .DeclaringType.FullName == "BlackStoreItemInfo");
        var limitBranch = remainCall.Next;
        if (limitBranch == null ||
            (limitBranch.OpCode != OpCodes.Ble_S &&
             limitBranch.OpCode != OpCodes.Br))
        {
            throw new InvalidOperationException(
                "Unexpected Black Store limit check layout.");
        }

        if (limitBranch.OpCode != OpCodes.Br)
        {
            limitBranch.OpCode = OpCodes.Br;
            Console.WriteLine(
                "Skipped the insufficient-remaining-count rejection.");
        }

        var exchangedLoads = instructions
            .Where(
                instruction =>
                    instruction.OpCode == OpCodes.Ldsfld &&
                    instruction.Operand is FieldReference &&
                    ((FieldReference)instruction.Operand).Name ==
                        "s_exchanged")
            .ToList();
        if (exchangedLoads.Count < 2)
        {
            throw new InvalidOperationException(
                "Expected two s_exchanged uses in OnExchangeReqLocal.");
        }

        var incrementStart = exchangedLoads[1];
        var decrementCall = instructions
            .SkipWhile(instruction => instruction != incrementStart)
            .FirstOrDefault(
                instruction =>
                    instruction.OpCode == OpCodes.Callvirt &&
                    instruction.Operand is MethodReference &&
                    ((MethodReference)instruction.Operand).Name ==
                        "set_RemainCount");
        if (decrementCall == null || decrementCall.Next == null)
        {
            throw new InvalidOperationException(
                "Could not find the Black Store counter decrement.");
        }

        var incrementBlock = instructions
            .SkipWhile(instruction => instruction != incrementStart)
            .TakeWhile(instruction => instruction != decrementCall)
            .Concat(new[] { decrementCall })
            .ToList();
        if (incrementBlock.All(instruction => instruction.OpCode == OpCodes.Nop))
        {
            Console.WriteLine(
                "Black Store exchange no longer updates remaining counts.");
            return;
        }

        foreach (var instruction in instructions)
        {
            var target = instruction.Operand as Instruction;
            if (target != null &&
                target != incrementStart &&
                incrementBlock.Contains(target))
            {
                throw new InvalidOperationException(
                    "Unexpected branch into the Black Store counter update.");
            }
        }

        foreach (var instruction in incrementBlock)
        {
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
        }

        Console.WriteLine(
            "Black Store exchange no longer records or decrements usage.");
    }
}
