using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class SignInDailyCycleFix
{
    private static readonly DateTime CycleEpoch =
        new DateTime(2026, 11, 18);

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: SignInDailyCycleFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var gameplay = module.GetType("YzrLocal.LocalGameplay");
                if (gameplay == null)
                {
                    throw new InvalidOperationException(
                        "LocalGameplay was not found.");
                }

                var cycleMonth = gameplay.Methods.SingleOrDefault(
                    method =>
                        method.Name == "GetCycleRewardMonth" &&
                        method.IsStatic &&
                        method.Parameters.Count == 1);
                if (cycleMonth == null)
                {
                    cycleMonth = AddCycleMonthMethod(module, gameplay);
                    Console.WriteLine(
                        "Added 12-day sign-in reward-table rotation.");
                }

                PatchDailySessionKey(gameplay);
                PatchRewardTableSelection(gameplay, cycleMonth);

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

    private static void PatchDailySessionKey(TypeDefinition gameplay)
    {
        var currentMonth = gameplay.Methods.SingleOrDefault(
            method =>
                method.Name == "CurrentSignInMonth" &&
                method.IsStatic &&
                method.Parameters.Count == 0);
        if (currentMonth == null || !currentMonth.HasBody)
        {
            throw new InvalidOperationException(
                "LocalGameplay.CurrentSignInMonth was not found.");
        }

        var format = currentMonth.Body.Instructions.SingleOrDefault(
            instruction =>
                instruction.OpCode == OpCodes.Ldstr &&
                (string)instruction.Operand == "yyyyMM");
        if (format != null)
        {
            format.Operand = "yyyyMMdd";
            Console.WriteLine(
                "Changed sign-in session reset from month to day.");
            return;
        }

        if (currentMonth.Body.Instructions.Any(
                instruction =>
                    instruction.OpCode == OpCodes.Ldstr &&
                    (string)instruction.Operand == "yyyyMMdd"))
        {
            Console.WriteLine(
                "Daily sign-in session reset is already present.");
            return;
        }

        throw new InvalidOperationException(
            "Unexpected CurrentSignInMonth layout.");
    }

    private static void PatchRewardTableSelection(
        TypeDefinition gameplay,
        MethodDefinition cycleMonth)
    {
        var sync = gameplay.Methods.SingleOrDefault(
            method =>
                method.Name == "SyncSignInState" &&
                method.IsStatic &&
                method.Parameters.Count == 0);
        if (sync == null || !sync.HasBody)
        {
            throw new InvalidOperationException(
                "LocalGameplay.SyncSignInState was not found.");
        }

        var instructions = sync.Body.Instructions;
        var helperCall = instructions.FirstOrDefault(
            instruction =>
            {
                var reference = instruction.Operand as MethodReference;
                return reference != null &&
                       reference.Name == "GetCycleRewardMonth";
            });
        if (helperCall != null)
        {
            Console.WriteLine(
                "Sign-in reward-table rotation is already patched.");
            return;
        }

        for (var index = 1; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            var reference = instruction.Operand as MethodReference;
            if (reference == null ||
                reference.Name != "get_Month" ||
                reference.DeclaringType.FullName != "System.DateTime")
            {
                continue;
            }

            var previous = instructions[index - 1];
            if (previous.OpCode != OpCodes.Ldloca_S)
            {
                throw new InvalidOperationException(
                    "Unexpected DateTime.Now.Month layout.");
            }

            previous.OpCode = OpCodes.Ldloc_S;
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = cycleMonth;
            Console.WriteLine(
                "Patched SyncSignInState to select the reward table " +
                "from the 12-day cycle.");
            return;
        }

        throw new InvalidOperationException(
            "DateTime.Now.Month was not found in SyncSignInState.");
    }

    private static MethodDefinition AddCycleMonthMethod(
        ModuleDefinition module,
        TypeDefinition gameplay)
    {
        var coreLibrary = module.AssemblyReferences.FirstOrDefault(
            reference => reference.Name == "mscorlib");
        if (coreLibrary == null)
        {
            throw new InvalidOperationException(
                "mscorlib reference was not found.");
        }

        var dateType = new TypeReference(
            "System",
            "DateTime",
            module,
            coreLibrary);
        var method = new MethodDefinition(
            "GetCycleRewardMonth",
            MethodAttributes.Public |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Int32);
        method.Parameters.Add(new ParameterDefinition(
            "date",
            ParameterAttributes.None,
            dateType));
        gameplay.Methods.Add(method);

        var getDate = new MethodReference(
            "get_Date",
            dateType,
            dateType)
        {
            HasThis = true
        };
        var getTicks = new MethodReference(
            "get_Ticks",
            module.TypeSystem.Int64,
            dateType)
        {
            HasThis = true
        };

        var date = new VariableDefinition(dateType);
        var delta = new VariableDefinition(module.TypeSystem.Int32);
        method.Body.Variables.Add(date);
        method.Body.Variables.Add(delta);
        method.Body.InitLocals = true;

        var il = method.Body.GetILProcessor();
        Append(
            il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Stloc, date),
            Instruction.Create(OpCodes.Ldloca, date),
            Instruction.Create(OpCodes.Call, getDate),
            Instruction.Create(OpCodes.Stloc, date),
            Instruction.Create(OpCodes.Ldloca, date),
            Instruction.Create(OpCodes.Call, getTicks),
            Instruction.Create(
                OpCodes.Ldc_I8,
                CycleEpoch.Date.Ticks),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(
                OpCodes.Ldc_I8,
                TimeSpan.TicksPerDay),
            Instruction.Create(OpCodes.Div),
            Instruction.Create(OpCodes.Conv_I4),
            Instruction.Create(OpCodes.Stloc, delta),
            Instruction.Create(OpCodes.Ldloc, delta),
            Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)12),
            Instruction.Create(OpCodes.Rem),
            Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)12),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)12),
            Instruction.Create(OpCodes.Rem),
            Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)10),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)12),
            Instruction.Create(OpCodes.Rem),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Ret));
        method.Body.MaxStackSize = 3;
        return method;
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
