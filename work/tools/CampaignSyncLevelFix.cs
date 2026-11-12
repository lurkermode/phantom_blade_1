using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class CampaignSyncLevelFix
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: CampaignSyncLevelFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var type = module.GetType("YzrLocal.LocalCampaignSync");
                if (type == null)
                {
                    throw new InvalidOperationException(
                        "LocalCampaignSync was not found.");
                }

                var method = type.Methods.Single(candidate =>
                    candidate.Name == "SyncLevelInfos");
                var instructions = method.Body.Instructions;
                var clearCall = instructions.Single(instruction =>
                    instruction.OpCode == OpCodes.Call &&
                    instruction.Operand is MethodReference &&
                    ((MethodReference)instruction.Operand).Name ==
                        "ClearLevelInfo");
                var receiverLoad = clearCall.Previous;
                if (receiverLoad == null ||
                    (receiverLoad.OpCode != OpCodes.Ldloc_S &&
                     receiverLoad.OpCode != OpCodes.Ldloc))
                {
                    throw new InvalidOperationException(
                        "Unexpected ClearLevelInfo receiver load.");
                }

                // Preserve existing progress for levels above the player's
                // current level. The old path wrote rankScore=0, which the
                // game treats as a passed C-rank level.
                receiverLoad.OpCode = OpCodes.Nop;
                receiverLoad.Operand = null;
                clearCall.OpCode = OpCodes.Ldc_I4_0;
                clearCall.Operand = null;

                assembly.Write(args[1]);
                Console.WriteLine(
                    "Above-level campaign records are now left untouched.");
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
