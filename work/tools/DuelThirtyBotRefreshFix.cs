using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class DuelThirtyBotRefreshFix
{
    private const int RosterRoleFirst = 700000001;
    private const int RosterRoleLast = 700000026;

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: DuelThirtyBotRefreshFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                MakeRankingBuilderPublic(module);
                var lobby = RequiredType(module, "YzrLocal.LocalDuelLobby");
                var duel = RequiredType(module, "YzrLocal.LocalDuel");
                var refresh = RequiredMethod(
                    lobby,
                    "Refresh",
                    1);
                var displayState = AllTypes(module).Single(type =>
                    type.Name == "<>c__DisplayClass6_0" &&
                    type.DeclaringType == lobby);
                var displayBots = displayState.Fields.Single(
                    field => field.Name == "bots");
                var friendListType =
                    (GenericInstanceType)displayBots.FieldType;
                var listConstructor = ExistingFriendListMethod(
                    refresh,
                    ".ctor",
                    0);
                var getCount = ExistingFriendListMethod(
                    refresh,
                    "get_Count",
                    0);
                var getItem = ExistingFriendListMethod(
                    refresh,
                    "get_Item",
                    1);
                var add = ExistingFriendListMethod(
                    refresh,
                    "Add",
                    1);

                var append = EnsureAppendRankingBots(
                    module,
                    lobby,
                    friendListType,
                    getCount,
                    getItem,
                    add);
                var pick = EnsurePickFour(
                    module,
                    lobby,
                    friendListType,
                    listConstructor,
                    getCount,
                    getItem,
                    add);
                PatchRefresh(
                    refresh,
                    displayBots,
                    append,
                    pick);
                PatchCardsListInitialization(
                    RequiredType(module, "PVPWindow_MatchList"));

                var startRoster = EnsureRosterStartBridge(
                    module,
                    duel);
                PatchOriginalStartOverride(
                    duel,
                    startRoster);
                PatchStartCardRosterBranch(
                    lobby,
                    startRoster);

                assembly.Write(args[1]);
                Console.WriteLine(
                    "Duel list now samples four bots from the 30-bot pool.");
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

    private static void MakeRankingBuilderPublic(
        ModuleDefinition module)
    {
        var rank = RequiredType(module, "YzrLocal.LocalRank");
        var build = RequiredMethod(rank, "BuildPlayers", 0);
        if (!build.IsPublic)
        {
            build.Attributes =
                (build.Attributes & ~MethodAttributes.MemberAccessMask) |
                MethodAttributes.Public;
        }
    }

    private static MethodDefinition EnsureAppendRankingBots(
        ModuleDefinition module,
        TypeDefinition lobby,
        GenericInstanceType friendListType,
        MethodReference getCount,
        MethodReference getItem,
        MethodReference add)
    {
        var existing = lobby.Methods.SingleOrDefault(
            candidate => candidate.Name == "AppendRankingBotsForDuel");
        if (existing != null)
        {
            return existing;
        }

        var method = NewMethod(
            "AppendRankingBotsForDuel",
            module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition(
            "candidates",
            ParameterAttributes.None,
            friendListType));
        method.Parameters.Add(new ParameterDefinition(
            "rankDestination",
            ParameterAttributes.None,
            friendListType));
        method.Body.InitLocals = true;
        method.Body.MaxStackSize = 3;
        lobby.Methods.Add(method);

        var friendType = friendListType.GenericArguments[0];
        var ranking = AddLocal(method, friendListType);
        var index = AddLocal(method, module.TypeSystem.Int32);
        var friend = AddLocal(method, friendType);
        var role = AddLocal(method, module.TypeSystem.Int32);
        var buildPlayers = RequiredMethod(
            RequiredType(module, "YzrLocal.LocalRank"),
            "BuildPlayers",
            0);
        var roleField = RequiredType(module, "FriendData").Fields.Single(
            field => field.Name == "roleID");

        var il = method.Body.GetILProcessor();
        var done = Instruction.Create(OpCodes.Ret);
        var next = Instruction.Create(OpCodes.Nop);
        var check = Instruction.Create(OpCodes.Nop);

        Emit(il,
            Instruction.Create(OpCodes.Call, buildPlayers),
            Instruction.Create(OpCodes.Stloc, ranking),
            Instruction.Create(OpCodes.Ldloc, ranking),
            Instruction.Create(OpCodes.Brfalse, done),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, check));

        il.Append(check);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldloc, ranking),
            Instruction.Create(OpCodes.Callvirt, getCount),
            Instruction.Create(OpCodes.Bge, done),
            Instruction.Create(OpCodes.Ldloc, ranking),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Callvirt, getItem),
            Instruction.Create(OpCodes.Stloc, friend),
            Instruction.Create(OpCodes.Ldloc, friend),
            Instruction.Create(OpCodes.Brfalse, next),
            Instruction.Create(OpCodes.Ldloc, friend),
            Instruction.Create(OpCodes.Ldfld, roleField),
            Instruction.Create(OpCodes.Conv_I4),
            Instruction.Create(OpCodes.Stloc, role),
            Instruction.Create(OpCodes.Ldloc, role),
            Instruction.Create(OpCodes.Ldc_I4, RosterRoleFirst),
            Instruction.Create(OpCodes.Blt, next),
            Instruction.Create(OpCodes.Ldloc, role),
            Instruction.Create(OpCodes.Ldc_I4, RosterRoleLast),
            Instruction.Create(OpCodes.Bgt, next),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldloc, friend),
            Instruction.Create(OpCodes.Callvirt, add),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldloc, friend),
            Instruction.Create(OpCodes.Callvirt, add));

        il.Append(next);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, check));
        il.Append(done);
        return method;
    }

    private static MethodDefinition EnsurePickFour(
        ModuleDefinition module,
        TypeDefinition lobby,
        GenericInstanceType friendListType,
        MethodReference listConstructor,
        MethodReference getCount,
        MethodReference getItem,
        MethodReference add)
    {
        var existing = lobby.Methods.SingleOrDefault(
            candidate => candidate.Name == "PickFourDuelBots");
        if (existing != null)
        {
            return existing;
        }

        var method = NewMethod(
            "PickFourDuelBots",
            friendListType);
        method.Parameters.Add(new ParameterDefinition(
            "candidates",
            ParameterAttributes.None,
            friendListType));
        method.Parameters.Add(new ParameterDefinition(
            "take",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        method.Body.InitLocals = true;
        method.Body.MaxStackSize = 3;
        lobby.Methods.Add(method);

        var selected = AddLocal(method, friendListType);
        var count = AddLocal(method, module.TypeSystem.Int32);
        var chosenIndex = AddLocal(method, module.TypeSystem.Int32);
        var resultIndex = AddLocal(method, module.TypeSystem.Int32);
        var used = AddLocal(
            method,
            new ArrayType(module.TypeSystem.Int32));
        var randomRange = RandomRange(module);

        var il = method.Body.GetILProcessor();
        var done = Instruction.Create(OpCodes.Ldloc, selected);
        var doneRet = Instruction.Create(OpCodes.Ret);
        var directCheck = Instruction.Create(OpCodes.Nop);
        var randomCheck = Instruction.Create(OpCodes.Nop);
        var randomRetry = Instruction.Create(OpCodes.Nop);

        Emit(il,
            Instruction.Create(OpCodes.Newobj, listConstructor),
            Instruction.Create(OpCodes.Stloc, selected),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Brfalse, done),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Blt, done),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Callvirt, getCount),
            Instruction.Create(OpCodes.Stloc, count),
            Instruction.Create(OpCodes.Ldloc, count),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Blt, done),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, resultIndex),
            Instruction.Create(OpCodes.Ldloc, count),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ble, directCheck),
            Instruction.Create(OpCodes.Ldloc, count),
            Instruction.Create(OpCodes.Newarr, module.TypeSystem.Int32),
            Instruction.Create(OpCodes.Stloc, used),
            Instruction.Create(OpCodes.Br, randomCheck));

        il.Append(directCheck);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, resultIndex),
            Instruction.Create(OpCodes.Ldloc, count),
            Instruction.Create(OpCodes.Bge, done),
            Instruction.Create(OpCodes.Ldloc, selected),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldloc, resultIndex),
            Instruction.Create(OpCodes.Callvirt, getItem),
            Instruction.Create(OpCodes.Callvirt, add),
            Instruction.Create(OpCodes.Ldloc, resultIndex),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, resultIndex),
            Instruction.Create(OpCodes.Br, directCheck));

        il.Append(randomCheck);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, resultIndex),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Bge, done),
            Instruction.Create(OpCodes.Br, randomRetry));

        il.Append(randomRetry);
        Emit(il,
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Ldloc, count),
            Instruction.Create(OpCodes.Call, randomRange),
            Instruction.Create(OpCodes.Stloc, chosenIndex),
            Instruction.Create(OpCodes.Ldloc, used),
            Instruction.Create(OpCodes.Ldloc, chosenIndex),
            Instruction.Create(OpCodes.Ldelem_I4),
            Instruction.Create(OpCodes.Brtrue, randomRetry),
            Instruction.Create(OpCodes.Ldloc, used),
            Instruction.Create(OpCodes.Ldloc, chosenIndex),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Stelem_I4),
            Instruction.Create(OpCodes.Ldloc, selected),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldloc, chosenIndex),
            Instruction.Create(OpCodes.Callvirt, getItem),
            Instruction.Create(OpCodes.Callvirt, add),
            Instruction.Create(OpCodes.Ldloc, resultIndex),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, resultIndex),
            Instruction.Create(OpCodes.Br, randomCheck));

        il.Append(done);
        il.Append(doneRet);
        return method;
    }

    private static void PatchCardsListInitialization(
        TypeDefinition windowType)
    {
        var start = RequiredMethod(windowType, "Start", 0);
        var cards = windowType.Fields.Single(
            field => field.Name == "cards");
        var instructions = start.Body.Instructions;
        if (instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Ldfld &&
                instruction.Operand == cards))
        {
            return;
        }

        var store = instructions.First(instruction =>
            instruction.OpCode == OpCodes.Stfld &&
            instruction.Operand == cards);
        var first = instructions[0];
        var skip = Instruction.Create(OpCodes.Nop);
        var il = start.Body.GetILProcessor();
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldfld, cards));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Brtrue, skip));
        il.InsertAfter(store, skip);
    }

    private static void PatchRefresh(
        MethodDefinition refresh,
        FieldDefinition displayBots,
        MethodDefinition append,
        MethodDefinition pick)
    {
        var instructions = refresh.Body.Instructions;
        var setActive = ExistingTypeMethod(
            refresh.Module,
            "UnityEngine.GameObject",
            "SetActive",
            1);
        if (!instructions.Any(instruction =>
                instruction.Operand == setActive))
        {
            var destroy = instructions.First(instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodReference &&
                ((MethodReference)instruction.Operand).Name == "Destroy" &&
                ((MethodReference)instruction.Operand)
                    .DeclaringType.FullName == "UnityEngine.Object");
            var il = refresh.Body.GetILProcessor();
            il.InsertBefore(
                destroy,
                Instruction.Create(OpCodes.Dup));
            il.InsertBefore(
                destroy,
                Instruction.Create(OpCodes.Ldc_I4_0));
            il.InsertBefore(
                destroy,
                Instruction.Create(OpCodes.Callvirt, setActive));
        }

        var sort = instructions.First(instruction =>
            instruction.OpCode == OpCodes.Callvirt &&
            instruction.Operand is MethodReference &&
            ((MethodReference)instruction.Operand).Name == "Sort" &&
            ((MethodReference)instruction.Operand)
                .DeclaringType.FullName.StartsWith(
                    "System.Collections.Generic.List`1<FriendData>"));
        var comparisonField = instructions
            .TakeWhile(instruction => instruction != sort)
            .Reverse()
            .Select(instruction => instruction.Operand)
            .OfType<FieldReference>()
            .First(field =>
                field.DeclaringType.FullName.StartsWith(
                    "YzrLocal.LocalDuelLobby/<>c") &&
                field.FieldType.FullName.StartsWith(
                    "System.Comparison`1<FriendData>"));

        if (!instructions.Any(instruction =>
                instruction.Operand == append))
        {
            var listLoad = sort;
            while (listLoad != null && listLoad.OpCode != OpCodes.Ldloc_3)
            {
                listLoad = listLoad.Previous;
            }
            if (listLoad == null)
            {
                throw new InvalidOperationException(
                    "Unable to find the duel rank list sort receiver.");
            }

            var il = refresh.Body.GetILProcessor();
            il.InsertBefore(
                listLoad,
                Instruction.Create(OpCodes.Ldloc_0));
            il.InsertBefore(
                listLoad,
                Instruction.Create(OpCodes.Ldfld, displayBots));
            il.InsertBefore(
                listLoad,
                Instruction.Create(OpCodes.Ldloc_3));
            il.InsertBefore(
                listLoad,
                Instruction.Create(OpCodes.Call, append));
        }

        if (!instructions.Any(instruction =>
                instruction.Operand == pick))
        {
            var clearCards = instructions.First(instruction =>
                instruction.OpCode == OpCodes.Callvirt &&
                instruction.Operand is MethodReference &&
                ((MethodReference)instruction.Operand).Name ==
                    "Clear" &&
                ((MethodReference)instruction.Operand)
                    .DeclaringType.FullName.StartsWith(
                        "System.Collections.Generic.List`1<UnityEngine.GameObject>"));
            var cardLoopStart = clearCards.Next;
            if (cardLoopStart == null)
            {
                throw new InvalidOperationException(
                    "Duel card creation loop was not found.");
            }

            var il = refresh.Body.GetILProcessor();
            il.InsertBefore(
                cardLoopStart,
                Instruction.Create(OpCodes.Ldloc_0));
            il.InsertBefore(
                cardLoopStart,
                Instruction.Create(OpCodes.Ldloc_0));
            il.InsertBefore(
                cardLoopStart,
                Instruction.Create(OpCodes.Ldfld, displayBots));
            il.InsertBefore(
                cardLoopStart,
                Instruction.Create(OpCodes.Ldc_I4_4));
            il.InsertBefore(
                cardLoopStart,
                Instruction.Create(OpCodes.Call, pick));
            il.InsertBefore(
                cardLoopStart,
                Instruction.Create(OpCodes.Stfld, displayBots));
            il.InsertBefore(
                cardLoopStart,
                Instruction.Create(OpCodes.Ldloc_0));
            il.InsertBefore(
                cardLoopStart,
                Instruction.Create(OpCodes.Ldfld, displayBots));
            il.InsertBefore(
                cardLoopStart,
                Instruction.Create(OpCodes.Ldsfld, comparisonField));
            il.InsertBefore(
                cardLoopStart,
                Instruction.Create(OpCodes.Callvirt, (MethodReference)sort.Operand));
        }
    }

    private static MethodDefinition EnsureRosterStartBridge(
        ModuleDefinition module,
        TypeDefinition duel)
    {
        var playerType = RequiredType(module, "PlayerDataStruct");
        var opponentOverride = duel.Fields.SingleOrDefault(
            field => field.Name == "RosterOpponentOverride");
        if (opponentOverride == null)
        {
            opponentOverride = new FieldDefinition(
                "RosterOpponentOverride",
                FieldAttributes.Private |
                FieldAttributes.Static,
                playerType);
            duel.Fields.Add(opponentOverride);
        }

        var powerOverride = duel.Fields.SingleOrDefault(
            field => field.Name == "RosterPowerOverride");
        if (powerOverride == null)
        {
            powerOverride = new FieldDefinition(
                "RosterPowerOverride",
                FieldAttributes.Private |
                FieldAttributes.Static,
                module.TypeSystem.Int32);
            duel.Fields.Add(powerOverride);
        }

        var method = new MethodDefinition(
            "StartRosterBot",
            MethodAttributes.Public |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        method.Parameters.Add(new ParameterDefinition(
            "roleID",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition(
            "auto",
            ParameterAttributes.None,
            module.TypeSystem.Boolean));
        method.Body.InitLocals = true;
        method.Body.MaxStackSize = 3;
        duel.Methods.Add(method);

        var active = duel.Fields.Single(
            field => field.Name == "Active");
        var start = RequiredMethod(duel, "Start", 3);
        var gameMainGet = ExistingCall(
            start,
            "GameMain",
            "get_Instance",
            0);
        var roster = RequiredType(module, "YzrLocal.LocalBotRoster");
        var botType = RequiredType(
            module,
            "YzrLocal.LocalBotRoster/Bot");
        var findBot = RequiredMethod(roster, "FindBot", 1);
        var unitOf = RequiredMethod(roster, "UnitOf", 1);
        var botPower = botType.Fields.Single(
            field => field.Name == "power");
        var source = RequiredType(
            module,
            "YzrLocal.LocalDuelLobby").Fields.Single(
                field => field.Name == "Source");
        var showTips = FindShowTips(module);
        var bot = AddLocal(method, botType);
        var unit = AddLocal(method, playerType);

        var il = method.Body.GetILProcessor();
        var returnFalse = Instruction.Create(OpCodes.Ldc_I4_0);
        var returnFalseRet = Instruction.Create(OpCodes.Ret);
        var ready = Instruction.Create(OpCodes.Nop);
        var returnTrue = Instruction.Create(OpCodes.Ldc_I4_1);
        var returnTrueRet = Instruction.Create(OpCodes.Ret);

        Emit(il,
            Instruction.Create(OpCodes.Ldsfld, active),
            Instruction.Create(OpCodes.Brtrue, returnFalse),
            Instruction.Create(OpCodes.Call, gameMainGet),
            Instruction.Create(OpCodes.Brfalse, returnFalse),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Conv_U4),
            Instruction.Create(OpCodes.Call, findBot),
            Instruction.Create(OpCodes.Stloc, bot),
            Instruction.Create(OpCodes.Ldloc, bot),
            Instruction.Create(OpCodes.Brfalse, returnFalse),
            Instruction.Create(OpCodes.Ldloc, bot),
            Instruction.Create(OpCodes.Call, unitOf),
            Instruction.Create(OpCodes.Stloc, unit),
            Instruction.Create(OpCodes.Ldloc, unit),
            Instruction.Create(OpCodes.Brtrue, ready),
            Instruction.Create(OpCodes.Ldstr, "人机正在加载，请再试一次"),
            Instruction.Create(OpCodes.Call, showTips),
            Instruction.Create(OpCodes.Br, returnFalse));

        il.Append(ready);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, unit),
            Instruction.Create(OpCodes.Stsfld, opponentOverride),
            Instruction.Create(OpCodes.Ldloc, bot),
            Instruction.Create(OpCodes.Ldfld, botPower),
            Instruction.Create(OpCodes.Stsfld, powerOverride),
            Instruction.Create(OpCodes.Ldsfld, source),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Call, start),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Stsfld, opponentOverride),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stsfld, powerOverride),
            Instruction.Create(OpCodes.Br, returnTrue));

        il.Append(returnTrue);
        il.Append(returnTrueRet);
        il.Append(returnFalse);
        il.Append(returnFalseRet);
        return method;
    }

    private static void PatchOriginalStartOverride(
        TypeDefinition duel,
        MethodDefinition startRoster)
    {
        var start = RequiredMethod(duel, "Start", 3);
        var opponent = duel.Fields.Single(
            field => field.Name == "Opponent");
        var targetPower = duel.Fields.Single(
            field => field.Name == "TargetPower");
        var values = duel.Fields.Single(
            field => field.Name == "values");
        var opponentOverride = duel.Fields.Single(
            field => field.Name == "RosterOpponentOverride");
        var powerOverride = duel.Fields.Single(
            field => field.Name == "RosterPowerOverride");
        var instructions = start.Body.Instructions;
        if (instructions.Any(instruction =>
                instruction.Operand == opponentOverride))
        {
            return;
        }

        var storeOpponent = instructions.First(instruction =>
            instruction.OpCode == OpCodes.Stsfld &&
            instruction.Operand == opponent);
        var after = storeOpponent.Next;
        if (after == null)
        {
            throw new InvalidOperationException(
                "Original duel opponent setup tail was not found.");
        }

        var skip = Instruction.Create(OpCodes.Nop);
        var il = start.Body.GetILProcessor();
        il.InsertBefore(
            after,
            Instruction.Create(OpCodes.Ldsfld, opponentOverride));
        il.InsertBefore(
            after,
            Instruction.Create(OpCodes.Brfalse, skip));
        il.InsertBefore(
            after,
            Instruction.Create(OpCodes.Ldsfld, opponentOverride));
        il.InsertBefore(
            after,
            Instruction.Create(OpCodes.Stsfld, opponent));
        il.InsertBefore(
            after,
            Instruction.Create(OpCodes.Ldsfld, powerOverride));
        il.InsertBefore(
            after,
            Instruction.Create(OpCodes.Stsfld, targetPower));
        il.InsertBefore(
            after,
            Instruction.Create(OpCodes.Ldsfld, values));
        il.InsertBefore(
            after,
            Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)14));
        il.InsertBefore(
            after,
            Instruction.Create(OpCodes.Ldsfld, powerOverride));
        il.InsertBefore(
            after,
            Instruction.Create(OpCodes.Stelem_I4));
        il.InsertBefore(after, skip);
    }

    private static void PatchStartCardRosterBranch(
        TypeDefinition lobby,
        MethodDefinition startRoster)
    {
        var method = RequiredMethod(lobby, "StartCard", 1);
        var instructions = method.Body.Instructions;
        if (instructions.Any(instruction =>
                instruction.Operand == startRoster))
        {
            return;
        }

        var roleField = RequiredType(
            lobby.Module,
            "PlayerCard_S").Fields.Single(
                field => field.Name == "roleID");
        var auto = lobby.Fields.Single(
            field => field.Name == "Auto");
        var first = instructions[0];
        var original = Instruction.Create(OpCodes.Nop);
        var il = method.Body.GetILProcessor();
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldfld, roleField));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldc_I4, RosterRoleFirst));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Blt_Un, original));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldfld, roleField));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldc_I4, RosterRoleLast));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Bgt_Un, original));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldfld, roleField));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Conv_I4));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ldsfld, auto));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Call, startRoster));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Ret));
        il.InsertBefore(first, original);
    }

    private static void PatchStartCard(
        TypeDefinition lobby,
        MethodDefinition startRoster)
    {
        var method = RequiredMethod(lobby, "StartCard", 1);
        if (method.Body.Instructions.Any(instruction =>
                instruction.Operand == startRoster))
        {
            return;
        }

        var firstBot = lobby.Fields.Single(
            field => field.Name == "FirstBot");
        var factors = lobby.Fields.Single(
            field => field.Name == "DuelFactors");
        var source = lobby.Fields.Single(
            field => field.Name == "Source");
        var auto = lobby.Fields.Single(
            field => field.Name == "Auto");
        var start = RequiredMethod(
            RequiredType(lobby.Module, "YzrLocal.LocalDuel"),
            "Start",
            3);
        var findBot = RequiredMethod(
            RequiredType(lobby.Module, "YzrLocal.LocalBotRoster"),
            "FindBot",
            1);
        var cardType = RequiredType(
            lobby.Module,
            "PlayerCard_S");
        var roleField = cardType.Fields.Single(
            field => field.Name == "roleID");

        var body = method.Body;
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        body.InitLocals = false;
        body.MaxStackSize = 4;

        var il = body.GetILProcessor();
        var notLegacy = Instruction.Create(OpCodes.Nop);
        var returnFalse = Instruction.Create(OpCodes.Ldc_I4_0);
        var returnFalseRet = Instruction.Create(OpCodes.Ret);
        var returnTrue = Instruction.Create(OpCodes.Ldc_I4_1);
        var returnTrueRet = Instruction.Create(OpCodes.Ret);

        Emit(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, roleField),
            Instruction.Create(OpCodes.Ldc_I4, unchecked((int)3999999900)),
            Instruction.Create(OpCodes.Blt_Un, notLegacy),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, roleField),
            Instruction.Create(OpCodes.Ldc_I4, unchecked((int)3999999900)),
            Instruction.Create(OpCodes.Ldsfld, factors),
            Instruction.Create(OpCodes.Ldlen),
            Instruction.Create(OpCodes.Conv_I4),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Bge_Un, notLegacy),
            Instruction.Create(OpCodes.Ldsfld, source),
            Instruction.Create(OpCodes.Ldsfld, factors),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, roleField),
            Instruction.Create(OpCodes.Ldc_I4, unchecked((int)3999999900)),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(OpCodes.Conv_I4),
            Instruction.Create(OpCodes.Ldelem_I4),
            Instruction.Create(OpCodes.Ldsfld, auto),
            Instruction.Create(OpCodes.Call, start),
            Instruction.Create(OpCodes.Br, returnTrue));

        il.Append(notLegacy);
        Emit(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, roleField),
            Instruction.Create(OpCodes.Call, findBot),
            Instruction.Create(OpCodes.Brfalse, returnFalse),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, roleField),
            Instruction.Create(OpCodes.Conv_I4),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Ldsfld, auto),
            Instruction.Create(OpCodes.Call, startRoster),
            Instruction.Create(OpCodes.Ret));

        il.Append(returnFalse);
        il.Append(returnFalseRet);
        il.Append(returnTrue);
        il.Append(returnTrueRet);
    }

    private static MethodDefinition EnsureStartRosterBot(
        ModuleDefinition module,
        TypeDefinition duel)
    {
        var existing = duel.Methods.SingleOrDefault(
            candidate => candidate.Name == "StartRosterBot");
        if (existing != null)
        {
            return existing;
        }

        var start = RequiredMethod(duel, "Start", 3);
        var method = NewMethod(
            "StartRosterBot",
            module.TypeSystem.Boolean);
        method.Attributes =
            MethodAttributes.Public |
            MethodAttributes.Static |
            MethodAttributes.HideBySig;
        method.Parameters.Add(new ParameterDefinition(
            "roleID",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition(
            "ignored",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition(
            "auto",
            ParameterAttributes.None,
            module.TypeSystem.Boolean));
        method.Body.InitLocals = true;
        method.Body.MaxStackSize = Math.Max(
            start.Body.MaxStackSize,
            8);
        duel.Methods.Add(method);

        foreach (var variable in start.Body.Variables)
        {
            AddLocal(method, variable.VariableType);
        }
        var botLocal = AddLocal(
            method,
            RequiredType(module, "YzrLocal.LocalBotRoster/Bot"));
        var unitLocal = AddLocal(
            method,
            RequiredType(module, "PlayerDataStruct"));
        var meLocal = AddLocal(
            method,
            RequiredType(module, "PlayerDataStruct"));
        var tempLocal = AddLocal(
            method,
            RequiredType(module, "TempDataManager"));
        var loopLocal = AddLocal(method, module.TypeSystem.Int32);
        var loopPlayerLocal = AddLocal(
            method,
            RequiredType(module, "PlayerDataStruct"));
        var scoreLocal = AddLocal(method, module.TypeSystem.Int32);

        var activeField = duel.Fields.Single(
            field => field.Name == "Active");
        var opponentField = duel.Fields.Single(
            field => field.Name == "Opponent");
        var targetPowerField = duel.Fields.Single(
            field => field.Name == "TargetPower");
        var recordField = duel.Fields.Single(
            field => field.Name == "recordKey");
        var gameMainType = RequiredType(module, "GameMain");
        var gameMainGet = ExistingCall(
            start,
            "GameMain",
            "get_Instance",
            0);
        var saveIdField = gameMainType.Fields.Single(
            field => field.Name == "saveID");
        var dataManagerType = RequiredType(module, "DataManager");
        var dataManagerGet = ExistingCall(
            start,
            "DataManager",
            "get_Instance",
            0);
        var playerField = dataManagerType.Fields.Single(
            field => field.Name == "playerDataStruct");
        var playerType = RequiredType(module, "PlayerDataStruct");
        var playerRole = playerType.Fields.Single(
            field => field.Name == "roleID");
        var playerName = playerType.Fields.Single(
            field => field.Name == "playerName");
        var playerScore = playerType.Fields.Single(
            field => field.Name == "pvpScore");
        var botType = RequiredType(
            module,
            "YzrLocal.LocalBotRoster/Bot");
        var botRole = botType.Fields.Single(
            field => field.Name == "roleID");
        var botPower = botType.Fields.Single(
            field => field.Name == "power");
        var findBot = RequiredMethod(
            RequiredType(module, "YzrLocal.LocalBotRoster"),
            "FindBot",
            1);
        var unitOf = RequiredMethod(
            RequiredType(module, "YzrLocal.LocalBotRoster"),
            "UnitOf",
            1);
        var capture = RequiredMethod(
            RequiredType(module, "YzrLocal.LocalDuelProfile"),
            "Capture",
            1);
        var aiType = RequiredType(module, "YzrLocal.LocalDuelAI");
        var aiDifficulty = aiType.Fields.Single(
            field => field.Name == "Difficulty");
        var aiReset = RequiredMethod(aiType, "Reset", 0);
        var saveGet = ExistingCall(
            start,
            "SaveDataManager",
            "get_Instance",
            0);
        var saveMethod = ExistingCall(
            start,
            "SaveDataManager",
            "Save",
            0);
        var concat4 = ExistingCall(
            start,
            "System.String",
            "Concat",
            4);
        var tempGet = ExistingCall(
            start,
            "TempDataManager",
            "get_Instance",
            0);
        var tempType = RequiredType(module, "TempDataManager");
        var playerListField = tempType.Fields.Single(
            field => field.Name == "playerDataList");
        var battleListField = tempType.Fields.Single(
            field => field.Name == "battlePlayerDataList");
        var playerListType =
            (GenericInstanceType)playerListField.FieldType;
        var battleListType =
            (GenericInstanceType)battleListField.FieldType;
        var listCount = ExistingListMethod(
            module,
            playerType,
            "get_Count",
            0);
        var listGet = ExistingListMethod(
            module,
            playerType,
            "get_Item",
            1);
        var listRemoveAt = ExistingListMethod(
            module,
            playerType,
            "RemoveAt",
            1);
        var listAdd = ExistingListMethod(
            module,
            playerType,
            "Add",
            1);
        var battleClear = ExistingListMethod(
            module,
            playerType,
            "Clear",
            0);
        var battleAdd = listAdd;
        var showTips = FindShowTips(module);

        var il = method.Body.GetILProcessor();
        var returnFalse = Instruction.Create(OpCodes.Ldc_I4_0);
        var returnFalseRet = Instruction.Create(OpCodes.Ret);
        var returnTrue = Instruction.Create(OpCodes.Ldc_I4_1);
        var returnTrueRet = Instruction.Create(OpCodes.Ret);
        var removeLoop = Instruction.Create(OpCodes.Nop);
        var removeNext = Instruction.Create(OpCodes.Nop);
        var removeDone = Instruction.Create(OpCodes.Nop);
        var afterReady = Instruction.Create(OpCodes.Nop);

        Emit(il,
            Instruction.Create(OpCodes.Ldsfld, activeField),
            Instruction.Create(OpCodes.Brtrue, returnFalse),
            Instruction.Create(OpCodes.Call, gameMainGet),
            Instruction.Create(OpCodes.Stloc_0),
            Instruction.Create(OpCodes.Ldloc_0),
            Instruction.Create(OpCodes.Brfalse, returnFalse),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Conv_U4),
            Instruction.Create(OpCodes.Call, findBot),
            Instruction.Create(OpCodes.Stloc, botLocal),
            Instruction.Create(OpCodes.Ldloc, botLocal),
            Instruction.Create(OpCodes.Brfalse, returnFalse),
            Instruction.Create(OpCodes.Ldloc, botLocal),
            Instruction.Create(OpCodes.Call, unitOf),
            Instruction.Create(OpCodes.Stloc, unitLocal),
            Instruction.Create(OpCodes.Ldloc, unitLocal),
            Instruction.Create(OpCodes.Brtrue, afterReady),
            Instruction.Create(OpCodes.Ldstr, "人机正在加载，请再点一次挑战"),
            Instruction.Create(OpCodes.Call, showTips),
            Instruction.Create(OpCodes.Br, returnTrue));

        il.Append(afterReady);
        Emit(il,
            Instruction.Create(OpCodes.Call, dataManagerGet),
            Instruction.Create(OpCodes.Ldfld, playerField),
            Instruction.Create(OpCodes.Stloc, meLocal),
            Instruction.Create(OpCodes.Ldloc, meLocal),
            Instruction.Create(OpCodes.Brfalse, returnFalse),

            Instruction.Create(OpCodes.Ldc_I4, 800),
            Instruction.Create(OpCodes.Ldloc, botLocal),
            Instruction.Create(OpCodes.Ldfld, botPower),
            Instruction.Create(OpCodes.Ldc_I4, 2500),
            Instruction.Create(OpCodes.Div),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Ldloc, botLocal),
            Instruction.Create(OpCodes.Ldfld, botRole),
            Instruction.Create(OpCodes.Ldc_I4, 600),
            Instruction.Create(OpCodes.Rem_Un),
            Instruction.Create(OpCodes.Conv_I4),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, scoreLocal),
            Instruction.Create(OpCodes.Ldloc, unitLocal),
            Instruction.Create(OpCodes.Ldloc, scoreLocal),
            Instruction.Create(OpCodes.Stfld, playerScore),

            Instruction.Create(OpCodes.Ldc_I4_2),
            Instruction.Create(OpCodes.Stsfld, aiDifficulty),
            Instruction.Create(OpCodes.Call, aiReset),
            Instruction.Create(OpCodes.Call, saveGet),
            Instruction.Create(OpCodes.Callvirt, saveMethod),

            Instruction.Create(OpCodes.Ldstr, "yzr_duel_record_"),
            Instruction.Create(OpCodes.Ldloc_0),
            Instruction.Create(OpCodes.Ldfld, saveIdField),
            Instruction.Create(OpCodes.Stloc, loopLocal),
            Instruction.Create(OpCodes.Ldloca_S, loopLocal),
            Instruction.Create(OpCodes.Call, IntToString(module)),
            Instruction.Create(OpCodes.Ldstr, "_"),
            Instruction.Create(OpCodes.Ldloc, meLocal),
            Instruction.Create(OpCodes.Ldfld, playerName),
            Instruction.Create(OpCodes.Call, concat4),
            Instruction.Create(OpCodes.Stsfld, recordField),

            Instruction.Create(OpCodes.Ldloc, botLocal),
            Instruction.Create(OpCodes.Ldfld, botPower),
            Instruction.Create(OpCodes.Stsfld, targetPowerField),
            Instruction.Create(OpCodes.Ldloc, unitLocal),
            Instruction.Create(OpCodes.Stsfld, opponentField),
            Instruction.Create(OpCodes.Call, tempGet),
            Instruction.Create(OpCodes.Stloc, tempLocal),
            Instruction.Create(OpCodes.Ldloc, tempLocal),
            Instruction.Create(OpCodes.Brfalse, returnFalse),
            Instruction.Create(OpCodes.Ldloc, tempLocal),
            Instruction.Create(OpCodes.Ldfld, battleListField),
            Instruction.Create(OpCodes.Callvirt, battleClear),

            Instruction.Create(OpCodes.Ldloc, tempLocal),
            Instruction.Create(OpCodes.Ldfld, playerListField),
            Instruction.Create(OpCodes.Callvirt, listCount),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(OpCodes.Stloc, loopLocal),
            Instruction.Create(OpCodes.Br, removeLoop));

        il.Append(removeLoop);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, loopLocal),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Blt, removeDone),
            Instruction.Create(OpCodes.Ldloc, tempLocal),
            Instruction.Create(OpCodes.Ldfld, playerListField),
            Instruction.Create(OpCodes.Ldloc, loopLocal),
            Instruction.Create(OpCodes.Callvirt, listGet),
            Instruction.Create(OpCodes.Stloc, loopPlayerLocal),
            Instruction.Create(OpCodes.Ldloc, loopPlayerLocal),
            Instruction.Create(OpCodes.Brfalse, removeNext),
            Instruction.Create(OpCodes.Ldloc, loopPlayerLocal),
            Instruction.Create(OpCodes.Ldfld, playerRole),
            Instruction.Create(OpCodes.Ldloc, meLocal),
            Instruction.Create(OpCodes.Ldfld, playerRole),
            Instruction.Create(OpCodes.Beq, removeNext),
            Instruction.Create(OpCodes.Ldloc, tempLocal),
            Instruction.Create(OpCodes.Ldfld, playerListField),
            Instruction.Create(OpCodes.Ldloc, loopLocal),
            Instruction.Create(OpCodes.Callvirt, listRemoveAt));

        il.Append(removeNext);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, loopLocal),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(OpCodes.Stloc, loopLocal),
            Instruction.Create(OpCodes.Br, removeLoop));

        il.Append(removeDone);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, tempLocal),
            Instruction.Create(OpCodes.Ldfld, playerListField),
            Instruction.Create(OpCodes.Ldloc, unitLocal),
            Instruction.Create(OpCodes.Callvirt, listAdd),
            Instruction.Create(OpCodes.Ldloc, tempLocal),
            Instruction.Create(OpCodes.Ldfld, battleListField),
            Instruction.Create(OpCodes.Ldloc, unitLocal),
            Instruction.Create(OpCodes.Callvirt, battleAdd),
            Instruction.Create(OpCodes.Ldloc, meLocal),
            Instruction.Create(OpCodes.Call, capture),
            Instruction.Create(
                OpCodes.Stloc_S,
                method.Body.Variables[4]));

        var opponentStore = start.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Stsfld &&
            instruction.Operand == opponentField);
        var addCalls = start.Body.Instructions
            .Where(instruction =>
                instruction.OpCode == OpCodes.Callvirt &&
                instruction.Operand is MethodReference &&
                ((MethodReference)instruction.Operand).Name == "Add" &&
                ((MethodReference)instruction.Operand)
                    .DeclaringType.FullName.StartsWith(
                        "System.Collections.Generic.List`1<PlayerDataStruct>") &&
                instruction.Offset > opponentStore.Offset)
            .Take(2)
            .ToList();
        if (addCalls.Count != 2)
        {
            throw new InvalidOperationException(
                "Unable to locate LocalDuel battle setup.");
        }

        var tailStart = addCalls[1].Next;
        var originalTail = new List<Instruction>();
        for (var instruction = tailStart;
             instruction != null;
             instruction = instruction.Next)
        {
            originalTail.Add(instruction);
        }
        var lastRet = originalTail.Last();
        if (lastRet.OpCode != OpCodes.Ret)
        {
            throw new InvalidOperationException(
                "Unexpected LocalDuel setup tail.");
        }

        var clones = new Dictionary<Instruction, Instruction>();
        foreach (var original in originalTail)
        {
            clones[original] = original == lastRet
                ? returnTrue
                : CreateSkeleton(original);
        }
        foreach (var original in originalTail)
        {
            if (original != lastRet)
            {
                clones[original].Operand = MapOperand(
                    original.Operand,
                    method,
                    clones);
            }
        }
        foreach (var original in originalTail)
        {
            if (original == lastRet)
            {
                continue;
            }
            il.Append(clones[original]);
        }
        il.Append(returnTrue);
        il.Append(returnTrueRet);
        il.Append(returnFalse);
        il.Append(returnFalseRet);
        return method;
    }

    private static object MapOperand(
        object operand,
        MethodDefinition target,
        Dictionary<Instruction, Instruction> clones)
    {
        if (operand is Instruction)
        {
            Instruction mapped;
            if (clones.TryGetValue((Instruction)operand, out mapped))
            {
                return mapped;
            }
            throw new InvalidOperationException(
                "IL branch target was outside the copied block.");
        }
        if (operand is Instruction[])
        {
            var source = (Instruction[])operand;
            var mapped = new Instruction[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                Instruction value;
                mapped[i] = clones.TryGetValue(source[i], out value)
                    ? value
                    : source[i];
            }
            return mapped;
        }
        if (operand is VariableDefinition)
        {
            return target.Body.Variables[
                ((VariableDefinition)operand).Index];
        }
        if (operand is ParameterDefinition)
        {
            var parameter = (ParameterDefinition)operand;
            return target.Parameters[parameter.Index];
        }
        return operand;
    }

    private static Instruction CreateSkeleton(
        Instruction original)
    {
        var operand = original.Operand;
        if (operand == null)
        {
            return Instruction.Create(original.OpCode);
        }
        if (operand is string)
        {
            return Instruction.Create(
                original.OpCode,
                (string)operand);
        }
        if (operand is sbyte)
        {
            return Instruction.Create(
                original.OpCode,
                (sbyte)operand);
        }
        if (operand is byte)
        {
            return Instruction.Create(
                original.OpCode,
                (byte)operand);
        }
        if (operand is int)
        {
            return Instruction.Create(
                original.OpCode,
                (int)operand);
        }
        if (operand is long)
        {
            return Instruction.Create(
                original.OpCode,
                (long)operand);
        }
        if (operand is float)
        {
            return Instruction.Create(
                original.OpCode,
                (float)operand);
        }
        if (operand is double)
        {
            return Instruction.Create(
                original.OpCode,
                (double)operand);
        }
        if (operand is MethodReference)
        {
            return Instruction.Create(
                original.OpCode,
                (MethodReference)operand);
        }
        if (operand is FieldReference)
        {
            return Instruction.Create(
                original.OpCode,
                (FieldReference)operand);
        }
        if (operand is TypeReference)
        {
            return Instruction.Create(
                original.OpCode,
                (TypeReference)operand);
        }
        if (operand is ParameterDefinition)
        {
            return Instruction.Create(
                original.OpCode,
                (ParameterDefinition)operand);
        }
        if (operand is VariableDefinition)
        {
            return Instruction.Create(
                original.OpCode,
                (VariableDefinition)operand);
        }
        if (operand is Instruction)
        {
            return Instruction.Create(
                original.OpCode,
                (Instruction)operand);
        }
        if (operand is Instruction[])
        {
            return Instruction.Create(
                original.OpCode,
                (Instruction[])operand);
        }
        throw new InvalidOperationException(
            "Unsupported skeleton operand: " +
            operand.GetType().FullName);
    }

    private static MethodReference FindShowTips(
        ModuleDefinition module)
    {
        foreach (var type in AllTypes(module))
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                {
                    continue;
                }
                foreach (var instruction in method.Body.Instructions)
                {
                    var reference =
                        instruction.Operand as MethodReference;
                    if (reference != null &&
                        reference.Name == "ShowTips" &&
                        reference.DeclaringType.FullName ==
                            "YuXueUIUtils" &&
                        reference.Parameters.Count == 1)
                    {
                        return reference;
                    }
                }
            }
        }
        throw new InvalidOperationException(
            "YuXueUIUtils.ShowTips was not found.");
    }

    private static MethodReference RandomRange(
        ModuleDefinition module)
    {
        var assembly = module.AssemblyReferences.FirstOrDefault(
            reference =>
                reference.Name == "UnityEngine" ||
                reference.Name.StartsWith("UnityEngine"));
        if (assembly == null)
        {
            throw new InvalidOperationException(
                "UnityEngine assembly reference was not found.");
        }

        var type = new TypeReference(
            "UnityEngine",
            "Random",
            module,
            assembly);
        var method = new MethodReference(
            "Range",
            module.TypeSystem.Int32,
            type)
        {
            HasThis = false,
            ExplicitThis = false
        };
        method.Parameters.Add(new ParameterDefinition(
            module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition(
            module.TypeSystem.Int32));
        return module.ImportReference(method);
    }

    private static MethodReference IntToString(
        ModuleDefinition module)
    {
        var method = new MethodReference(
            "ToString",
            module.TypeSystem.String,
            module.TypeSystem.Int32)
        {
            HasThis = true,
            ExplicitThis = false
        };
        return module.ImportReference(method);
    }

    private static MethodReference ExistingCall(
        MethodDefinition method,
        string declaringType,
        string name,
        int parameterCount)
    {
        var reference = method.Body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .FirstOrDefault(candidate =>
                candidate.Name == name &&
                candidate.DeclaringType.FullName == declaringType &&
                candidate.Parameters.Count == parameterCount);
        if (reference == null)
        {
            throw new InvalidOperationException(
                "Unable to reuse " + declaringType + "." + name);
        }
        return reference;
    }

    private static MethodReference ExistingTypeMethod(
        ModuleDefinition module,
        string declaringType,
        string name,
        int parameterCount)
    {
        var reference = AllTypes(module)
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .FirstOrDefault(candidate =>
                candidate.Name == name &&
                candidate.DeclaringType.FullName == declaringType &&
                candidate.Parameters.Count == parameterCount);
        if (reference == null)
        {
            throw new InvalidOperationException(
                "Unable to reuse " + declaringType + "." + name);
        }
        return reference;
    }

    private static MethodReference ExistingGenericMethod(
        ModuleDefinition module,
        string declaringType,
        string name,
        string genericArgument,
        int parameterCount)
    {
        var reference = AllTypes(module)
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<GenericInstanceMethod>()
            .FirstOrDefault(candidate =>
                candidate.Name == name &&
                candidate.DeclaringType.FullName == declaringType &&
                candidate.Parameters.Count == parameterCount &&
                candidate.GenericArguments.Count == 1 &&
                candidate.GenericArguments[0].FullName ==
                    genericArgument);
        if (reference == null)
        {
            throw new InvalidOperationException(
                "Unable to reuse " + declaringType + "." + name +
                "<" + genericArgument + ">");
        }
        return reference;
    }

    private static MethodReference ExistingFriendListMethod(
        MethodDefinition method,
        string name,
        int parameterCount)
    {
        var reference = method.Body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .FirstOrDefault(candidate =>
                candidate.Name == name &&
                candidate.DeclaringType.FullName.StartsWith(
                    "System.Collections.Generic.List`1<FriendData>") &&
                candidate.Parameters.Count == parameterCount);
        if (reference == null)
        {
            throw new InvalidOperationException(
                "Unable to reuse List<FriendData>." + name);
        }
        return reference;
    }

    private static MethodReference ExistingListMethod(
        ModuleDefinition module,
        TypeReference elementType,
        string name,
        int parameterCount)
    {
        var declaringType =
            "System.Collections.Generic.List`1<" +
            elementType.FullName + ">";
        var reference = AllTypes(module)
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .FirstOrDefault(candidate =>
                candidate.Name == name &&
                candidate.DeclaringType.FullName == declaringType &&
                candidate.Parameters.Count == parameterCount);
        if (reference == null)
        {
            throw new InvalidOperationException(
                "Unable to reuse " + declaringType + "." + name);
        }
        return reference;
    }

    private static MethodDefinition RequiredMethod(
        TypeDefinition type,
        string name,
        int parameterCount)
    {
        var method = type.Methods.SingleOrDefault(candidate =>
            candidate.Name == name &&
            candidate.Parameters.Count == parameterCount);
        if (method == null)
        {
            throw new InvalidOperationException(
                "Method not found: " + type.FullName + "." + name);
        }
        return method;
    }

    private static TypeDefinition RequiredType(
        ModuleDefinition module,
        string name)
    {
        var type = module.GetType(name);
        if (type == null)
        {
            throw new InvalidOperationException(
                "Type not found: " + name);
        }
        return type;
    }

    private static IEnumerable<TypeDefinition> AllTypes(
        ModuleDefinition module)
    {
        return module.Types.SelectMany(Types);
    }

    private static IEnumerable<TypeDefinition> Types(
        TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes)
        {
            foreach (var child in Types(nested))
            {
                yield return child;
            }
        }
    }

    private static VariableDefinition AddLocal(
        MethodDefinition method,
        TypeReference type)
    {
        var variable = new VariableDefinition(type);
        method.Body.Variables.Add(variable);
        return variable;
    }

    private static MethodDefinition NewMethod(
        string name,
        TypeReference returnType)
    {
        return new MethodDefinition(
            name,
            MethodAttributes.Private |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            returnType);
    }

    private static void Emit(
        ILProcessor il,
        params Instruction[] instructions)
    {
        foreach (var instruction in instructions)
        {
            il.Append(instruction);
        }
    }
}
