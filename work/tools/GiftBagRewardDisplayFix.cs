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
                var module = assembly.MainModule;
                var selectWindow = module.GetType("SelectItemWindow");
                var display = selectWindow == null
                    ? null
                    : selectWindow.Methods.SingleOrDefault(method =>
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

                EnsureExpandedRewardLayout(module);
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

    private static void EnsureExpandedRewardLayout(ModuleDefinition module)
    {
        var type = module.GetType("NewItemWindow");
        var init = type == null
            ? null
            : type.Methods.SingleOrDefault(method =>
                method.Name == "Init" &&
                method.Parameters.Count == 1);
        if (init == null || !init.HasBody)
        {
            throw new InvalidOperationException(
                "NewItemWindow.Init was not found.");
        }

        var helper = type.Methods.SingleOrDefault(method =>
            method.Name == "EnsureGiftBagRewardLayout");
        if (helper == null)
        {
            helper = AddExpandedRewardLayoutMethod(module, type, init);
            Console.WriteLine(
                "Added dynamic gift bag reward grid expansion.");
        }

        if (init.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand == helper))
        {
            Console.WriteLine(
                "Dynamic gift bag reward grid expansion is already applied.");
            return;
        }

        var countStore = init.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Stfld &&
            instruction.Operand is FieldReference &&
            ((FieldReference)instruction.Operand).Name == "count4");
        var loadWindow = Instruction.Create(OpCodes.Ldarg_0);
        var callHelper = Instruction.Create(OpCodes.Call, helper);
        var il = init.Body.GetILProcessor();
        il.InsertAfter(countStore, loadWindow);
        il.InsertAfter(loadWindow, callHelper);
    }

    private static MethodDefinition AddExpandedRewardLayoutMethod(
        ModuleDefinition module,
        TypeDefinition type,
        MethodDefinition init)
    {
        var gridField = type.Fields.Single(field => field.Name == "grids");
        var hiddenField = type.Fields.Single(
            field => field.Name == "hiddenObjs");
        var countField = type.Fields.Single(field => field.Name == "count");
        var gridArrayType = (ArrayType)gridField.FieldType;
        var hiddenArrayType = (ArrayType)hiddenField.FieldType;
        var gridType = gridArrayType.ElementType;
        var gameObjectType = hiddenArrayType.ElementType;

        var instantiate = ExistingMethod(
            init,
            "UnityEngine.Object",
            "Instantiate",
            1);
        var getComponentTransform = ExistingMethod(
            init,
            "UnityEngine.Component",
            "get_transform",
            0);
        var getGameObjectTransform = ExistingMethod(
            init,
            "UnityEngine.GameObject",
            "get_transform",
            0);
        var getGameObject = ExistingMethod(
            init,
            "UnityEngine.Component",
            "get_gameObject",
            0);
        var setParent = ExistingMethod(
            init,
            "UnityEngine.Transform",
            "set_parent",
            1);
        var setLocalPosition = ExistingMethod(
            init,
            "UnityEngine.Transform",
            "set_localPosition",
            1);
        var setActive = ExistingMethod(
            init,
            "UnityEngine.GameObject",
            "SetActive",
            1);
        var vectorConstructor = ExistingMethod(
            init,
            "UnityEngine.Vector3",
            ".ctor",
            3);

        var helper = new MethodDefinition(
            "EnsureGiftBagRewardLayout",
            MethodAttributes.Private |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        helper.Parameters.Add(new ParameterDefinition(
            "window",
            ParameterAttributes.None,
            type));
        helper.Body.InitLocals = true;
        helper.Body.MaxStackSize = 6;

        var required = AddLocal(helper, module.TypeSystem.Int32);
        var oldLength = AddLocal(helper, module.TypeSystem.Int32);
        var index = AddLocal(helper, module.TypeSystem.Int32);
        var oldGrids = AddLocal(helper, gridArrayType);
        var oldHidden = AddLocal(helper, hiddenArrayType);
        var anchor = AddLocal(helper, gameObjectType);
        var template = AddLocal(helper, gameObjectType);
        var parent = AddLocal(
            helper,
            ExistingMethod(
                init,
                "UnityEngine.GameObject",
                "get_transform",
                0).ReturnType);
        var rows = AddLocal(helper, module.TypeSystem.Int32);
        var x = AddLocal(helper, module.TypeSystem.Single);
        var y = AddLocal(helper, module.TypeSystem.Single);

        var il = helper.Body.GetILProcessor();
        var returnInstruction = Instruction.Create(OpCodes.Ret);
        var gridsReady = Instruction.Create(OpCodes.Nop);
        var copyGrids = Instruction.Create(OpCodes.Nop);
        var copyGridsCheck = Instruction.Create(OpCodes.Nop);
        var hiddenReady = Instruction.Create(OpCodes.Nop);
        var copyHidden = Instruction.Create(OpCodes.Nop);
        var copyHiddenCheck = Instruction.Create(OpCodes.Nop);
        var createHidden = Instruction.Create(OpCodes.Nop);
        var createHiddenCheck = Instruction.Create(OpCodes.Nop);
        var layoutDone = Instruction.Create(OpCodes.Nop);
        var layoutCheck = Instruction.Create(OpCodes.Nop);
        var regularY = Instruction.Create(OpCodes.Nop);
        var storeY = Instruction.Create(OpCodes.Nop);

        Emit(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, countField),
            Instruction.Create(OpCodes.Stloc, required),
            Instruction.Create(OpCodes.Ldloc, required),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Ble, returnInstruction),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, gridField),
            Instruction.Create(OpCodes.Ldlen),
            Instruction.Create(OpCodes.Conv_I4),
            Instruction.Create(OpCodes.Stloc, oldLength),
            Instruction.Create(OpCodes.Ldloc, oldLength),
            Instruction.Create(OpCodes.Ldloc, required),
            Instruction.Create(OpCodes.Bge, gridsReady),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, gridField),
            Instruction.Create(OpCodes.Stloc, oldGrids),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldloc, required),
            Instruction.Create(OpCodes.Newarr, gridType),
            Instruction.Create(OpCodes.Stfld, gridField),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, copyGridsCheck));

        il.Append(copyGrids);
        Emit(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, gridField),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldloc, oldGrids),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldelem_Ref),
            Instruction.Create(OpCodes.Stelem_Ref),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, index));

        il.Append(copyGridsCheck);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldloc, oldLength),
            Instruction.Create(OpCodes.Blt, copyGrids));

        il.Append(gridsReady);
        Emit(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, hiddenField),
            Instruction.Create(OpCodes.Ldlen),
            Instruction.Create(OpCodes.Conv_I4),
            Instruction.Create(OpCodes.Stloc, oldLength),
            Instruction.Create(OpCodes.Ldloc, oldLength),
            Instruction.Create(OpCodes.Ldloc, required),
            Instruction.Create(OpCodes.Bge, hiddenReady),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, hiddenField),
            Instruction.Create(OpCodes.Stloc, oldHidden),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldloc, required),
            Instruction.Create(OpCodes.Newarr, gameObjectType),
            Instruction.Create(OpCodes.Stfld, hiddenField),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, copyHiddenCheck));

        il.Append(copyHidden);
        Emit(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, hiddenField),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldloc, oldHidden),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldelem_Ref),
            Instruction.Create(OpCodes.Stelem_Ref),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, index));

        il.Append(copyHiddenCheck);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldloc, oldLength),
            Instruction.Create(OpCodes.Blt, copyHidden),

            Instruction.Create(OpCodes.Ldloc, oldHidden),
            Instruction.Create(OpCodes.Ldloc, oldLength),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(OpCodes.Ldelem_Ref),
            Instruction.Create(OpCodes.Stloc, template),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Callvirt, getGameObject),
            Instruction.Create(OpCodes.Callvirt, getGameObjectTransform),
            Instruction.Create(OpCodes.Stloc, parent),
            Instruction.Create(OpCodes.Ldloc, oldLength),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, createHiddenCheck));

        il.Append(createHidden);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, template),
            Instruction.Create(OpCodes.Call, instantiate),
            Instruction.Create(OpCodes.Castclass, gameObjectType),
            Instruction.Create(OpCodes.Stloc, anchor),
            Instruction.Create(OpCodes.Ldloc, anchor),
            Instruction.Create(OpCodes.Callvirt, getGameObjectTransform),
            Instruction.Create(OpCodes.Ldloc, parent),
            Instruction.Create(OpCodes.Callvirt, setParent),
            Instruction.Create(OpCodes.Ldloc, anchor),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Callvirt, setActive),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, hiddenField),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldloc, anchor),
            Instruction.Create(OpCodes.Stelem_Ref),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, index));

        il.Append(createHiddenCheck);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldloc, required),
            Instruction.Create(OpCodes.Blt, createHidden));

        il.Append(hiddenReady);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, required),
            Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)12),
            Instruction.Create(OpCodes.Ble, layoutDone),
            Instruction.Create(OpCodes.Ldloc, required),
            Instruction.Create(OpCodes.Ldc_I4_4),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Ldc_I4_5),
            Instruction.Create(OpCodes.Div),
            Instruction.Create(OpCodes.Stloc, rows),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, layoutCheck));

        il.Append(layoutCheck);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldloc, required),
            Instruction.Create(OpCodes.Bge, layoutDone),

            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_5),
            Instruction.Create(OpCodes.Rem),
            Instruction.Create(OpCodes.Ldc_I4_2),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(OpCodes.Conv_R4),
            Instruction.Create(OpCodes.Ldc_R4, 100f),
            Instruction.Create(OpCodes.Mul),
            Instruction.Create(OpCodes.Stloc, x),

            Instruction.Create(OpCodes.Ldloc, rows),
            Instruction.Create(OpCodes.Ldc_I4_4),
            Instruction.Create(OpCodes.Bgt, regularY),
            Instruction.Create(OpCodes.Ldc_R4, 135f),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_5),
            Instruction.Create(OpCodes.Div),
            Instruction.Create(OpCodes.Conv_R4),
            Instruction.Create(OpCodes.Ldc_R4, 100f),
            Instruction.Create(OpCodes.Mul),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(OpCodes.Br, storeY));

        il.Append(regularY);
        Emit(il,
            Instruction.Create(OpCodes.Ldc_R4, 150f),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_5),
            Instruction.Create(OpCodes.Div),
            Instruction.Create(OpCodes.Conv_R4),
            Instruction.Create(OpCodes.Ldc_R4, 90f),
            Instruction.Create(OpCodes.Mul),
            Instruction.Create(OpCodes.Sub));

        il.Append(storeY);
        Emit(il,
            Instruction.Create(OpCodes.Stloc, y),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, hiddenField),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldelem_Ref),
            Instruction.Create(OpCodes.Callvirt, getGameObjectTransform),
            Instruction.Create(OpCodes.Ldloc, x),
            Instruction.Create(OpCodes.Ldloc, y),
            Instruction.Create(OpCodes.Ldc_R4, 0f),
            Instruction.Create(OpCodes.Newobj, vectorConstructor),
            Instruction.Create(OpCodes.Callvirt, setLocalPosition),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, layoutCheck));

        il.Append(layoutDone);
        il.Append(returnInstruction);
        type.Methods.Add(helper);
        return helper;
    }

    private static VariableDefinition AddLocal(
        MethodDefinition method,
        TypeReference type)
    {
        var variable = new VariableDefinition(type);
        method.Body.Variables.Add(variable);
        return variable;
    }

    private static MethodReference ExistingMethod(
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
