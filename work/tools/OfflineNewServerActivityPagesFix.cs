using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class OfflineNewServerActivityPagesFix
{
    private const int ActivityBeginSeconds = 1568217600;
    private const int ActivityEndSeconds = 1570723199;
    private const int OpenSubWindowEvent = 117;
    private const int AskNewServerActivityListEvent = 0xEAA;

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: OfflineNewServerActivityPagesFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var newController = module.GetType("NewServerAcitivityController");
                var newInfo = module.GetType("NewServerActivityInfo");
                var topMenu = module.GetType("TopMenu");
                if (newController == null ||
                    newInfo == null ||
                    topMenu == null)
                {
                    throw new InvalidOperationException(
                        "Required activity types were not found.");
                }

                var prepareInfo = EnsureOfflineInfoPreparation(
                    module,
                    newInfo);
                InitializeNewActivityContent(module);
                var prepareCatalog = EnsureNewActivityPreparation(
                    module,
                    newController,
                    newInfo,
                    prepareInfo);
                PatchNewActivityController(
                    module,
                    newController,
                    prepareCatalog);
                PatchNewActivityButtons(
                    module,
                    topMenu,
                    prepareCatalog);
                EnsureNewActivityButtonVisible(topMenu);
                RemoveNewActivityNetworkRequest(module);

                assembly.Write(args[1]);
            }

            Console.WriteLine("Enabled the separate new-server activity button.");
            Console.WriteLine("Prepared all 6 new-server activities for offline viewing.");
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

    private static MethodDefinition EnsureOfflineInfoPreparation(
        ModuleDefinition module,
        TypeDefinition infoType)
    {
        var method = infoType.Methods.SingleOrDefault(
            candidate => candidate.Name == "PrepareOffline");
        if (method != null)
        {
            return method;
        }

        method = new MethodDefinition(
            "PrepareOffline",
            MethodAttributes.Public |
            MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition(
            "activityId",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition(
            "beginSeconds",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition(
            "endSeconds",
            ParameterAttributes.None,
            module.TypeSystem.Int32));
        method.Body.InitLocals = true;
        method.Body.MaxStackSize = 2;
        infoType.Methods.Add(method);

        var prototypeId = infoType.Fields.Single(
            field => field.Name == "mnPrototypeID");
        var prototypeData = infoType.Fields.Single(
            field => field.Name == "mPrototypeData");
        var beginTime = infoType.Fields.Single(
            field => field.Name == "muBeginTime");
        var endTime = infoType.Fields.Single(
            field => field.Name == "muEndTime");
        var serverContent = infoType.Fields.Single(
            field => field.Name == "mServerContent");
        var prototypeLocal = new VariableDefinition(prototypeData.FieldType);
        method.Body.Variables.Add(prototypeLocal);
        var getPrototypeData = infoType.Methods.Single(
            candidate => candidate.Name == "get_prototypeData");
        var prototypeDataType = getPrototypeData.ReturnType.Resolve();
        var typeField = prototypeDataType.Fields.Single(
            field => field.Name == "type");
        var createContent = module.GetType("NewServerActivityUtils")
            .Methods.Single(candidate =>
                candidate.Name == "CreateNewServerActivityContent");
        var setActivityInfo = module.GetType("INewServerActivityContent")
            .Methods.Single(candidate =>
                candidate.Name == "set_activityInfo");

        var il = method.Body.GetILProcessor();
        var returnInstruction = Instruction.Create(OpCodes.Ret);
        var callCreateContent = Instruction.Create(
            OpCodes.Call,
            createContent);
        Emit(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Stfld, prototypeId),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Stfld, prototypeData),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg_2),
            Instruction.Create(OpCodes.Stfld, beginTime),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg_3),
            Instruction.Create(OpCodes.Stfld, endTime),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, getPrototypeData),
            Instruction.Create(OpCodes.Stloc, prototypeLocal),
            Instruction.Create(OpCodes.Ldloc, prototypeLocal),
            Instruction.Create(OpCodes.Brfalse, returnInstruction),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldloc, prototypeLocal),
            Instruction.Create(OpCodes.Ldfld, typeField),
            callCreateContent,
            Instruction.Create(OpCodes.Stfld, serverContent),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, serverContent),
            Instruction.Create(OpCodes.Brfalse, returnInstruction),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, serverContent),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Callvirt, setActivityInfo));
        il.Append(returnInstruction);
        return method;
    }

    private static void InitializeNewActivityContent(
        ModuleDefinition module)
    {
        var firstCharge = module.GetType("FirstChargeActivityContent");
        var firstChargePrototype = firstCharge.Fields.Single(
            field => field.Name == "PROTOTYPE_ID");
        var firstChargeCtor = firstCharge.Methods.Single(
            method => method.IsConstructor &&
                      method.Parameters.Count == 0);
        if (!firstChargeCtor.Body.Instructions.Any(instruction =>
                instruction.OpCode ==
                (firstChargePrototype.IsStatic
                    ? OpCodes.Stsfld
                    : OpCodes.Stfld) &&
                instruction.Operand == firstChargePrototype))
        {
            if (firstChargePrototype.IsStatic)
            {
                InsertBeforeReturn(
                    firstChargeCtor,
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(
                        OpCodes.Stsfld,
                        firstChargePrototype));
            }
            else
            {
                InsertBeforeReturn(
                    firstChargeCtor,
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(
                        OpCodes.Stfld,
                        firstChargePrototype));
            }
        }

        InitializeDictionaryFields(
            module,
            "ServerFirstChargeActivityContent",
            "mRewardStatusDictNormal",
            "mRewardStatusDictVIP");
        InitializeDictionaryFields(
            module,
            "VIPPrizeActivityContent",
            "mRewardStatusDict");
        InitializeDictionaryFields(
            module,
            "TimeMachineActivityContent",
            "mRewardStatusDict");
        InitializeDictionaryFields(
            module,
            "CirclePayActivityContent",
            "mRewardStatusDict");
    }

    private static void InitializeDictionaryFields(
        ModuleDefinition module,
        string typeName,
        params string[] fieldNames)
    {
        var type = module.GetType(typeName);
        var ctor = type.Methods.Single(
            method => method.IsConstructor &&
                      method.Parameters.Count == 0);
        foreach (var fieldName in fieldNames)
        {
            var field = type.Fields.Single(
                candidate => candidate.Name == fieldName);
            if (ctor.Body.Instructions.Any(instruction =>
                    instruction.OpCode == OpCodes.Stfld &&
                    instruction.Operand == field))
            {
                continue;
            }

            var dictionaryType = (GenericInstanceType)field.FieldType;
            var dictionaryCtor = module.GetMemberReferences()
                .OfType<MethodReference>()
                .First(reference =>
                    reference.Name == ".ctor" &&
                    reference.DeclaringType.FullName ==
                    dictionaryType.FullName &&
                    reference.Parameters.Count == 0);
            InsertBeforeReturn(
                ctor,
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Newobj, dictionaryCtor),
                Instruction.Create(OpCodes.Stfld, field));
        }
    }

    private static MethodDefinition EnsureNewActivityPreparation(
        ModuleDefinition module,
        TypeDefinition controller,
        TypeDefinition infoType,
        MethodDefinition prepareInfo)
    {
        var method = controller.Methods.SingleOrDefault(
            candidate => candidate.Name == "PrepareOfflinePages");
        if (method != null)
        {
            return method;
        }

        method = new MethodDefinition(
            "PrepareOfflinePages",
            MethodAttributes.Public |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        method.Body.InitLocals = true;
        method.Body.MaxStackSize = 5;
        controller.Methods.Add(method);

        var controllerLocal = new VariableDefinition(controller);
        var infoListType = controller.Methods.Single(
            candidate => candidate.Name == "get_activityInfoList")
            .ReturnType;
        var infoList = new VariableDefinition(infoListType);
        var index = new VariableDefinition(module.TypeSystem.Int32);
        method.Body.Variables.Add(controllerLocal);
        method.Body.Variables.Add(infoList);
        method.Body.Variables.Add(index);

        var getTempManager = module.GetType("TempDataManager")
            .Methods.Single(candidate =>
                candidate.Name == "get_Instance" &&
                candidate.IsStatic);
        var getNewController = module.GetType("TempDataManager")
            .Methods.Single(candidate =>
                candidate.Name == "get_theNewServerActivityController");
        var getInfoList = controller.Methods.Single(
            candidate => candidate.Name == "get_activityInfoList");
        var clearInfoList = FindGenericReference(
            module,
            (GenericInstanceType)infoListType,
            "Clear");
        var addInfo = FindGenericReference(
            module,
            (GenericInstanceType)infoListType,
            "Add");
        var infoCtor = infoType.Methods.Single(candidate =>
            candidate.IsConstructor &&
            candidate.Parameters.Count == 0);
        var il = method.Body.GetILProcessor();
        var check = Instruction.Create(OpCodes.Nop);
        var body = Instruction.Create(OpCodes.Nop);

        Emit(il,
            Instruction.Create(OpCodes.Call, getTempManager),
            Instruction.Create(OpCodes.Callvirt, getNewController),
            Instruction.Create(OpCodes.Stloc, controllerLocal),
            Instruction.Create(OpCodes.Ldloc, controllerLocal),
            Instruction.Create(OpCodes.Callvirt, getInfoList),
            Instruction.Create(OpCodes.Stloc, infoList),
            Instruction.Create(OpCodes.Ldloc, infoList),
            Instruction.Create(OpCodes.Callvirt, clearInfoList),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, check));

        il.Append(body);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, infoList),
            Instruction.Create(OpCodes.Newobj, infoCtor),
            Instruction.Create(OpCodes.Dup),
            Instruction.Create(OpCodes.Ldc_I4, 200000),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Ldc_I4, ActivityBeginSeconds),
            Instruction.Create(OpCodes.Ldc_I4, ActivityEndSeconds),
            Instruction.Create(OpCodes.Callvirt, prepareInfo),
            Instruction.Create(OpCodes.Callvirt, addInfo),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, index));

        il.Append(check);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_6),
            Instruction.Create(OpCodes.Blt, body),
            Instruction.Create(OpCodes.Ret));
        return method;
    }

    private static void PatchNewActivityController(
        ModuleDefinition module,
        TypeDefinition controller,
        MethodDefinition prepareCatalog)
    {
        ReplaceWithLocalPreparation(
            controller.Methods.Single(method =>
                method.Name == "AskAllActivityInfoList"),
            prepareCatalog);

        ReplaceWithLocalPreparationAndOpen(
            module,
            controller.Methods.Single(method =>
                method.Name == "AskAllActivityInfoListOnOpenWindow"),
            prepareCatalog);

        var askOne = controller.Methods.Single(method =>
            method.Name == "AskActivityInfo" &&
            method.Parameters.Count == 1);
        askOne.Body.Instructions.Clear();
        askOne.Body.ExceptionHandlers.Clear();
        askOne.Body.InitLocals = false;
        askOne.Body.MaxStackSize = 1;
        askOne.Body.GetILProcessor().Append(
            Instruction.Create(OpCodes.Ret));
    }

    private static void PatchNewActivityButtons(
        ModuleDefinition module,
        TypeDefinition topMenu,
        MethodDefinition prepareCatalog)
    {
        ReplaceWithLocalPreparationAndOpen(
            module,
            topMenu.Methods.Single(method =>
                method.Name == "OnButtonNewActivity"),
            prepareCatalog);
    }

    private static void EnsureNewActivityButtonVisible(
        TypeDefinition topMenu)
    {
        var init = topMenu.Methods.Single(method =>
            method.Name == "Init");
        var buttonField = topMenu.Fields.Single(field =>
            field.Name == "newActivityButtonObj");
        if (init.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Ldfld &&
                instruction.Operand == buttonField))
        {
            return;
        }

        var first = init.Body.Instructions[0];
        var setActive = topMenu.Methods
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Where(instruction =>
                instruction.OpCode == OpCodes.Callvirt &&
                instruction.Operand is MethodReference &&
                ((MethodReference)instruction.Operand).Name ==
                "SetActive" &&
                ((MethodReference)instruction.Operand).DeclaringType.FullName ==
                "UnityEngine.GameObject")
            .Select(instruction => (MethodReference)instruction.Operand)
            .First();
        var continueInstruction = Instruction.Create(OpCodes.Nop);
        var il = init.Body.GetILProcessor();
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldfld, buttonField));
        il.InsertBefore(first, Instruction.Create(
            OpCodes.Brfalse,
            continueInstruction));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldfld, buttonField));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(first, Instruction.Create(
            OpCodes.Callvirt,
            setActive));
        il.InsertBefore(first, continueInstruction);
    }

    private static void RemoveNewActivityNetworkRequest(
        ModuleDefinition module)
    {
        var init = module.GetType("UINewServerActivityWindow").Methods.Single(
            method => method.Name == "Init");
        var request = init.Body.Instructions.SingleOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldc_I4 &&
            instruction.Operand is int &&
            (int)instruction.Operand == AskNewServerActivityListEvent);
        if (request == null)
        {
            return;
        }

        var start = request.Previous;
        var constructor = request.Next;
        var post = constructor == null ? null : constructor.Next;
        if (start == null ||
            constructor == null ||
            post == null ||
            constructor.OpCode != OpCodes.Newobj ||
            post.OpCode != OpCodes.Callvirt)
        {
            throw new InvalidOperationException(
                "Unexpected new-server activity request sequence.");
        }

        start.OpCode = OpCodes.Nop;
        start.Operand = null;
        request.OpCode = OpCodes.Nop;
        request.Operand = null;
        constructor.OpCode = OpCodes.Nop;
        constructor.Operand = null;
        post.OpCode = OpCodes.Nop;
        post.Operand = null;
    }

    private static void ReplaceWithLocalPreparation(
        MethodDefinition method,
        MethodDefinition prepareCatalog)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.InitLocals = false;
        method.Body.MaxStackSize = 1;
        var il = method.Body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Call, prepareCatalog));
        il.Append(Instruction.Create(OpCodes.Ret));
    }

    private static void ReplaceWithLocalPreparationAndOpen(
        ModuleDefinition module,
        MethodDefinition method,
        MethodDefinition prepareCatalog)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.InitLocals = true;
        method.Body.MaxStackSize = 4;

        var messageType = module.GetType("MessageEventArgs");
        var messageCtor = messageType.Methods.Single(candidate =>
            candidate.IsConstructor &&
            candidate.Parameters.Count == 0);
        var addMessage = messageType.Methods.Single(candidate =>
            candidate.Name == "AddMessage");
        var eventManager = module.GetType("EventManager");
        var getEventManager = eventManager.Methods.Single(candidate =>
            candidate.Name == "get_Instance" &&
            candidate.IsStatic);
        var postEvent = eventManager.Methods.First(candidate =>
            candidate.Name == "PostEvent" &&
            candidate.Parameters.Count == 2);
        var message = new VariableDefinition(messageType);
        method.Body.Variables.Add(message);

        var il = method.Body.GetILProcessor();
        Emit(il,
            Instruction.Create(OpCodes.Call, prepareCatalog),
            Instruction.Create(OpCodes.Newobj, messageCtor),
            Instruction.Create(OpCodes.Stloc, message),
            Instruction.Create(OpCodes.Ldloc, message),
            Instruction.Create(OpCodes.Ldstr, "UIName"),
            Instruction.Create(OpCodes.Ldstr, "UINewServerActivityWindow"),
            Instruction.Create(OpCodes.Callvirt, addMessage),
            Instruction.Create(OpCodes.Call, getEventManager),
            Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)OpenSubWindowEvent),
            Instruction.Create(OpCodes.Ldloc, message),
            Instruction.Create(OpCodes.Callvirt, postEvent),
            Instruction.Create(OpCodes.Ret));
    }

    private static MethodReference FindGenericReference(
        ModuleDefinition module,
        GenericInstanceType declaringType,
        string name)
    {
        return module.GetMemberReferences()
            .OfType<MethodReference>()
            .First(reference =>
                reference.Name == name &&
                reference.DeclaringType.FullName ==
                declaringType.FullName);
    }

    private static bool StoresLocal(
        Instruction instruction,
        VariableDefinition variable)
    {
        if (instruction.Operand == variable)
        {
            return true;
        }

        switch (instruction.OpCode.Code)
        {
            case Code.Stloc_0:
                return variable.Index == 0;
            case Code.Stloc_1:
                return variable.Index == 1;
            case Code.Stloc_2:
                return variable.Index == 2;
            case Code.Stloc_3:
                return variable.Index == 3;
            default:
                return false;
        }
    }

    private static void InsertBeforeReturn(
        MethodDefinition method,
        params Instruction[] instructions)
    {
        var ret = method.Body.Instructions.Single(
            instruction => instruction.OpCode == OpCodes.Ret);
        var il = method.Body.GetILProcessor();
        foreach (var instruction in instructions)
        {
            il.InsertBefore(ret, instruction);
        }
    }

    private static void Emit(ILProcessor il, params Instruction[] instructions)
    {
        foreach (var instruction in instructions)
        {
            il.Append(instruction);
        }
    }
}
