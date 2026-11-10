using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class DuelFactorOneFix
{
    private const int FirstBot = unchecked((int)0xEE6B279C);
    private const string ScrollViewportName = "DuelBotScrollViewport";

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: DuelFactorOneFix <input> <output>");
            return 2;
        }

        try
        {
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var module = assembly.MainModule;
                var lobby = module.GetType("YzrLocal.LocalDuelLobby");
                if (lobby == null)
                {
                    throw new InvalidOperationException("YzrLocal.LocalDuelLobby was not found.");
                }

                var factors = lobby.Fields.SingleOrDefault(field =>
                    field.Name == "DuelFactors");
                var cctor = lobby.Methods.SingleOrDefault(method =>
                    method.IsConstructor &&
                    method.IsStatic &&
                    method.Name == ".cctor");
                if (factors == null || cctor == null)
                {
                    throw new InvalidOperationException(
                        "LocalDuelLobby.DuelFactors or its static constructor was not found.");
                }

                var body = cctor.Body;
                var instructions = body.Instructions;
                var il = body.GetILProcessor();
                var storeFactors = instructions.SingleOrDefault(instruction =>
                    instruction.OpCode == OpCodes.Stsfld &&
                    instruction.Operand == factors);
                if (storeFactors == null)
                {
                    throw new InvalidOperationException(
                        "Unable to locate the DuelFactors initialization.");
                }

                var alreadyPatched = instructions.Any(instruction =>
                    instruction.OpCode == OpCodes.Stelem_I4);
                if (!alreadyPatched)
                {
                    var newArray = instructions.FirstOrDefault(instruction =>
                        instruction.OpCode == OpCodes.Newarr &&
                        instruction.Previous != null &&
                        instruction.Previous.OpCode == OpCodes.Ldc_I4_4);
                    if (newArray == null)
                    {
                        throw new InvalidOperationException(
                            "Unexpected DuelFactors array layout.");
                    }

                    var initializeArray = instructions.FirstOrDefault(instruction =>
                        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                        instruction.Operand is MethodReference &&
                        ((MethodReference)instruction.Operand).Name == "InitializeArray");
                    if (initializeArray == null ||
                        initializeArray.Previous == null ||
                        initializeArray.Previous.OpCode != OpCodes.Ldtoken ||
                        initializeArray.Previous.Previous == null ||
                        initializeArray.Previous.Previous.OpCode != OpCodes.Dup)
                    {
                        throw new InvalidOperationException(
                            "Unexpected DuelFactors initializer layout.");
                    }

                    newArray.Previous.OpCode = OpCodes.Ldc_I4_5;
                    instructions.Remove(initializeArray.Previous.Previous);
                    instructions.Remove(initializeArray.Previous);
                    instructions.Remove(initializeArray);

                    var directInitializer = new List<Instruction>();
                    var values = new[] { 2, 4, 6, 8, 1 };
                    for (var index = 0; index < values.Length; index++)
                    {
                        directInitializer.Add(Instruction.Create(OpCodes.Dup));
                        directInitializer.Add(Instruction.Create(OpCodes.Ldc_I4, index));
                        directInitializer.Add(Instruction.Create(OpCodes.Ldc_I4, values[index]));
                        directInitializer.Add(Instruction.Create(OpCodes.Stelem_I4));
                    }
                    foreach (var instruction in directInitializer)
                    {
                        il.InsertBefore(storeFactors, instruction);
                    }

                    body.MaxStackSize = Math.Max(body.MaxStackSize, 3);
                    Console.WriteLine("Added the 1x duel bot factor without shifting existing factors.");
                }
                else
                {
                    Console.WriteLine("1x duel bot factor is already present.");
                }

                AddDisplayOrderAndScroll(module, lobby);

                var menu = module.GetType("YzrLocal.LocalDuelMenu");
                var onGui = menu == null
                    ? null
                    : menu.Methods.SingleOrDefault(method => method.Name == "OnGUI");
                if (onGui != null)
                {
                    var description = onGui.Body.Instructions.FirstOrDefault(instruction =>
                        instruction.OpCode == OpCodes.Ldstr &&
                        instruction.Operand is string &&
                        ((string)instruction.Operand).Contains("论剑列表提供"));
                    if (description != null &&
                        !((string)description.Operand).Contains("6倍"))
                    {
                        description.Operand =
                            "论剑列表提供 1倍、2倍、4倍、6倍、8倍战力挑战。";
                    }
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

    private static void AddDisplayOrderAndScroll(
        ModuleDefinition module,
        TypeDefinition lobby)
    {
        var window = module.GetType("PVPWindow_MatchList");
        var friendData = module.GetType("FriendData");
        var uiPanel = module.GetType("UIPanel");
        var uiWidget = module.GetType("UIWidget");
        var draggablePanel = module.GetType("UIDraggablePanel");
        var dragPanelContents = module.GetType("UIDragPanelContents");
        if (window == null || friendData == null || uiPanel == null ||
            uiWidget == null ||
            draggablePanel == null || dragPanelContents == null)
        {
            throw new InvalidOperationException(
                "Unable to resolve the duel lobby UI types.");
        }

        var refresh = lobby.Methods.SingleOrDefault(
            method => method.Name == "Refresh" &&
                      method.Parameters.Count == 1 &&
                      method.Parameters[0].ParameterType == window);
        var soloObjField = window.Fields.Single(field => field.Name == "soloObj");
        var cardsField = window.Fields.Single(field => field.Name == "cards");
        var displayClass = refresh.Body.Variables
            .Select(variable => variable.VariableType.Resolve())
            .First(type => type != null && type.Name == "<>c__DisplayClass6_0");
        var botsField = displayClass.Fields.Single(field => field.Name == "bots");
        var botsType = (GenericInstanceType)botsField.FieldType;
        var existingMethods = refresh.Body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .ToList();
        var botGetItem = existingMethods.First(method =>
            method.Name == "get_Item" &&
            method.Parameters.Count == 1 &&
            method.DeclaringType.FullName.StartsWith(
                "System.Collections.Generic.List`1<FriendData>"));
        var cardGetItem = RetargetListMethod(
            module,
            botGetItem,
            cardsField.FieldType);

        var orderBots = EnsureOrderBotsMethod(
            module,
            lobby,
            friendData,
            botsType,
            botGetItem);
        var ensureScroll = EnsureScrollMethod(
            module,
            lobby,
            window,
            friendData,
            uiPanel,
            uiWidget,
            draggablePanel,
            dragPanelContents,
            soloObjField,
            cardsField,
            cardGetItem);

        EnsureDisplayOrderCall(refresh, orderBots, botsField);
        EnsureScrollCall(refresh, ensureScroll, cardsField.DeclaringType);
    }

    private static MethodDefinition EnsureOrderBotsMethod(
        ModuleDefinition module,
        TypeDefinition lobby,
        TypeDefinition friendData,
        GenericInstanceType botsType,
        MethodReference getItem)
    {
        var existing = lobby.Methods.SingleOrDefault(
            method => method.Name == "OrderDuelBotsForDisplay");
        if (existing != null)
        {
            return existing;
        }

        var helper = new MethodDefinition(
            "OrderDuelBotsForDisplay",
            MethodAttributes.Private |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        helper.Parameters.Add(new ParameterDefinition(
            "bots",
            ParameterAttributes.None,
            botsType));
        helper.Body.InitLocals = true;
        helper.Body.MaxStackSize = 2;
        var oneBot = new VariableDefinition(friendData);
        helper.Body.Variables.Add(oneBot);
        lobby.Methods.Add(helper);

        var il = helper.Body.GetILProcessor();
        var done = Instruction.Create(OpCodes.Ret);
        var getCount = ListMethod(module, botsType, "get_Count", module.TypeSystem.Int32);
        var removeAt = ListMethod(module, botsType, "RemoveAt", module.TypeSystem.Void, module.TypeSystem.Int32);
        var listInsertTemplate = module.ImportReference(
            typeof(List<>).GetMethods().Single(candidate =>
                candidate.Name == "Insert" &&
                candidate.GetParameters().Length == 2));
        var insert = RetargetListMethod(
            module,
            listInsertTemplate,
            botsType);
        var roleId = friendData.Fields.Single(field => field.Name == "roleID");

        Emit(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Brfalse, done),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Callvirt, getCount),
            Instruction.Create(OpCodes.Ldc_I4_5),
            Instruction.Create(OpCodes.Bne_Un, done),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Callvirt, getItem),
            Instruction.Create(OpCodes.Ldfld, roleId),
            Instruction.Create(OpCodes.Ldc_I4, FirstBot),
            Instruction.Create(OpCodes.Bne_Un, done),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldc_I4_4),
            Instruction.Create(OpCodes.Callvirt, getItem),
            Instruction.Create(OpCodes.Ldfld, roleId),
            Instruction.Create(OpCodes.Ldc_I4, unchecked(FirstBot + 4)),
            Instruction.Create(OpCodes.Bne_Un, done),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldc_I4_4),
            Instruction.Create(OpCodes.Callvirt, getItem),
            Instruction.Create(OpCodes.Stloc, oneBot),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldc_I4_4),
            Instruction.Create(OpCodes.Callvirt, removeAt),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Ldloc, oneBot),
            Instruction.Create(OpCodes.Callvirt, insert));
        il.Append(done);
        return helper;
    }

    private static MethodDefinition EnsureScrollMethod(
        ModuleDefinition module,
        TypeDefinition lobby,
        TypeDefinition window,
        TypeDefinition friendData,
        TypeDefinition uiPanel,
        TypeDefinition uiWidget,
        TypeDefinition draggablePanel,
        TypeDefinition dragPanelContents,
        FieldDefinition soloObjField,
        FieldDefinition cardsField,
        MethodReference getCard)
    {
        var existing = lobby.Methods.SingleOrDefault(
            method => method.Name == "EnsureDuelBotScroll");
        if (existing != null)
        {
            return existing;
        }

        var unityGameObject = module.ImportReference(typeof(UnityEngine.GameObject));
        var unityTransform = module.ImportReference(typeof(UnityEngine.Transform));
        var unityBoxCollider = module.ImportReference(typeof(UnityEngine.BoxCollider));
        var unityComponentArray = new ArrayType(
            module.ImportReference(typeof(UnityEngine.Component)));
        var unityVector2 = module.ImportReference(typeof(UnityEngine.Vector2));
        var unityVector3 = module.ImportReference(typeof(UnityEngine.Vector3));
        var unityVector4 = module.ImportReference(typeof(UnityEngine.Vector4));

        var helper = new MethodDefinition(
            "EnsureDuelBotScroll",
            MethodAttributes.Private |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        helper.Parameters.Add(new ParameterDefinition(
            "window",
            ParameterAttributes.None,
            window));
        helper.Body.InitLocals = true;
        helper.Body.MaxStackSize = 8;

        var soloTransform = new VariableDefinition(unityTransform);
        var oldViewport = new VariableDefinition(unityTransform);
        var viewportObject = new VariableDefinition(unityGameObject);
        var viewportTransform = new VariableDefinition(unityTransform);
        var panel = new VariableDefinition(uiPanel);
        var drag = new VariableDefinition(draggablePanel);
        var index = new VariableDefinition(module.TypeSystem.Int32);
        var card = new VariableDefinition(unityGameObject);
        var cardTransform = new VariableDefinition(unityTransform);
        var boxCollider = new VariableDefinition(unityBoxCollider);
        var contents = new VariableDefinition(dragPanelContents);
        var widgets = new VariableDefinition(unityComponentArray);
        var widgetIndex = new VariableDefinition(module.TypeSystem.Int32);
        var widget = new VariableDefinition(uiWidget);
        helper.Body.Variables.Add(soloTransform);
        helper.Body.Variables.Add(oldViewport);
        helper.Body.Variables.Add(viewportObject);
        helper.Body.Variables.Add(viewportTransform);
        helper.Body.Variables.Add(panel);
        helper.Body.Variables.Add(drag);
        helper.Body.Variables.Add(index);
        helper.Body.Variables.Add(card);
        helper.Body.Variables.Add(cardTransform);
        helper.Body.Variables.Add(boxCollider);
        helper.Body.Variables.Add(contents);
        helper.Body.Variables.Add(widgets);
        helper.Body.Variables.Add(widgetIndex);
        helper.Body.Variables.Add(widget);
        lobby.Methods.Add(helper);

        var il = helper.Body.GetILProcessor();
        var done = Instruction.Create(OpCodes.Ret);
        var noOldViewport = Instruction.Create(OpCodes.Nop);
        var havePanel = Instruction.Create(OpCodes.Nop);
        var haveDrag = Instruction.Create(OpCodes.Nop);
        var nextCard = Instruction.Create(OpCodes.Nop);
        var addBoxCollider = Instruction.Create(OpCodes.Nop);
        var haveContents = Instruction.Create(OpCodes.Nop);
        var loopCheck = Instruction.Create(OpCodes.Nop);
        var widgetsCheck = Instruction.Create(OpCodes.Nop);
        var widgetsNext = Instruction.Create(OpCodes.Nop);
        var widgetsDone = Instruction.Create(OpCodes.Nop);
        var finish = Instruction.Create(OpCodes.Nop);

        var getTransform = ImportPropertyGetter(
            module,
            typeof(UnityEngine.GameObject),
            "transform");
        var getLayer = ImportPropertyGetter(
            module,
            typeof(UnityEngine.GameObject),
            "layer");
        var setLayer = ImportPropertySetter(
            module,
            typeof(UnityEngine.GameObject),
            "layer");
        var getGameObject = ImportPropertyGetter(
            module,
            typeof(UnityEngine.Component),
            "gameObject");
        var setLocalPosition = ImportPropertySetter(
            module,
            typeof(UnityEngine.Transform),
            "localPosition");
        var setLocalScale = ImportPropertySetter(
            module,
            typeof(UnityEngine.Transform),
            "localScale");
        var setParent = ImportPropertySetter(
            module,
            typeof(UnityEngine.Transform),
            "parent");
        var find = module.ImportReference(
            typeof(UnityEngine.Transform).GetMethod(
                "Find",
                new[] { typeof(string) }));
        var destroy = module.ImportReference(
            typeof(UnityEngine.Object).GetMethod(
                "Destroy",
                new[] { typeof(UnityEngine.Object) }));
        var setActive = module.ImportReference(
            typeof(UnityEngine.GameObject).GetMethod(
                "SetActive",
                new[] { typeof(bool) }));
        var vectorZero = module.ImportReference(
            typeof(UnityEngine.Vector3).GetProperty("zero").GetGetMethod());
        var vectorOne = module.ImportReference(
            typeof(UnityEngine.Vector3).GetProperty("one").GetGetMethod());
        var newGameObject = module.ImportReference(
            typeof(UnityEngine.GameObject).GetConstructor(
                new[] { typeof(string) }));
        var getComponent = module.ImportReference(
            typeof(UnityEngine.GameObject).GetMethod(
                "GetComponent",
                new[] { typeof(Type) }));
        var addComponent = module.ImportReference(
            typeof(UnityEngine.GameObject).GetMethod(
                "AddComponent",
                new[] { typeof(Type) }));
        var getComponentsInChildren = module.ImportReference(
            typeof(UnityEngine.GameObject).GetMethod(
                "GetComponentsInChildren",
                new[] { typeof(Type), typeof(bool) }));
        var getTypeFromHandle = module.ImportReference(
            typeof(Type).GetMethod(
                "GetTypeFromHandle",
                new[] { typeof(RuntimeTypeHandle) }));
        var newVector2 = module.ImportReference(
            typeof(UnityEngine.Vector2).GetConstructor(
                new[] { typeof(float), typeof(float) }));
        var newVector3 = module.ImportReference(
            typeof(UnityEngine.Vector3).GetConstructor(
                new[] { typeof(float), typeof(float), typeof(float) }));
        var newVector4 = module.ImportReference(
            typeof(UnityEngine.Vector4).GetConstructor(
                new[] { typeof(float), typeof(float), typeof(float), typeof(float) }));
        var setEnabled = module.ImportReference(
            typeof(UnityEngine.Behaviour).GetProperty("enabled").GetSetMethod());
        var setBoxCenter = module.ImportReference(
            typeof(UnityEngine.BoxCollider).GetProperty("center").GetSetMethod());
        var setBoxSize = module.ImportReference(
            typeof(UnityEngine.BoxCollider).GetProperty("size").GetSetMethod());
        var setClipping = uiPanel.Methods.Single(
            candidate => candidate.Name == "set_clipping");
        var setClipSoftness = uiPanel.Methods.Single(
            candidate => candidate.Name == "set_clipSoftness");
        var setClipRange = uiPanel.Methods.Single(
            candidate => candidate.Name == "set_clipRange");
        var refreshPanel = uiPanel.Methods.Single(
            candidate => candidate.Name == "Refresh" &&
                         candidate.Parameters.Count == 0);
        var updateScrollbars = draggablePanel.Methods.Single(
            candidate => candidate.Name == "UpdateScrollbars" &&
                         candidate.Parameters.Count == 1);
        var parentHasChanged = uiWidget.Methods.Single(
            candidate => candidate.Name == "ParentHasChanged");
        var getCardsCount = ListMethod(
            module,
            (GenericInstanceType)cardsField.FieldType,
            "get_Count",
            module.TypeSystem.Int32);
        Emit(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Brfalse, done),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, soloObjField),
            Instruction.Create(OpCodes.Brfalse, done),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, cardsField),
            Instruction.Create(OpCodes.Brfalse, done),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, cardsField),
            Instruction.Create(OpCodes.Callvirt, getCardsCount),
            Instruction.Create(OpCodes.Brfalse, done),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, soloObjField),
            Instruction.Create(OpCodes.Callvirt, getTransform),
            Instruction.Create(OpCodes.Stloc, soloTransform),

            Instruction.Create(OpCodes.Ldloc, soloTransform),
            Instruction.Create(OpCodes.Ldstr, ScrollViewportName),
            Instruction.Create(OpCodes.Callvirt, find),
            Instruction.Create(OpCodes.Stloc, oldViewport),
            Instruction.Create(OpCodes.Ldloc, oldViewport),
            Instruction.Create(OpCodes.Brfalse, noOldViewport),
            Instruction.Create(OpCodes.Ldloc, oldViewport),
            Instruction.Create(OpCodes.Callvirt, getGameObject),
            Instruction.Create(OpCodes.Stloc, viewportObject),
            Instruction.Create(OpCodes.Ldloc, viewportObject),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Callvirt, setActive),
            Instruction.Create(OpCodes.Ldloc, viewportObject),
            Instruction.Create(OpCodes.Call, destroy));

        il.Append(noOldViewport);
        Emit(il,
            Instruction.Create(OpCodes.Ldstr, ScrollViewportName),
            Instruction.Create(OpCodes.Newobj, newGameObject),
            Instruction.Create(OpCodes.Stloc, viewportObject),
            Instruction.Create(OpCodes.Ldloc, viewportObject),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, soloObjField),
            Instruction.Create(OpCodes.Callvirt, getLayer),
            Instruction.Create(OpCodes.Callvirt, setLayer),
            Instruction.Create(OpCodes.Ldloc, viewportObject),
            Instruction.Create(OpCodes.Callvirt, getTransform),
            Instruction.Create(OpCodes.Stloc, viewportTransform),
            Instruction.Create(OpCodes.Ldloc, viewportTransform),
            Instruction.Create(OpCodes.Ldloc, soloTransform),
            Instruction.Create(OpCodes.Callvirt, setParent),
            Instruction.Create(OpCodes.Ldloc, viewportTransform),
            Instruction.Create(OpCodes.Call, vectorZero),
            Instruction.Create(OpCodes.Callvirt, setLocalPosition),
            Instruction.Create(OpCodes.Ldloc, viewportTransform),
            Instruction.Create(OpCodes.Call, vectorOne),
            Instruction.Create(OpCodes.Callvirt, setLocalScale),

            Instruction.Create(OpCodes.Ldloc, viewportObject),
            Instruction.Create(OpCodes.Ldtoken, uiPanel),
            Instruction.Create(OpCodes.Call, getTypeFromHandle),
            Instruction.Create(OpCodes.Callvirt, getComponent),
            Instruction.Create(OpCodes.Castclass, uiPanel),
            Instruction.Create(OpCodes.Stloc, panel),
            Instruction.Create(OpCodes.Ldloc, panel),
            Instruction.Create(OpCodes.Brtrue, havePanel),
            Instruction.Create(OpCodes.Ldloc, viewportObject),
            Instruction.Create(OpCodes.Ldtoken, uiPanel),
            Instruction.Create(OpCodes.Call, getTypeFromHandle),
            Instruction.Create(OpCodes.Callvirt, addComponent),
            Instruction.Create(OpCodes.Castclass, uiPanel),
            Instruction.Create(OpCodes.Stloc, panel));

        il.Append(havePanel);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, panel),
            Instruction.Create(OpCodes.Ldc_I4_3),
            Instruction.Create(OpCodes.Callvirt, setClipping),
            Instruction.Create(OpCodes.Ldloc, panel),
            Instruction.Create(OpCodes.Ldc_R4, 6f),
            Instruction.Create(OpCodes.Ldc_R4, 6f),
            Instruction.Create(OpCodes.Newobj, newVector2),
            Instruction.Create(OpCodes.Callvirt, setClipSoftness),
            Instruction.Create(OpCodes.Ldloc, panel),
            Instruction.Create(OpCodes.Ldc_R4, 0f),
            Instruction.Create(OpCodes.Ldc_R4, -155f),
            Instruction.Create(OpCodes.Ldc_R4, 1700f),
            Instruction.Create(OpCodes.Ldc_R4, 450f),
            Instruction.Create(OpCodes.Newobj, newVector4),
            Instruction.Create(OpCodes.Callvirt, setClipRange),

            Instruction.Create(OpCodes.Ldloc, viewportObject),
            Instruction.Create(OpCodes.Ldtoken, draggablePanel),
            Instruction.Create(OpCodes.Call, getTypeFromHandle),
            Instruction.Create(OpCodes.Callvirt, getComponent),
            Instruction.Create(OpCodes.Castclass, draggablePanel),
            Instruction.Create(OpCodes.Stloc, drag),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Brtrue, haveDrag),
            Instruction.Create(OpCodes.Ldloc, viewportObject),
            Instruction.Create(OpCodes.Ldtoken, draggablePanel),
            Instruction.Create(OpCodes.Call, getTypeFromHandle),
            Instruction.Create(OpCodes.Callvirt, addComponent),
            Instruction.Create(OpCodes.Castclass, draggablePanel),
            Instruction.Create(OpCodes.Stloc, drag));

        il.Append(haveDrag);
        var dragFields = draggablePanel.Fields.ToDictionary(field => field.Name);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Stfld, dragFields["restrictWithinPanel"]),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Ldc_I4_2),
            Instruction.Create(OpCodes.Stfld, dragFields["dragEffect"]),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stfld, dragFields["disableDragIfFits"]),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Stfld, dragFields["smoothDragStart"]),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Ldc_R4, 0f),
            Instruction.Create(OpCodes.Ldc_R4, 1f),
            Instruction.Create(OpCodes.Ldc_R4, 0f),
            Instruction.Create(OpCodes.Newobj, newVector3),
            Instruction.Create(OpCodes.Stfld, dragFields["scale"]),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Stfld, dragFields["iOSDragEmulation"]),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Ldc_R4, 0.25f),
            Instruction.Create(OpCodes.Stfld, dragFields["scrollWheelFactor"]),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stfld, dragFields["repositionClipping"]),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Callvirt, setEnabled),

            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, loopCheck));

        il.Append(loopCheck);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, cardsField),
            Instruction.Create(OpCodes.Callvirt, getCardsCount),
            Instruction.Create(OpCodes.Bge, finish));

        var loopBody = Instruction.Create(OpCodes.Nop);
        il.Append(loopBody);
        Emit(il,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, cardsField),
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Callvirt, getCard),
            Instruction.Create(OpCodes.Stloc, card),
            Instruction.Create(OpCodes.Ldloc, card),
            Instruction.Create(OpCodes.Brfalse, nextCard),
            Instruction.Create(OpCodes.Ldloc, card),
            Instruction.Create(OpCodes.Callvirt, getTransform),
            Instruction.Create(OpCodes.Stloc, cardTransform),
            Instruction.Create(OpCodes.Ldloc, cardTransform),
            Instruction.Create(OpCodes.Ldloc, viewportTransform),
            Instruction.Create(OpCodes.Callvirt, setParent),
            Instruction.Create(OpCodes.Ldloc, cardTransform),
            Instruction.Create(OpCodes.Call, vectorOne),
            Instruction.Create(OpCodes.Callvirt, setLocalScale),

            Instruction.Create(OpCodes.Ldloc, card),
            Instruction.Create(OpCodes.Ldtoken, uiWidget),
            Instruction.Create(OpCodes.Call, getTypeFromHandle),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Callvirt, getComponentsInChildren),
            Instruction.Create(OpCodes.Stloc, widgets),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, widgetIndex),
            Instruction.Create(OpCodes.Br, widgetsCheck));

        il.Append(widgetsCheck);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, widgetIndex),
            Instruction.Create(OpCodes.Ldloc, widgets),
            Instruction.Create(OpCodes.Ldlen),
            Instruction.Create(OpCodes.Conv_I4),
            Instruction.Create(OpCodes.Bge, widgetsDone),
            Instruction.Create(OpCodes.Ldloc, widgets),
            Instruction.Create(OpCodes.Ldloc, widgetIndex),
            Instruction.Create(OpCodes.Ldelem_Ref),
            Instruction.Create(OpCodes.Castclass, uiWidget),
            Instruction.Create(OpCodes.Stloc, widget),
            Instruction.Create(OpCodes.Ldloc, widget),
            Instruction.Create(OpCodes.Brfalse, widgetsNext),
            Instruction.Create(OpCodes.Ldloc, widget),
            Instruction.Create(OpCodes.Callvirt, parentHasChanged));

        il.Append(widgetsNext);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, widgetIndex),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, widgetIndex),
            Instruction.Create(OpCodes.Br, widgetsCheck));

        il.Append(widgetsDone);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, card),
            Instruction.Create(OpCodes.Ldtoken, unityBoxCollider),
            Instruction.Create(OpCodes.Call, getTypeFromHandle),
            Instruction.Create(OpCodes.Callvirt, getComponent),
            Instruction.Create(OpCodes.Castclass, unityBoxCollider),
            Instruction.Create(OpCodes.Stloc, boxCollider),
            Instruction.Create(OpCodes.Ldloc, boxCollider),
            Instruction.Create(OpCodes.Brfalse, addBoxCollider));

        var haveBoxCollider = Instruction.Create(OpCodes.Nop);
        il.Append(haveBoxCollider);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, card),
            Instruction.Create(OpCodes.Ldtoken, dragPanelContents),
            Instruction.Create(OpCodes.Call, getTypeFromHandle),
            Instruction.Create(OpCodes.Callvirt, getComponent),
            Instruction.Create(OpCodes.Castclass, dragPanelContents),
            Instruction.Create(OpCodes.Stloc, contents),
            Instruction.Create(OpCodes.Ldloc, contents),
            Instruction.Create(OpCodes.Brtrue, haveContents),
            Instruction.Create(OpCodes.Ldloc, card),
            Instruction.Create(OpCodes.Ldtoken, dragPanelContents),
            Instruction.Create(OpCodes.Call, getTypeFromHandle),
            Instruction.Create(OpCodes.Callvirt, addComponent),
            Instruction.Create(OpCodes.Castclass, dragPanelContents),
            Instruction.Create(OpCodes.Stloc, contents));

        il.Append(haveContents);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, contents),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Stfld, dragPanelContents.Fields.Single(
                field => field.Name == "draggablePanel")),
            Instruction.Create(OpCodes.Ldloc, contents),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Callvirt, setEnabled));

        il.Append(nextCard);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, index),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stloc, index),
            Instruction.Create(OpCodes.Br, loopCheck));

        il.Append(addBoxCollider);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, card),
            Instruction.Create(OpCodes.Ldtoken, unityBoxCollider),
            Instruction.Create(OpCodes.Call, getTypeFromHandle),
            Instruction.Create(OpCodes.Callvirt, addComponent),
            Instruction.Create(OpCodes.Castclass, unityBoxCollider),
            Instruction.Create(OpCodes.Stloc, boxCollider),
            Instruction.Create(OpCodes.Ldloc, boxCollider),
            Instruction.Create(OpCodes.Call, vectorZero),
            Instruction.Create(OpCodes.Callvirt, setBoxCenter),
            Instruction.Create(OpCodes.Ldloc, boxCollider),
            Instruction.Create(OpCodes.Ldc_R4, 1200f),
            Instruction.Create(OpCodes.Ldc_R4, 96f),
            Instruction.Create(OpCodes.Ldc_R4, 10f),
            Instruction.Create(OpCodes.Newobj, newVector3),
            Instruction.Create(OpCodes.Callvirt, setBoxSize),
            Instruction.Create(OpCodes.Br, haveBoxCollider));

        il.Append(finish);
        Emit(il,
            Instruction.Create(OpCodes.Ldloc, panel),
            Instruction.Create(OpCodes.Callvirt, refreshPanel),
            Instruction.Create(OpCodes.Ldloc, drag),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Callvirt, updateScrollbars));
        il.Append(done);
        return helper;
    }

    private static void EnsureDisplayOrderCall(
        MethodDefinition refresh,
        MethodDefinition orderBots,
        FieldDefinition botsField)
    {
        if (refresh.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand == orderBots))
        {
            return;
        }

        var sort = refresh.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Callvirt &&
            instruction.Operand is MethodReference &&
            ((MethodReference)instruction.Operand).Name == "Sort" &&
            ((MethodReference)instruction.Operand).DeclaringType.FullName.StartsWith(
                "System.Collections.Generic.List`1<FriendData>"));
        var insertBefore = sort;
        while (insertBefore.Previous != null &&
               insertBefore.OpCode != OpCodes.Ldloc_3)
        {
            insertBefore = insertBefore.Previous;
        }
        if (insertBefore.OpCode != OpCodes.Ldloc_3)
        {
            throw new InvalidOperationException(
                "Unable to locate the duel rank-list sort setup.");
        }
        var il = refresh.Body.GetILProcessor();
        il.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldloc_0));
        il.InsertBefore(insertBefore, Instruction.Create(OpCodes.Ldfld, botsField));
        il.InsertBefore(insertBefore, Instruction.Create(OpCodes.Call, orderBots));
        Console.WriteLine("Added 1x-first duel display ordering.");
    }

    private static void EnsureScrollCall(
        MethodDefinition refresh,
        MethodDefinition ensureScroll,
        TypeDefinition window)
    {
        if (refresh.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand == ensureScroll))
        {
            return;
        }

        var prepare = refresh.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Callvirt &&
            instruction.Operand is MethodReference &&
            ((MethodReference)instruction.Operand).Name == "PreparePVPRank" &&
            ((MethodReference)instruction.Operand).DeclaringType == window);
        var il = refresh.Body.GetILProcessor();
        il.InsertBefore(prepare.Previous, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(prepare.Previous, Instruction.Create(OpCodes.Call, ensureScroll));
        Console.WriteLine("Enabled vertical scrolling for five duel bot cards.");
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

    private static MethodReference RetargetListMethod(
        ModuleDefinition module,
        MethodReference template,
        TypeReference targetListType)
    {
        var method = new MethodReference(
            template.Name,
            template.ReturnType,
            targetListType)
        {
            HasThis = template.HasThis,
            ExplicitThis = template.ExplicitThis,
            CallingConvention = template.CallingConvention
        };
        foreach (var parameter in template.Parameters)
        {
            method.Parameters.Add(new ParameterDefinition(
                parameter.Name,
                parameter.Attributes,
                parameter.ParameterType));
        }
        return module.ImportReference(method);
    }

    private static MethodReference ImportPropertyGetter(
        ModuleDefinition module,
        Type type,
        string propertyName)
    {
        var getter = type.GetProperty(
            propertyName,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance).GetGetMethod();
        if (getter == null)
        {
            throw new InvalidOperationException(
                "Getter was not found: " + type.FullName + "." + propertyName);
        }
        return module.ImportReference(getter);
    }

    private static MethodReference ImportPropertySetter(
        ModuleDefinition module,
        Type type,
        string propertyName)
    {
        var setter = type.GetProperty(
            propertyName,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance).GetSetMethod();
        if (setter == null)
        {
            throw new InvalidOperationException(
                "Setter was not found: " + type.FullName + "." + propertyName);
        }
        return module.ImportReference(setter);
    }

    private static void Emit(ILProcessor il, params Instruction[] instructions)
    {
        foreach (var instruction in instructions)
        {
            il.Append(instruction);
        }
    }
}
