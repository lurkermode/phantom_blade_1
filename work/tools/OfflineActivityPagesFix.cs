using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class OfflineActivityPagesFix
{
    private const int RewardStatusSlots = 1024;
    private const int OvertimeStatus = 3;

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: OfflineActivityPagesFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var controller = module.GetType("ActivityController");
                var activityWindow = module.GetType("ActivityWindow");
                var topMenu = module.GetType("TopMenu");
                var activityInfo = module.GetType("ActivityInfo");
                var tempDataManager = module.GetType("TempDataManager");
                if (controller == null ||
                    activityWindow == null ||
                    topMenu == null ||
                    activityInfo == null ||
                    tempDataManager == null)
                {
                    throw new InvalidOperationException(
                        "Activity catalog types were not found.");
                }

                var updateList = controller.Methods.Single(
                    method => method.Name == "UpdateActivityList" &&
                              method.Parameters.Count == 1);
                var getControllerList = controller.Methods.Single(
                    method => method.Name == "get_activityInfoList");
                var updateWindow = activityWindow.Methods.Single(
                    method => method.Name == "UpdateActivityInfoList");
                var windowStart = activityWindow.Methods.Single(
                    method => method.Name == "Start");
                var buttonActivity = topMenu.Methods.Single(
                    method => method.Name == "OnButtonActivity");
                var getActivityList = topMenu.Methods.Single(
                    method => method.Name == "OnGetActivityList");
                var statusGetter = activityInfo.Methods.Single(
                    method => method.Name == "get_finishStatus");
                var whichDaySetter = activityInfo.Methods.Single(
                    method => method.Name == "set_whichDay");

                var infoListType = getControllerList.ReturnType;
                var intListType = statusGetter.ReturnType;
                var infoType = ((GenericInstanceType)infoListType)
                    .GenericArguments[0];

                var prepare = controller.Methods.SingleOrDefault(
                    method => method.Name == "PrepareOfflinePages");
                if (prepare == null)
                {
                    prepare = AddPrepareMethod(
                        module,
                        controller,
                        updateList,
                        getControllerList,
                        updateWindow,
                        statusGetter,
                        whichDaySetter,
                        tempDataManager,
                        (GenericInstanceType)infoListType,
                        (GenericInstanceType)intListType,
                        infoType);
                }

                UnfilterStaticActivities(
                    module,
                    updateList,
                    controller.Fields.Single(field =>
                        field.Name == "<activityInfoList>k__BackingField"));

                EnsureCallBefore(
                    windowStart,
                    prepare,
                    "ActivityWindow.Start");
                ReplaceButtonEntry(buttonActivity, prepare, getActivityList);

                assembly.Write(args[1]);
            }

            Console.WriteLine(
                "Prepared 25 historical activity pages for offline viewing.");
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

    private static MethodDefinition AddPrepareMethod(
        ModuleDefinition module,
        TypeDefinition controller,
        MethodDefinition updateList,
        MethodDefinition getControllerList,
        MethodDefinition updateWindow,
        MethodDefinition statusGetter,
        MethodDefinition whichDaySetter,
        TypeDefinition tempDataManager,
        GenericInstanceType infoListType,
        GenericInstanceType intListType,
        TypeReference infoType)
    {
        var method = new MethodDefinition(
            "PrepareOfflinePages",
            MethodAttributes.Public |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        method.Body.InitLocals = true;
        method.Body.MaxStackSize = 4;

        var controllerLocal = new VariableDefinition(controller);
        var infos = new VariableDefinition(infoListType);
        var info = new VariableDefinition(infoType);
        var statuses = new VariableDefinition(intListType);
        var outerIndex = new VariableDefinition(module.TypeSystem.Int32);
        var statusIndex = new VariableDefinition(module.TypeSystem.Int32);
        method.Body.Variables.Add(controllerLocal);
        method.Body.Variables.Add(infos);
        method.Body.Variables.Add(info);
        method.Body.Variables.Add(statuses);
        method.Body.Variables.Add(outerIndex);
        method.Body.Variables.Add(statusIndex);
        controller.Methods.Add(method);

        var existingWindowMethods = updateWindow.Body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Where(reference =>
                reference.DeclaringType.FullName == infoListType.FullName)
            .ToList();
        var infoCount = existingWindowMethods.First(
            reference => reference.Name == "get_Count");
        var infoItem = existingWindowMethods.First(
            reference => reference.Name == "get_Item");
        var existingControllerMethods = updateWindow.Body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .ToList();
        var getTempDataManager = existingControllerMethods.First(
            reference =>
                reference.Name == "get_Instance" &&
                reference.DeclaringType == tempDataManager);
        var getActivityController = existingControllerMethods.First(
            reference =>
                reference.Name == "get_theActivityController" &&
                reference.DeclaringType == tempDataManager);

        var newInfoList = new MethodReference(
            ".ctor",
            module.TypeSystem.Void,
            infoListType)
        {
            HasThis = true
        };
        var clearStatuses = ListMethod(
            module,
            intListType,
            "Clear",
            module.TypeSystem.Void);
        // The game ships a very old Mono runtime. A manually constructed
        // List<int>.Add(int) MemberRef is not accepted by that runtime, even
        // though desktop PE verification accepts it. Reuse the compiler's
        // existing generic method reference instead.
        var addStatus = module.GetMemberReferences()
            .OfType<MethodReference>()
            .First(
            delegate(MethodReference reference)
            {
                if (reference.Name != "Add" ||
                    reference.DeclaringType.FullName != intListType.FullName ||
                    reference.Parameters.Count != 1)
                {
                    return false;
                }
                var parameter =
                    reference.Parameters[0].ParameterType as GenericParameter;
                return parameter != null &&
                       parameter.Type == GenericParameterType.Type &&
                       parameter.Position == 0;
            });

        var il = method.Body.GetILProcessor();
        var outerCheck = Instruction.Create(OpCodes.Nop);
        var outerBody = Instruction.Create(OpCodes.Nop);
        var statusCheck = Instruction.Create(OpCodes.Nop);
        var statusBody = Instruction.Create(OpCodes.Nop);

        Emit(il,
            Instruction.Create(OpCodes.Call, getTempDataManager),
            Instruction.Create(OpCodes.Callvirt, getActivityController),
            Instruction.Create(OpCodes.Stloc, controllerLocal),

            Instruction.Create(OpCodes.Ldloc, controllerLocal),
            Instruction.Create(OpCodes.Newobj, newInfoList),
            Instruction.Create(OpCodes.Callvirt, updateList),

            Instruction.Create(OpCodes.Ldloc, controllerLocal),
            Instruction.Create(OpCodes.Callvirt, getControllerList),
            Instruction.Create(OpCodes.Stloc, infos),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, outerIndex),
            Instruction.Create(OpCodes.Br, outerCheck));

        il.Append(outerBody);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, infos),
            Instruction.Create(OpCodes.Ldloc, outerIndex),
            Instruction.Create(OpCodes.Callvirt, infoItem),
            Instruction.Create(OpCodes.Stloc, info),
            Instruction.Create(OpCodes.Ldloc, info),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Callvirt, whichDaySetter),
            Instruction.Create(OpCodes.Ldloc, info),
            Instruction.Create(OpCodes.Callvirt, statusGetter),
            Instruction.Create(OpCodes.Stloc, statuses),
            Instruction.Create(OpCodes.Ldloc, statuses),
            Instruction.Create(OpCodes.Callvirt, clearStatuses),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, statusIndex),
            Instruction.Create(OpCodes.Br, statusCheck));

        il.Append(statusBody);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, statuses),
            Instruction.Create(OpCodes.Ldc_I4_3),
            Instruction.Create(OpCodes.Callvirt, addStatus),
            Instruction.Create(OpCodes.Ldloc, statusIndex),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, statusIndex));

        il.Append(statusCheck);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, statusIndex),
            Instruction.Create(OpCodes.Ldc_I4, RewardStatusSlots),
            Instruction.Create(OpCodes.Blt, statusBody),
            Instruction.Create(OpCodes.Ldloc, outerIndex),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, outerIndex));

        il.Append(outerCheck);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, outerIndex),
            Instruction.Create(OpCodes.Ldloc, infos),
            Instruction.Create(OpCodes.Callvirt, infoCount),
            Instruction.Create(OpCodes.Blt, outerBody),
            Instruction.Create(OpCodes.Ret));

        return method;
    }

    private static void UnfilterStaticActivities(
        ModuleDefinition module,
        MethodDefinition updateList,
        FieldDefinition activityListField)
    {
        var body = updateList.Body;
        var contentTypeCheck = body.Instructions.Single(instruction =>
            (instruction.OpCode == OpCodes.Brtrue ||
             instruction.OpCode == OpCodes.Brtrue_S) &&
            instruction.Previous != null &&
            instruction.Previous.OpCode == OpCodes.Ldfld &&
            instruction.Previous.Operand is FieldReference &&
            ((FieldReference)instruction.Previous.Operand).Name ==
            "contentType");
        ReplaceWithPop(contentTypeCheck);

        var dateChecks = body.Instructions
            .Where(instruction =>
                (instruction.OpCode == OpCodes.Brfalse ||
                 instruction.OpCode == OpCodes.Brfalse_S) &&
                instruction.Previous != null &&
                instruction.Previous.OpCode == OpCodes.Call &&
                instruction.Previous.Operand is MethodReference &&
                ((MethodReference)instruction.Previous.Operand).Name ==
                "op_LessThanOrEqual")
            .ToList();
        if (dateChecks.Count != 2)
        {
            throw new InvalidOperationException(
                "Unexpected activity time filters.");
        }
        foreach (var check in dateChecks)
        {
            ReplaceWithPop(check);
        }
    }

    private static void ReplaceWithPop(Instruction instruction)
    {
        instruction.OpCode = OpCodes.Pop;
        instruction.Operand = null;
    }

    private static void EnsureCallBefore(
        MethodDefinition method,
        MethodDefinition prepare,
        string description)
    {
        if (method.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand == prepare))
        {
            return;
        }
        method.Body.GetILProcessor().InsertBefore(
            method.Body.Instructions[0],
            Instruction.Create(OpCodes.Call, prepare));
        Console.WriteLine("Added offline preparation to " + description + ".");
    }

    private static void ReplaceButtonEntry(
        MethodDefinition method,
        MethodDefinition prepare,
        MethodDefinition getActivityList)
    {
        if (method.Body.Instructions.Count > 0 &&
            method.Body.Instructions[0].OpCode == OpCodes.Call &&
            method.Body.Instructions[0].Operand == prepare)
        {
            Console.WriteLine("Activity button already uses local catalog.");
            return;
        }

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.InitLocals = false;
        method.Body.MaxStackSize = 4;
        var il = method.Body.GetILProcessor();
        Emit(il,
            Instruction.Create(OpCodes.Call, prepare),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldc_I4, 0x816),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Newobj,
                method.Module.ImportReference(
                    method.Module.GetType("MessageEventArgs")
                        .Methods.Single(ctor =>
                            ctor.IsConstructor &&
                            ctor.Parameters.Count == 0))),
            Instruction.Create(OpCodes.Call, getActivityList),
            Instruction.Create(OpCodes.Ret));
        Console.WriteLine("Activity button now opens the local catalog.");
    }

    private static MethodReference ListMethod(
        ModuleDefinition module,
        GenericInstanceType listType,
        string name,
        TypeReference returnType,
        params TypeReference[] parameterTypes)
    {
        var method = new MethodReference(name, returnType, listType)
        {
            HasThis = true
        };
        foreach (var parameterType in parameterTypes)
        {
            method.Parameters.Add(new ParameterDefinition(parameterType));
        }
        return module.ImportReference(method);
    }

    private static void Emit(ILProcessor il, params Instruction[] instructions)
    {
        foreach (var instruction in instructions)
        {
            il.Append(instruction);
        }
    }
}
