using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class UnlimitedResignFix
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: UnlimitedResignFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var localGameplay = module.GetType("YzrLocal.LocalGameplay");
                if (localGameplay == null)
                {
                    throw new InvalidOperationException("YzrLocal.LocalGameplay was not found.");
                }

                var getRemain = localGameplay.Methods.SingleOrDefault(
                    method => method.Name == "GetMonthlyResignInRemain" &&
                              method.Parameters.Count == 0);
                if (getRemain == null)
                {
                    throw new InvalidOperationException(
                        "LocalGameplay.GetMonthlyResignInRemain was not found.");
                }

                var instructions = getRemain.Body.Instructions;
                if (instructions.Count == 0)
                {
                    throw new InvalidOperationException(
                        "GetMonthlyResignInRemain has an empty body.");
                }

                var first = instructions[0];
                var alreadyPatched =
                    first.OpCode == OpCodes.Ldc_I4 &&
                    (int)first.Operand == int.MaxValue;
                if (!alreadyPatched)
                {
                    if (first.OpCode != OpCodes.Ldc_I4_0)
                    {
                        throw new InvalidOperationException(
                            "Unexpected GetMonthlyResignInRemain layout.");
                    }

                    first.OpCode = OpCodes.Ldc_I4;
                    first.Operand = int.MaxValue;
                    foreach (var instruction in instructions.Where(instruction =>
                        instruction.OpCode == OpCodes.Ldc_I4_5))
                    {
                        instruction.OpCode = OpCodes.Ldc_I4;
                        instruction.Operand = int.MaxValue;
                    }

                    Console.WriteLine("Removed the monthly resign-in limit.");
                }
                else
                {
                    Console.WriteLine("Unlimited resign-in fix is already present.");
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
