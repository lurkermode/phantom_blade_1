using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class StoreBundleQuantityFix
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: StoreBundleQuantityFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var itemManager = module.GetType("ItemManager");
                if (itemManager == null)
                {
                    throw new InvalidOperationException("ItemManager was not found.");
                }

                var onBuyItem = itemManager.Methods.SingleOrDefault(
                    method => method.Name == "OnBuyItem" && method.Parameters.Count == 3);
                if (onBuyItem == null)
                {
                    throw new InvalidOperationException("ItemManager.OnBuyItem was not found.");
                }

                var body = onBuyItem.Body;
                var instructions = body.Instructions;
                var il = body.GetILProcessor();

                if (instructions.Any(instruction =>
                    instruction.OpCode == OpCodes.Callvirt &&
                    instruction.Operand is MethodReference &&
                    ((MethodReference)instruction.Operand).Name ==
                    "GetStackableItemAmountByRule"))
                {
                    Console.WriteLine("Store bundle quantity fix is already present.");
                    assembly.Write(args[1]);
                    return 0;
                }

                var amountByRule = itemManager.Methods.SingleOrDefault(
                    method => method.Name == "GetStackableItemAmountByRule" &&
                              method.Parameters.Count == 1);
                if (amountByRule == null)
                {
                    throw new InvalidOperationException(
                        "ItemManager.GetStackableItemAmountByRule was not found.");
                }

                var getStackableItem = instructions.SingleOrDefault(instruction =>
                    instruction.OpCode == OpCodes.Callvirt &&
                    instruction.Operand is MethodReference &&
                    ((MethodReference)instruction.Operand).Name == "GetStackableItem" &&
                    ((MethodReference)instruction.Operand).Parameters.Count == 2);
                if (getStackableItem == null ||
                    getStackableItem.Previous == null ||
                    getStackableItem.Previous.OpCode != OpCodes.Ldloc)
                {
                    throw new InvalidOperationException(
                        "Unexpected stackable item delivery code.");
                }

                var purchaseCountLoad = getStackableItem.Previous;
                var purchaseCount = (VariableDefinition)purchaseCountLoad.Operand;
                var deliveryCount = new VariableDefinition(module.TypeSystem.Int32);
                body.Variables.Add(deliveryCount);

                var countCheck = instructions.FirstOrDefault(instruction =>
                    instruction.OpCode == OpCodes.Ldloc &&
                    instruction.Operand == purchaseCount &&
                    instruction.Next != null &&
                    instruction.Next.OpCode == OpCodes.Ldc_I4_1 &&
                    instruction.Next.Next != null &&
                    (instruction.Next.Next.OpCode == OpCodes.Blt ||
                     instruction.Next.Next.OpCode == OpCodes.Blt_S));
                if (countCheck == null)
                {
                    throw new InvalidOperationException(
                        "Unable to locate the purchase count validation.");
                }

                il.InsertBefore(countCheck, Instruction.Create(OpCodes.Ldloc, purchaseCount));
                il.InsertBefore(countCheck, Instruction.Create(OpCodes.Stloc, deliveryCount));

                var storeItem = body.Variables.Single(variable =>
                    variable.VariableType.FullName == "StoreItem");
                var storeRule = module.GetType("StoreItem").Fields.Single(field =>
                    field.Name == "rule");
                var getItemManager = instructions.First(instruction =>
                    instruction.OpCode == OpCodes.Call &&
                    instruction.Operand is MethodReference &&
                    ((MethodReference)instruction.Operand).Name == "get_Instance" &&
                    ((MethodReference)instruction.Operand).DeclaringType.FullName ==
                    "ItemManager");
                var mathMax = module.ImportReference(
                    typeof(Math).GetMethod("Max", new[] { typeof(int), typeof(int) }));

                var computation = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Call, getItemManager.Operand as MethodReference),
                    Instruction.Create(OpCodes.Ldloc, storeItem),
                    Instruction.Create(OpCodes.Ldfld, storeRule),
                    Instruction.Create(OpCodes.Callvirt, amountByRule),
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Call, mathMax),
                    Instruction.Create(OpCodes.Ldloc, purchaseCount),
                    Instruction.Create(OpCodes.Mul),
                    Instruction.Create(OpCodes.Stloc, deliveryCount),
                    Instruction.Create(OpCodes.Ldloc, deliveryCount)
                };
                foreach (var instruction in computation)
                {
                    il.InsertBefore(purchaseCountLoad, instruction);
                }
                instructions.Remove(purchaseCountLoad);

                var stackCountLabel = instructions.Single(instruction =>
                    instruction.OpCode == OpCodes.Ldstr &&
                    (string)instruction.Operand == "StackCount0");
                var popupCountLoad = stackCountLabel.Next;
                if (popupCountLoad == null ||
                    popupCountLoad.OpCode != OpCodes.Ldloc ||
                    popupCountLoad.Operand != purchaseCount)
                {
                    throw new InvalidOperationException(
                        "Unable to locate the stackable item popup count.");
                }
                il.Replace(popupCountLoad, Instruction.Create(OpCodes.Ldloc, deliveryCount));

                body.MaxStackSize = Math.Max(body.MaxStackSize, 8);
                assembly.Write(args[1]);
            }

            Console.WriteLine("Patched store bundle delivery quantity.");
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
}
