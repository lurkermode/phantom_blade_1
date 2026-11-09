using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class UnlimitedStoreFix
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
            Console.Error.WriteLine("Usage: UnlimitedStoreFix <input> <output>");
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
                var instructions = body.Instructions;
                var il = body.GetILProcessor();

                var availabilityCheck = instructions.First(i =>
                    IsField(i, "avaliableTime") &&
                    i.Next != null &&
                    i.Next.OpCode == OpCodes.Ldc_I4_0 &&
                    i.Next.Next != null &&
                    i.Next.Next.OpCode == OpCodes.Bgt_S);
                var success = (Instruction)availabilityCheck.Next.Next.Operand;
                var storeLoad = availabilityCheck.Previous;
                var limitValue = availabilityCheck.Next;
                var branch = availabilityCheck.Next.Next;

                il.Replace(storeLoad, Instruction.Create(OpCodes.Br, success));
                instructions.Remove(availabilityCheck);
                instructions.Remove(limitValue);
                instructions.Remove(branch);

                var decrement = instructions.First(i =>
                    IsField(i, "avaliableTime") &&
                    i.Next != null &&
                    i.Next.OpCode == OpCodes.Ldc_I4_1);
                il.Replace(decrement.Next, Instruction.Create(OpCodes.Ldc_I4_0));

                var crystalStoreCard = module.GetType("CrystalStoreCard");
                var onButtonBuy = crystalStoreCard.Methods.Single(m => m.Name == "OnButtonBuy");
                var uiInstructions = onButtonBuy.Body.Instructions;
                var uiLimitCheck = uiInstructions.Take(5).ToList();
                if (!IsField(uiLimitCheck[2], "avaliableTime") ||
                    uiLimitCheck[4].OpCode != OpCodes.Ble)
                {
                    throw new InvalidOperationException("Unexpected crystal store UI limit check.");
                }
                foreach (var instruction in uiLimitCheck)
                {
                    uiInstructions.Remove(instruction);
                }

                assembly.Write(args[1]);
            }

            Console.WriteLine("Removed store purchase limits.");
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
