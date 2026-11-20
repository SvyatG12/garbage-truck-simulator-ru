using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Patch
{
    private static MethodDefinition Method(ModuleDefinition module, string type, string name)
    {
        return module.GetType(type).Methods.Single(method => method.Name == name && method.HasBody);
    }

    private static void Apply(ModuleDefinition game, ModuleDefinition runtime)
    {
        var refresh = game.ImportReference(Method(runtime, "GarbageTruckRu.RussianUi", "Refresh"));
        var translate = game.ImportReference(Method(runtime, "GarbageTruckRu.RussianUi", "Translate"));
        var shift = game.ImportReference(Method(runtime, "GarbageTruckRu.RussianUi", "BeginShift"));

        var canvas = Method(game, "C_CanvasManager", "Update");
        canvas.Body.GetILProcessor().InsertBefore(canvas.Body.Instructions[0], Instruction.Create(OpCodes.Call, refresh));

        var start = Method(game, "C_JobControl", "StartLevel");
        var startIl = start.Body.GetILProcessor();
        var entry = start.Body.Instructions[0];
        startIl.InsertBefore(entry, Instruction.Create(OpCodes.Ldarg_0));
        startIl.InsertBefore(entry, Instruction.Create(OpCodes.Ldfld, start.DeclaringType.Fields.Single(field => field.Name == "menuSelectionMode")));
        startIl.InsertBefore(entry, Instruction.Create(OpCodes.Ldarg_0));
        startIl.InsertBefore(entry, Instruction.Create(OpCodes.Ldfld, start.DeclaringType.Fields.Single(field => field.Name == "menuSelectionTruck")));
        startIl.InsertBefore(entry, Instruction.Create(OpCodes.Call, shift));

        var textUpdate = Method(game, "C_UI_translate", "Update");
        // Make room for the extra instructions without overflowing any short branch.
        foreach (var instruction in textUpdate.Body.Instructions)
        {
            if (instruction.OpCode == OpCodes.Br_S) instruction.OpCode = OpCodes.Br;
            if (instruction.OpCode == OpCodes.Brfalse_S) instruction.OpCode = OpCodes.Brfalse;
            if (instruction.OpCode == OpCodes.Brtrue_S) instruction.OpCode = OpCodes.Brtrue;
            if (instruction.OpCode == OpCodes.Beq_S) instruction.OpCode = OpCodes.Beq;
            if (instruction.OpCode == OpCodes.Bne_Un_S) instruction.OpCode = OpCodes.Bne_Un;
            if (instruction.OpCode == OpCodes.Bge_S) instruction.OpCode = OpCodes.Bge;
        }
        var stores = textUpdate.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Stfld
            && ((FieldReference)instruction.Operand).Name == "output").ToList();
        if (stores.Count != 2)
            throw new InvalidOperationException("Unexpected built-in localization routine.");
        var textIl = textUpdate.Body.GetILProcessor();
        foreach (var store in stores)
        {
            textIl.InsertBefore(store, Instruction.Create(OpCodes.Ldarg_0));
            textIl.InsertBefore(store, Instruction.Create(OpCodes.Call, translate));
        }

        var bonus = Method(game, "C_CanvasManager", "ShowGUICleanupFragmentsReward");
        var message = bonus.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Ldstr
            && (string)instruction.Operand == "superclean");
        var bonusIl = bonus.Body.GetILProcessor();
        var context = Instruction.Create(OpCodes.Ldnull);
        bonusIl.InsertAfter(message, context);
        bonusIl.InsertAfter(context, Instruction.Create(OpCodes.Call, translate));
    }

    private static int Main(string[] args)
    {
        try
        {
            using (var game = AssemblyDefinition.ReadAssembly(args[0]))
            using (var runtime = AssemblyDefinition.ReadAssembly(args[1]))
            {
                Apply(game.MainModule, runtime.MainModule);
                game.Write(args[2]);
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
