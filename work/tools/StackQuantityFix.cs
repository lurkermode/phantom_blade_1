using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class StackQuantityFix
{
    private static bool IsField(Instruction instruction, string name)
    {
        return instruction.OpCode == OpCodes.Ldfld &&
               instruction.Operand is FieldReference &&
               ((FieldReference)instruction.Operand).Name == name;
    }

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: StackQuantityFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var itemManager = module.GetType("ItemManager");
                var onBuyItem = itemManager.Methods.Single(m => m.Name == "OnBuyItem");
                var body = onBuyItem.Body;
                body.InitLocals = true;
                var instructions = body.Instructions;
                var il = body.GetILProcessor();

                var purchaseCount = new VariableDefinition(module.TypeSystem.Int32);
                body.Variables.Add(purchaseCount);

                var eventMessage = body.Variables[0];
                var storeValue = body.Variables.Single(v => v.VariableType.FullName == "StoreItem");
                var finalReturn = instructions.Last(i => i.OpCode == OpCodes.Ret);

                var messageType = module.GetType("MessageEventArgs");
                var containMessage = messageType.Methods.Single(m => m.Name == "ContainMessage");
                var getMessage = messageType.Methods.Single(m => m.Name == "GetMessage");
                var toInt32 = instructions.Where(i =>
                    i.OpCode == OpCodes.Call &&
                    i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name == "ToInt32")
                    .Select(i => (MethodReference)i.Operand)
                    .First();

                var shopIdStored = instructions.First(i => i.OpCode == OpCodes.Stloc_1);
                var countReady = Instruction.Create(OpCodes.Nop);
                var defaultCount = Instruction.Create(OpCodes.Nop);
                var countInstructions = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Ldloc, eventMessage),
                    Instruction.Create(OpCodes.Ldstr, "count"),
                    Instruction.Create(OpCodes.Callvirt, containMessage),
                    Instruction.Create(OpCodes.Brfalse, defaultCount),
                    Instruction.Create(OpCodes.Ldloc, eventMessage),
                    Instruction.Create(OpCodes.Ldstr, "count"),
                    Instruction.Create(OpCodes.Callvirt, getMessage),
                    Instruction.Create(OpCodes.Call, toInt32),
                    Instruction.Create(OpCodes.Stloc, purchaseCount),
                    Instruction.Create(OpCodes.Br, countReady),
                    defaultCount,
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Stloc, purchaseCount),
                    countReady,
                    Instruction.Create(OpCodes.Ldloc, purchaseCount),
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Blt, finalReturn)
                };
                InsertAfterList(il, shopIdStored, countInstructions);

                var useCurrency = instructions.Single(i =>
                    i.OpCode == OpCodes.Callvirt &&
                    i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name == "UseCurrency");
                var coinTypeLoad = useCurrency.Previous;
                if (!IsField(coinTypeLoad, "coinType"))
                {
                    throw new InvalidOperationException("Unable to locate store price calculation.");
                }
                var storeForCoinType = coinTypeLoad.Previous;
                il.InsertBefore(storeForCoinType, Instruction.Create(OpCodes.Ldloc, purchaseCount));
                il.InsertBefore(storeForCoinType, Instruction.Create(OpCodes.Mul));

                var getStackableItem = instructions.Single(i =>
                    i.OpCode == OpCodes.Callvirt &&
                    i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name == "GetStackableItem" &&
                    ((MethodReference)i.Operand).Parameters.Count == 2);
                if (getStackableItem.Previous.OpCode != OpCodes.Ldc_I4_1)
                {
                    throw new InvalidOperationException("Unexpected stackable delivery amount.");
                }
                il.Replace(getStackableItem.Previous, Instruction.Create(OpCodes.Ldloc, purchaseCount));

                var stackCountLabel = instructions.Single(i =>
                    i.OpCode == OpCodes.Ldstr && (string)i.Operand == "StackCount0");
                var fixedCount = stackCountLabel.Next;
                if (fixedCount.OpCode != OpCodes.Ldstr || (string)fixedCount.Operand != "1")
                {
                    throw new InvalidOperationException("Unexpected stackable popup count.");
                }
                var convertIntToString = module.ImportReference(
                    typeof(Convert).GetMethod("ToString", new[] { typeof(int) }));
                var popupCountLoad = Instruction.Create(OpCodes.Ldloc, purchaseCount);
                il.Replace(fixedCount, popupCountLoad);
                il.InsertAfter(popupCountLoad, Instruction.Create(OpCodes.Call, convertIntToString));

                body.MaxStackSize = Math.Max(body.MaxStackSize, 8);
                assembly.Write(args[1]);
            }

            Console.WriteLine("Patched stackable store quantity.");
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

    private static void InsertAfterList(
        ILProcessor il,
        Instruction anchor,
        List<Instruction> inserted)
    {
        foreach (var instruction in inserted)
        {
            il.InsertAfter(anchor, instruction);
            anchor = instruction;
        }
    }
}
