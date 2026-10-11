using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BetterSolarSystem.Compat
{
    // BepInEx preloader: the game assembly is changed IN MEMORY only. Restoring
    // this API before JIT is necessary: a Harmony transpiler alone is too late
    // on runtimes that compile the original method before installing a detour.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get { yield return "Assembly-CSharp.dll"; } }
        public static void Patch(AssemblyDefinition assembly)
        {
            var module = assembly.MainModule;
            var type = module.Types.Single(t => t.Name == "NameGen");
            if (type.Methods.Any(m => m.Name == "_RandomStarName" && m.Parameters.Count == 2)) return;
            var current = type.Methods.SingleOrDefault(m => m.Name == "_RandomStarName" && m.Parameters.Count == 3 &&
                m.Parameters[0].ParameterType.FullName == "System.Int32" && m.Parameters[1].ParameterType.FullName == "System.Int32" && m.Parameters[2].ParameterType.FullName == "StarData");
            if (current == null) throw new MissingMethodException("Unsupported NameGen language API; compatibility bridge not installed.");
            var bridge = new MethodDefinition("_RandomStarName", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.String);
            bridge.Parameters.Add(new ParameterDefinition("seed", ParameterAttributes.None, module.TypeSystem.Int32));
            bridge.Parameters.Add(new ParameterDefinition("starData", ParameterAttributes.None, current.Parameters[2].ParameterType));
            var il = bridge.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, 1033); // Preserve legacy GS2's English-name mode.
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, current);
            il.Emit(OpCodes.Ret);
            type.Methods.Add(bridge);
        }
    }
}
