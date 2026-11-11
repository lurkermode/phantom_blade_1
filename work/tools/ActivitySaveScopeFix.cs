using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class ActivitySaveScopeFix
{
    private static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine(
                "Usage: ActivitySaveScopeFix <main-input> <main-output> " +
                "<activity-input> <activity-output>");
            return 2;
        }

        try
        {
            MakeCurrentSaveIdPublic(args[0], args[1]);
            ScopeActivityPrefs(args[2], args[3]);
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

    private static void MakeCurrentSaveIdPublic(
        string input,
        string output)
    {
        using (var assembly = AssemblyDefinition.ReadAssembly(input))
        {
            var persistence =
                assembly.MainModule.GetType("YzrLocal.LocalPersistence");
            var method = persistence == null
                ? null
                : persistence.Methods.SingleOrDefault(candidate =>
                    candidate.Name == "CurrentSaveID" &&
                    candidate.IsStatic &&
                    candidate.Parameters.Count == 0);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "LocalPersistence.CurrentSaveID was not found.");
            }

            method.Attributes =
                (method.Attributes & ~MethodAttributes.MemberAccessMask) |
                MethodAttributes.Public;
            assembly.Write(output);
            Console.WriteLine(
                "Made LocalPersistence.CurrentSaveID public for activity scoping.");
        }
    }

    private static void ScopeActivityPrefs(
        string input,
        string output)
    {
        using (var assembly = AssemblyDefinition.ReadAssembly(input))
        {
            var module = assembly.MainModule;
            var activityType = module.GetType("YzrLocal.ActivityRewards");
            var newServerType =
                module.GetType("YzrLocal.NewServerActivityRewards");
            if (activityType == null || newServerType == null)
            {
                throw new InvalidOperationException(
                    "Activity reward types were not found.");
            }

            var playerPrefs = FindPlayerPrefsMethods(
                activityType,
                newServerType);
            var helper = module.GetType("YzrLocal.SaveScopedPrefs");
            if (helper == null)
            {
                helper = AddHelperType(module, playerPrefs);
                Console.WriteLine(
                    "Added save-scoped PlayerPrefs helper.");
            }

            int patchedCalls = 0;
            foreach (var type in new[] { activityType, newServerType })
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
                        if (reference == null ||
                            reference.DeclaringType.FullName !=
                                "UnityEngine.PlayerPrefs")
                        {
                            continue;
                        }
                        if (reference.Name == "Save")
                        {
                            continue;
                        }

                        var scoped = helper.Methods.SingleOrDefault(
                            candidate =>
                                candidate.Name == reference.Name &&
                                candidate.Parameters.Count ==
                                    reference.Parameters.Count);
                        if (scoped == null)
                        {
                            throw new InvalidOperationException(
                                "Unhandled PlayerPrefs method: " +
                                reference.Name);
                        }

                        instruction.Operand = scoped;
                        patchedCalls++;
                    }
                }
            }

            if (patchedCalls == 0)
            {
                throw new InvalidOperationException(
                    "No PlayerPrefs activity calls were patched.");
            }

            assembly.Write(output);
            Console.WriteLine(
                "Scoped " + patchedCalls +
                " activity PlayerPrefs calls by save ID.");
        }
    }

    private static PlayerPrefsMethods FindPlayerPrefsMethods(
        TypeDefinition activityType,
        TypeDefinition newServerType)
    {
        var calls = activityType.Methods
            .Concat(newServerType.Methods)
            .SelectMany(method => method.HasBody
                ? method.Body.Instructions
                : Enumerable.Empty<Instruction>())
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Where(method =>
                method.DeclaringType.FullName ==
                    "UnityEngine.PlayerPrefs")
            .ToList();

        return new PlayerPrefsMethods
        {
            GetInt = Required(calls, "GetInt", 2),
            SetInt = Required(calls, "SetInt", 2),
            GetString = Required(calls, "GetString", 2),
            SetString = Required(calls, "SetString", 2),
            HasKey = Required(calls, "HasKey", 1),
        };
    }

    private static MethodReference Required(
        System.Collections.Generic.List<MethodReference> calls,
        string name,
        int parameterCount)
    {
        var method = calls.FirstOrDefault(candidate =>
            candidate.Name == name &&
            candidate.Parameters.Count == parameterCount);
        if (method == null)
        {
            throw new InvalidOperationException(
                "PlayerPrefs." + name + " was not found.");
        }
        return method;
    }

    private static TypeDefinition AddHelperType(
        ModuleDefinition module,
        PlayerPrefsMethods playerPrefs)
    {
        var assemblyReference = module.AssemblyReferences.SingleOrDefault(
            reference => reference.Name == "Assembly-CSharp");
        if (assemblyReference == null)
        {
            throw new InvalidOperationException(
                "Assembly-CSharp reference was not found.");
        }

        var persistenceType = new TypeReference(
            "YzrLocal",
            "LocalPersistence",
            module,
            assemblyReference);
        var currentSaveId = module.ImportReference(
            new MethodReference(
                "CurrentSaveID",
                module.TypeSystem.Int32,
                persistenceType)
            {
                HasThis = false,
                ExplicitThis = false
            });
        var concat = module.ImportReference(
            typeof(string).GetMethod(
                "Concat",
                new[]
                {
                    typeof(object),
                    typeof(object),
                    typeof(object)
                }));

        var type = new TypeDefinition(
            "YzrLocal",
            "SaveScopedPrefs",
            TypeAttributes.Public |
            TypeAttributes.Class |
            TypeAttributes.Abstract |
            TypeAttributes.Sealed |
            TypeAttributes.BeforeFieldInit,
            module.TypeSystem.Object);
        module.Types.Add(type);

        var key = AddKeyMethod(
            module,
            type,
            currentSaveId,
            concat);
        AddGetInt(type, key, playerPrefs.GetInt);
        AddSetInt(type, key, playerPrefs.SetInt);
        AddGetString(type, key, playerPrefs.GetString);
        AddSetString(type, key, playerPrefs.SetString);
        AddHasKey(type, key, playerPrefs.HasKey);

        return type;
    }

    private static MethodDefinition AddKeyMethod(
        ModuleDefinition module,
        TypeDefinition type,
        MethodReference currentSaveId,
        MethodReference concat)
    {
        var method = NewMethod(
            "Key",
            module.TypeSystem.String);
        method.Parameters.Add(new ParameterDefinition(
            "key",
            ParameterAttributes.None,
            module.TypeSystem.String));
        type.Methods.Add(method);

        Emit(
            method.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldstr, ".Save."),
            Instruction.Create(OpCodes.Call, currentSaveId),
            Instruction.Create(OpCodes.Box, module.TypeSystem.Int32),
            Instruction.Create(OpCodes.Call, concat),
            Instruction.Create(OpCodes.Ret));
        method.Body.MaxStackSize = 3;
        return method;
    }

    private static void AddGetInt(
        TypeDefinition type,
        MethodDefinition key,
        MethodReference playerPrefsGetInt)
    {
        var method = NewMethod("GetInt", type.Module.TypeSystem.Int32);
        method.Parameters.Add(StringParameter(type.Module, "key"));
        method.Parameters.Add(IntParameter(type.Module, "defaultValue"));
        type.Methods.Add(method);

        Emit(
            method.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, key),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Call, playerPrefsGetInt),
            Instruction.Create(OpCodes.Ret));
    }

    private static void AddSetInt(
        TypeDefinition type,
        MethodDefinition key,
        MethodReference playerPrefsSetInt)
    {
        var method = NewMethod("SetInt", type.Module.TypeSystem.Void);
        method.Parameters.Add(StringParameter(type.Module, "key"));
        method.Parameters.Add(IntParameter(type.Module, "value"));
        type.Methods.Add(method);

        Emit(
            method.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, key),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Call, playerPrefsSetInt),
            Instruction.Create(OpCodes.Ret));
    }

    private static void AddGetString(
        TypeDefinition type,
        MethodDefinition key,
        MethodReference playerPrefsGetString)
    {
        var method = NewMethod("GetString", type.Module.TypeSystem.String);
        method.Parameters.Add(StringParameter(type.Module, "key"));
        method.Parameters.Add(StringParameter(type.Module, "defaultValue"));
        type.Methods.Add(method);

        Emit(
            method.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, key),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Call, playerPrefsGetString),
            Instruction.Create(OpCodes.Ret));
    }

    private static void AddSetString(
        TypeDefinition type,
        MethodDefinition key,
        MethodReference playerPrefsSetString)
    {
        var method = NewMethod("SetString", type.Module.TypeSystem.Void);
        method.Parameters.Add(StringParameter(type.Module, "key"));
        method.Parameters.Add(StringParameter(type.Module, "value"));
        type.Methods.Add(method);

        Emit(
            method.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, key),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Call, playerPrefsSetString),
            Instruction.Create(OpCodes.Ret));
    }

    private static void AddHasKey(
        TypeDefinition type,
        MethodDefinition key,
        MethodReference playerPrefsHasKey)
    {
        var method = NewMethod("HasKey", type.Module.TypeSystem.Boolean);
        method.Parameters.Add(StringParameter(type.Module, "key"));
        type.Methods.Add(method);

        Emit(
            method.Body.GetILProcessor(),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, key),
            Instruction.Create(OpCodes.Call, playerPrefsHasKey),
            Instruction.Create(OpCodes.Ret));
    }

    private static MethodDefinition NewMethod(
        string name,
        TypeReference returnType)
    {
        return new MethodDefinition(
            name,
            MethodAttributes.Public |
            MethodAttributes.Static |
            MethodAttributes.HideBySig,
            returnType);
    }

    private static ParameterDefinition StringParameter(
        ModuleDefinition module,
        string name)
    {
        return new ParameterDefinition(
            name,
            ParameterAttributes.None,
            module.TypeSystem.String);
    }

    private static ParameterDefinition IntParameter(
        ModuleDefinition module,
        string name)
    {
        return new ParameterDefinition(
            name,
            ParameterAttributes.None,
            module.TypeSystem.Int32);
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

    private sealed class PlayerPrefsMethods
    {
        public MethodReference GetInt;
        public MethodReference SetInt;
        public MethodReference GetString;
        public MethodReference SetString;
        public MethodReference HasKey;
    }
}
