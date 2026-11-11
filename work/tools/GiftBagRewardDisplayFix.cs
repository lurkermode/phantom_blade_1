using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class GiftBagRewardDisplayFix
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: GiftBagRewardDisplayFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var window = assembly.MainModule.GetType("SelectItemWindow");
                var display = window == null
                    ? null
                    : window.Methods.SingleOrDefault(method =>
                        method.Name == "DisplayBagItem" &&
                        method.Parameters.Count == 1);
                if (display == null || !display.HasBody)
                {
                    throw new InvalidOperationException(
                        "SelectItemWindow.DisplayBagItem was not found.");
                }

                var body = display.Body;
                var instructions = body.Instructions;
                if (body.Variables.Count < 6)
                {
                    throw new InvalidOperationException(
                        "Unexpected DisplayBagItem local layout.");
                }

                var countKey = instructions.Single(instruction =>
                    instruction.OpCode == OpCodes.Ldstr &&
                    string.Equals(
                        instruction.Operand as string,
                        "count",
                        StringComparison.Ordinal));
                var writeMessage = countKey.Previous;
                var itemCountLocal = body.Variables[1];
                var equipmentCountLocal = body.Variables[5];
                var messageCountLocal = body.Variables[3];
                var alreadyPatched =
                    writeMessage.Previous != null &&
                    writeMessage.Previous.OpCode == OpCodes.Stloc_3 &&
                    writeMessage.Previous.Previous != null &&
                    writeMessage.Previous.Previous.OpCode == OpCodes.Sub &&
                    writeMessage.Previous.Previous.Previous != null &&
                    writeMessage.Previous.Previous.Previous.OpCode ==
                        OpCodes.Ldloc_S &&
                    writeMessage.Previous.Previous.Previous.Operand ==
                        equipmentCountLocal &&
                    writeMessage.Previous.Previous.Previous.Previous != null &&
                    writeMessage.Previous.Previous.Previous.Previous.OpCode ==
                        OpCodes.Ldloc_1;

                if (alreadyPatched)
                {
                    Console.WriteLine(
                        "Gift bag reward display count is already fixed.");
                }
                else
                {
                    var il = body.GetILProcessor();
                    il.InsertBefore(
                        writeMessage,
                        Instruction.Create(OpCodes.Ldloc_1));
                    il.InsertBefore(
                        writeMessage,
                        Instruction.Create(
                            OpCodes.Ldloc_S,
                            equipmentCountLocal));
                    il.InsertBefore(
                        writeMessage,
                        Instruction.Create(OpCodes.Sub));
                    il.InsertBefore(
                        writeMessage,
                        Instruction.Create(OpCodes.Stloc_3));

                    body.MaxStackSize = Math.Max(body.MaxStackSize, 2);
                    Console.WriteLine(
                        "Fixed gift bag reward count: runes + gems + " +
                        "materials (excluding equipment).");
                }

                if (messageCountLocal.VariableType.MetadataType !=
                    MetadataType.Int32 ||
                    itemCountLocal.VariableType.MetadataType !=
                        MetadataType.Int32)
                {
                    throw new InvalidOperationException(
                        "Unexpected reward count local types.");
                }

                assembly.Write(args[1]);
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
}
