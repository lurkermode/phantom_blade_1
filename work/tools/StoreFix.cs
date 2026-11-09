using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class StoreFix
{
    private static bool IsField(Instruction instruction, string name)
    {
        return instruction.OpCode == OpCodes.Ldfld &&
               instruction.Operand is FieldReference &&
               ((FieldReference)instruction.Operand).Name == name;
    }

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: StoreFix <input> <output>");
            return 2;
        }

        try
        {
            var reader = new ReaderParameters { ReadSymbols = false };
            using (var assembly = AssemblyDefinition.ReadAssembly(args[0], reader))
            {
                var module = assembly.MainModule;
                var itemManager = module.GetType("ItemManager");
                var onBuyItem = itemManager.Methods.Single(m => m.Name == "OnBuyItem");
                var body = onBuyItem.Body;
                body.InitLocals = true;
                var instructions = body.Instructions;
                var il = body.GetILProcessor();

                var purchaseCount = new VariableDefinition(module.TypeSystem.Int32);
                var deliveryIndex = new VariableDefinition(module.TypeSystem.Int32);
                var deliveryItem = new VariableDefinition(itemManager.Module.GetType("Item"));
                body.Variables.Add(purchaseCount);
                body.Variables.Add(deliveryIndex);
                body.Variables.Add(deliveryItem);

                var eventMessageVariable = body.Variables[0];
                var storeVariable = body.Variables.Single(v => v.VariableType.FullName == "StoreItem");
                var playerVariable = body.Variables.Single(v => v.VariableType.FullName == "PlayerDataStruct");
                var messageVariable = body.Variables.Last(v => v.VariableType.FullName == "MessageEventArgs");
                var finalReturn = instructions.Last(i => i.OpCode == OpCodes.Ret);

                var gameMainType = module.GetType("GameMain");
                var messageType = module.GetType("MessageEventArgs");
                var containMessage = messageType.Methods.Single(m => m.Name == "ContainMessage");
                var getMessage = messageType.Methods.Single(m => m.Name == "GetMessage");
                var addMessage = messageType.Methods.Single(m => m.Name == "AddMessage");
                var toInt32 = instructions.Where(i =>
                    i.OpCode == OpCodes.Call &&
                    i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name == "ToInt32")
                    .Select(i => (MethodReference)i.Operand)
                    .First();

                var shopIdStored = instructions.First(i => i.OpCode == OpCodes.Stloc_1);
                var countReady = Instruction.Create(OpCodes.Nop);
                var defaultCount = Instruction.Create(OpCodes.Nop);
                var countInstructions = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Ldloc, eventMessageVariable),
                    Instruction.Create(OpCodes.Ldstr, "count"),
                    Instruction.Create(OpCodes.Callvirt, containMessage),
                    Instruction.Create(OpCodes.Brfalse, defaultCount),
                    Instruction.Create(OpCodes.Ldloc, eventMessageVariable),
                    Instruction.Create(OpCodes.Ldstr, "count"),
                    Instruction.Create(OpCodes.Callvirt, getMessage),
                    Instruction.Create(OpCodes.Call, toInt32),
                    Instruction.Create(OpCodes.Stloc, purchaseCount),
                    Instruction.Create(OpCodes.Br, countReady),
                    defaultCount,
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Stloc, purchaseCount),
                    countReady,
                    Instruction.Create(OpCodes.Ldloc, purchaseCount),
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Blt, finalReturn)
                };
                InsertAfterList(il, shopIdStored, countInstructions);

                var availabilityCheck = instructions.First(i =>
                    IsField(i, "avaliableTime") &&
                    i.Next != null &&
                    i.Next.OpCode == OpCodes.Ldc_I4_0 &&
                    i.Next.Next != null &&
                    i.Next.Next.OpCode == OpCodes.Bgt_S);
                var availabilitySuccess = (Instruction)availabilityCheck.Next.Next.Operand;
                var availabilityCount = availabilityCheck.Next;
                var availabilityBranch = availabilityCheck.Next.Next;
                var availabilityStore = availabilityCheck.Previous;
                il.Replace(availabilityStore, Instruction.Create(OpCodes.Br, availabilitySuccess));
                instructions.Remove(availabilityCheck);
                instructions.Remove(availabilityCount);
                instructions.Remove(availabilityBranch);

                var useCurrency = instructions.Single(i =>
                    i.OpCode == OpCodes.Callvirt &&
                    i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name == "UseCurrency");
                var coinTypeLoad = useCurrency.Previous;
                if (!IsField(coinTypeLoad, "coinType"))
                {
                    throw new InvalidOperationException("Unable to locate store price calculation.");
                }
                var storeForCoinType = coinTypeLoad.Previous;
                if (storeForCoinType.OpCode != OpCodes.Ldloc_2 &&
                    (storeForCoinType.OpCode != OpCodes.Ldloc ||
                     !(storeForCoinType.Operand is VariableDefinition) ||
                     (VariableDefinition)storeForCoinType.Operand != storeVariable))
                {
                    throw new InvalidOperationException("Unable to locate store currency field access.");
                }
                il.InsertBefore(storeForCoinType, Instruction.Create(OpCodes.Ldloc, purchaseCount));
                il.InsertBefore(storeForCoinType, Instruction.Create(OpCodes.Mul));

                var availabilityDecrement = instructions.First(i =>
                    IsField(i, "avaliableTime") &&
                    i.Next != null && i.Next.OpCode == OpCodes.Ldc_I4_1);
                il.Replace(availabilityDecrement.Next, Instruction.Create(OpCodes.Ldc_I4_0));

                var getItemCall = instructions.Single(i =>
                    i.OpCode == OpCodes.Callvirt &&
                    i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).DeclaringType.Name == "ItemManager" &&
                    ((MethodReference)i.Operand).Name == "GetItem" &&
                    ((MethodReference)i.Operand).Parameters.Count == 5);
                var getItem = (MethodReference)getItemCall.Operand;
                var getItemPrototype = itemManager.Methods.Single(m =>
                    m.Name == "GetItemPrototype" && m.Parameters.Count == 1);

                var obsoleteArguments = new List<Instruction>();
                var cursor = getItemCall.Previous;
                for (var i = 0; i < 4; i++)
                {
                    obsoleteArguments.Add(cursor);
                    cursor = cursor.Previous;
                }
                foreach (var instruction in obsoleteArguments)
                {
                    instructions.Remove(instruction);
                }
                getItemCall.OpCode = OpCodes.Callvirt;
                getItemCall.Operand = getItemPrototype;

                var itemStored = getItemCall.Next;
                if (itemStored.OpCode != OpCodes.Stloc_S ||
                    !(itemStored.Operand is VariableDefinition))
                {
                    throw new InvalidOperationException("Unexpected IL after GetItemPrototype.");
                }
                var prototypeVariable = (VariableDefinition)itemStored.Operand;
                var deliveryStart = itemStored.Next;

                var prototypeReady = Instruction.Create(OpCodes.Nop);
                il.InsertBefore(deliveryStart, Instruction.Create(OpCodes.Ldloc, prototypeVariable));
                il.InsertBefore(deliveryStart, Instruction.Create(OpCodes.Brtrue, prototypeReady));
                il.InsertBefore(deliveryStart, Instruction.Create(OpCodes.Br, finalReturn));
                il.InsertBefore(deliveryStart, prototypeReady);

                var addItemCall = instructions.Single(i =>
                    i.OpCode == OpCodes.Callvirt &&
                    i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).DeclaringType.FullName == "PlayerDataStruct" &&
                    ((MethodReference)i.Operand).Name == "AddItem");
                var deliveryEnd = addItemCall.Next;
                var originalDeliveryEnd = deliveryEnd;
                var itemTypeField = module.GetType("Item").Fields.Single(f => f.Name == "itemType");
                var storeIdField = module.GetType("StoreItem").Fields.Single(f => f.Name == "itemID");
                var storeRuleField = module.GetType("StoreItem").Fields.Single(f => f.Name == "rule");
                var getInstance = itemManager.Methods.Single(m => m.Name == "get_Instance");
                var getStackableItem = itemManager.Methods.Single(m =>
                    m.Name == "GetStackableItem" && m.Parameters.Count == 2);
                var addItem = (MethodReference)addItemCall.Operand;

                var stackableLabel = Instruction.Create(OpCodes.Nop);
                var sideEffectLabel = Instruction.Create(OpCodes.Nop);
                var deliveryDone = Instruction.Create(OpCodes.Nop);
                var loopStart = Instruction.Create(OpCodes.Nop);
                var loopBody = Instruction.Create(OpCodes.Nop);
                var addGeneratedItem = Instruction.Create(OpCodes.Nop);
                var loopIncrement = Instruction.Create(OpCodes.Nop);

                var delivery = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Ldloc, prototypeVariable),
                    Instruction.Create(OpCodes.Ldfld, itemTypeField),
                    Instruction.Create(OpCodes.Ldc_I4_8),
                    Instruction.Create(OpCodes.Beq, stackableLabel),
                    Instruction.Create(OpCodes.Ldloc, prototypeVariable),
                    Instruction.Create(OpCodes.Ldfld, itemTypeField),
                    Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)9),
                    Instruction.Create(OpCodes.Beq, stackableLabel),
                    Instruction.Create(OpCodes.Ldloc, prototypeVariable),
                    Instruction.Create(OpCodes.Ldfld, itemTypeField),
                    Instruction.Create(OpCodes.Ldc_I4_4),
                    Instruction.Create(OpCodes.Beq, sideEffectLabel),
                    Instruction.Create(OpCodes.Ldloc, prototypeVariable),
                    Instruction.Create(OpCodes.Ldfld, itemTypeField),
                    Instruction.Create(OpCodes.Ldc_I4_5),
                    Instruction.Create(OpCodes.Beq, sideEffectLabel),

                    Instruction.Create(OpCodes.Ldc_I4_0),
                    Instruction.Create(OpCodes.Stloc, deliveryIndex),
                    loopStart,
                    Instruction.Create(OpCodes.Ldloc, deliveryIndex),
                    Instruction.Create(OpCodes.Ldloc, purchaseCount),
                    Instruction.Create(OpCodes.Blt, loopBody),
                    Instruction.Create(OpCodes.Br, deliveryDone),
                    loopBody,
                    Instruction.Create(OpCodes.Call, getInstance),
                    Instruction.Create(OpCodes.Ldloc, storeVariable),
                    Instruction.Create(OpCodes.Ldfld, storeIdField),
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Ldloc, storeVariable),
                    Instruction.Create(OpCodes.Ldfld, storeRuleField),
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Ldc_I4_0),
                    Instruction.Create(OpCodes.Callvirt, getItem),
                    Instruction.Create(OpCodes.Stloc, deliveryItem),
                    Instruction.Create(OpCodes.Ldloc, deliveryItem),
                    Instruction.Create(OpCodes.Brtrue, addGeneratedItem),
                    Instruction.Create(OpCodes.Br, loopIncrement),
                    addGeneratedItem,
                    Instruction.Create(OpCodes.Ldloc, playerVariable),
                    Instruction.Create(OpCodes.Ldloc, deliveryItem),
                    Instruction.Create(OpCodes.Callvirt, addItem),
                    loopIncrement,
                    Instruction.Create(OpCodes.Ldloc, deliveryIndex),
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Add),
                    Instruction.Create(OpCodes.Stloc, deliveryIndex),
                    Instruction.Create(OpCodes.Br, loopStart),

                    stackableLabel,
                    Instruction.Create(OpCodes.Call, getInstance),
                    Instruction.Create(OpCodes.Ldloc, storeVariable),
                    Instruction.Create(OpCodes.Ldfld, storeIdField),
                    Instruction.Create(OpCodes.Ldloc, purchaseCount),
                    Instruction.Create(OpCodes.Callvirt, getStackableItem),
                    Instruction.Create(OpCodes.Pop),
                    Instruction.Create(OpCodes.Br, deliveryDone),

                    sideEffectLabel,
                    Instruction.Create(OpCodes.Call, getInstance),
                    Instruction.Create(OpCodes.Ldloc, storeVariable),
                    Instruction.Create(OpCodes.Ldfld, storeIdField),
                    Instruction.Create(OpCodes.Ldloc, purchaseCount),
                    Instruction.Create(OpCodes.Ldloc, storeVariable),
                    Instruction.Create(OpCodes.Ldfld, storeRuleField),
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Ldc_I4_0),
                    Instruction.Create(OpCodes.Callvirt, getItem),
                    Instruction.Create(OpCodes.Pop),
                    deliveryDone
                };

                foreach (var instruction in delivery)
                {
                    il.InsertBefore(deliveryStart, instruction);
                }
                while (deliveryStart != originalDeliveryEnd)
                {
                    var remove = deliveryStart;
                    deliveryStart = deliveryStart.Next;
                    instructions.Remove(remove);
                }

                var itemUnidString = instructions.Single(i =>
                    i.OpCode == OpCodes.Ldstr && (string)i.Operand == "itemUnid");
                var itemUnidAdd = itemUnidString.Next;
                while (itemUnidAdd.OpCode != OpCodes.Callvirt)
                {
                    itemUnidAdd = itemUnidAdd.Next;
                }

                var convertIntToString = module.ImportReference(
                    typeof(Convert).GetMethod("ToString", new[] { typeof(int) }));
                var popupFields = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Ldloc, messageVariable),
                    Instruction.Create(OpCodes.Ldstr, "RuneCount"),
                    Instruction.Create(OpCodes.Ldstr, "0"),
                    Instruction.Create(OpCodes.Callvirt, addMessage),
                    Instruction.Create(OpCodes.Ldloc, messageVariable),
                    Instruction.Create(OpCodes.Ldstr, "StackID0"),
                    Instruction.Create(OpCodes.Ldloc, storeVariable),
                    Instruction.Create(OpCodes.Ldfld, storeIdField),
                    Instruction.Create(OpCodes.Call, convertIntToString),
                    Instruction.Create(OpCodes.Callvirt, addMessage),
                    Instruction.Create(OpCodes.Ldloc, messageVariable),
                    Instruction.Create(OpCodes.Ldstr, "StackCount0"),
                    Instruction.Create(OpCodes.Ldloc, purchaseCount),
                    Instruction.Create(OpCodes.Call, convertIntToString),
                    Instruction.Create(OpCodes.Callvirt, addMessage)
                };
                foreach (var instruction in popupFields)
                {
                    il.InsertAfter(itemUnidAdd, instruction);
                    itemUnidAdd = instruction;
                }

                var popupEvent = instructions.Single(i =>
                    (i.OpCode == OpCodes.Ldc_I4 || i.OpCode == OpCodes.Ldc_I4_S) &&
                    Convert.ToInt32(i.Operand) == 0x1a0);
                var emptyEventArgs = popupEvent.Next;
                if (emptyEventArgs.OpCode != OpCodes.Newobj ||
                    ((MethodReference)emptyEventArgs.Operand).DeclaringType.FullName != "MessageEventArgs")
                {
                    throw new InvalidOperationException("Unexpected reward popup event arguments.");
                }
                il.Replace(emptyEventArgs, Instruction.Create(OpCodes.Ldloc, messageVariable));

                var crystalStoreCard = module.GetType("CrystalStoreCard");
                var crystalBuyButton = crystalStoreCard.Methods.Single(m => m.Name == "OnButtonBuy");
                var crystalButtonInstructions = crystalBuyButton.Body.Instructions;
                var availabilityUiCheck = crystalButtonInstructions.Take(5).ToList();
                if (!IsField(availabilityUiCheck[2], "avaliableTime") ||
                    availabilityUiCheck[4].OpCode != OpCodes.Ble)
                {
                    throw new InvalidOperationException("Unexpected crystal store availability check.");
                }
                foreach (var instruction in availabilityUiCheck)
                {
                    crystalButtonInstructions.Remove(instruction);
                }

                body.MaxStackSize = Math.Max(body.MaxStackSize, 8);
                assembly.Write(args[1]);
            }

            Console.WriteLine("Patched offline store quantity and stackable delivery.");
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

    private static void InsertAfterList(
        ILProcessor il,
        Instruction anchor,
        List<Instruction> inserted)
    {
        foreach (var instruction in inserted)
        {
            il.InsertAfter(anchor, instruction);
            anchor = instruction;
        }
    }
}
