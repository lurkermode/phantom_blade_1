using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class DailyBeidouFix
{
    private const int FirstBeidouScene = 51601;
    private const int LastBeidouScene = 51604;
    private const int FinishRuneFarmLevel = 21;

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: DailyBeidouFix <input> <output>");
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

                var classify = localGameplay.Methods.SingleOrDefault(
                    method => method.Name == "ClassifyLevelCategory");
                if (classify == null || classify.Parameters.Count != 1)
                {
                    throw new InvalidOperationException(
                        "LocalGameplay.ClassifyLevelCategory(int32) was not found.");
                }

                var genericReturn = classify.ReturnType as GenericInstanceType;
                if (genericReturn == null ||
                    genericReturn.GenericArguments.Count != 1 ||
                    genericReturn.GenericArguments[0].Name != "RewardQuestConditionType")
                {
                    throw new InvalidOperationException(
                        "Unexpected ClassifyLevelCategory return type: " +
                        classify.ReturnType.FullName);
                }

                var body = classify.Body;
                var instructions = body.Instructions;
                if (instructions.Count < 2 ||
                    instructions[0].OpCode != OpCodes.Ldarg_0 ||
                    !(instructions[1].Operand is int) ||
                    (int)instructions[1].Operand != FirstBeidouScene)
                {
                    var originalStart = instructions[0];
                    var nullableConstructor = instructions
                        .Where(instruction =>
                            instruction.OpCode == OpCodes.Newobj &&
                            instruction.Operand is MethodReference)
                        .Select(instruction => (MethodReference)instruction.Operand)
                        .FirstOrDefault(reference =>
                            reference.DeclaringType.FullName.StartsWith(
                                "System.Nullable`1<RewardQuestConditionType>",
                                StringComparison.Ordinal));
                    if (nullableConstructor == null)
                    {
                        throw new InvalidOperationException(
                            "RewardQuestConditionType nullable constructor was not found.");
                    }

                    var returnValue = instructions.Last().Previous;
                    if (returnValue.OpCode != OpCodes.Ldloc_3 ||
                        instructions.Last().OpCode != OpCodes.Ret)
                    {
                        throw new InvalidOperationException(
                            "Unexpected ClassifyLevelCategory return sequence.");
                    }

                    var il = body.GetILProcessor();
                    var skip = originalStart;
                    var firstPatchInstruction = Instruction.Create(OpCodes.Ldarg_0);
                    var patch = new[]
                    {
                        firstPatchInstruction,
                        Instruction.Create(OpCodes.Ldc_I4, FirstBeidouScene),
                        Instruction.Create(OpCodes.Blt_Un, skip),
                        Instruction.Create(OpCodes.Ldarg_0),
                        Instruction.Create(OpCodes.Ldc_I4, LastBeidouScene),
                        Instruction.Create(OpCodes.Bgt_Un, skip),
                        Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)FinishRuneFarmLevel),
                        Instruction.Create(OpCodes.Newobj, nullableConstructor),
                        Instruction.Create(OpCodes.Stloc_3),
                        Instruction.Create(OpCodes.Leave, returnValue)
                    };

                    foreach (var instruction in patch)
                    {
                        il.InsertBefore(originalStart, instruction);
                    }

                    foreach (var handler in body.ExceptionHandlers)
                    {
                        if (handler.TryStart == originalStart)
                        {
                            handler.TryStart = firstPatchInstruction;
                        }
                    }

                    body.MaxStackSize = Math.Max(body.MaxStackSize, 2);
                    Console.WriteLine(
                        "Mapped daily Beidou scenes {0}-{1} to {2}.",
                        FirstBeidouScene,
                        LastBeidouScene,
                        FinishRuneFarmLevel);
                }
                else
                {
                    Console.WriteLine("Daily Beidou scene mapping is already present.");
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
