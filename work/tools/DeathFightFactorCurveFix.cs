using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class DeathFightFactorCurveFix
{
    private static readonly int[] Factors =
    {
        100, 150, 200, 250, 300, 400, 600, 600, 800, 1000,
    };

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: DeathFightFactorCurveFix <input> <output>");
            return 2;
        }

        using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
        {
            var type = assembly.MainModule.GetType(
                "YzrLocal.LocalDeathFight");
            if (type == null)
            {
                Console.Error.WriteLine(
                    "YzrLocal.LocalDeathFight not found.");
                return 1;
            }

            var method = type.Methods.SingleOrDefault(candidate =>
                candidate.Name == "LadderX100" &&
                candidate.IsStatic &&
                candidate.ReturnType.MetadataType == MetadataType.Int32 &&
                candidate.Parameters.Count == 1 &&
                candidate.Parameters[0].ParameterType.MetadataType ==
                    MetadataType.Int32);
            if (method == null || !method.HasBody)
            {
                Console.Error.WriteLine(
                    "LocalDeathFight.LadderX100(int) not found.");
                return 1;
            }

            PatchLadder(method);
            assembly.Write(args[1]);
        }

        Console.WriteLine(
            "Patched DeathFight factors: " +
            string.Join(", ", Factors.Select(value =>
                (value / 100f).ToString("0.##") + "x")));
        return 0;
    }

    private static void PatchLadder(MethodDefinition method)
    {
        var body = method.Body;
        body.Instructions.Clear();
        body.ExceptionHandlers.Clear();
        body.Variables.Clear();
        body.MaxStackSize = 2;

        var il = body.GetILProcessor();
        var oneHundred = il.Create(OpCodes.Ldc_I4, Factors[0]);
        var linear = il.Create(OpCodes.Ldc_I4, Factors[0]);
        var five = il.Create(OpCodes.Ldc_I4, Factors[5]);
        var sixOrSeven = il.Create(OpCodes.Ldc_I4, Factors[6]);
        var eight = il.Create(OpCodes.Ldc_I4, Factors[8]);
        var nineOrMore = il.Create(OpCodes.Ldc_I4, Factors[9]);

        EmitReturn(il, OpCodes.Ldarg_0, OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, oneHundred);

        EmitReturn(il, OpCodes.Ldarg_0, OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ble, linear);

        EmitReturn(il, OpCodes.Ldarg_0, OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Beq, five);

        EmitReturn(il, OpCodes.Ldarg_0, OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Ble, sixOrSeven);

        EmitReturn(il, OpCodes.Ldarg_0, OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Beq, eight);
        il.Emit(OpCodes.Br, nineOrMore);

        AppendReturn(il, oneHundred);

        il.Append(linear);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 50);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ret);

        AppendReturn(il, five);
        AppendReturn(il, sixOrSeven);
        AppendReturn(il, eight);
        AppendReturn(il, nineOrMore);
    }

    private static void EmitReturn(
        ILProcessor il,
        OpCode loadIndex,
        OpCode loadLimit)
    {
        il.Emit(loadIndex);
        il.Emit(loadLimit);
    }

    private static void AppendReturn(ILProcessor il, Instruction value)
    {
        il.Append(value);
        il.Append(il.Create(OpCodes.Ret));
    }
}
