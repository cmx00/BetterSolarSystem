using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BetterSolarSystem
{
    public static class RuntimeCompatibility
    {
        private static readonly MethodInfo StarName = AccessTools.Method(typeof(NameGen), "_RandomStarName", new[] { typeof(int), typeof(int), typeof(StarData) });

        public static string RandomStarName(int seed, StarData star)
        {
            if (StarName == null) throw new MissingMethodException("NameGen._RandomStarName(int,int,StarData)");
            return (string)StarName.Invoke(null, new object[] { seed, 1033, star });
        }

        // The installed GS2 references the removed two-argument member. Harmony
        // reads that unresolved MemberRef as a null operand, before emitting IL.
        // Replace it in the same patch transaction that adds our postfix.
        public static IEnumerable<CodeInstruction> RepairStarNameCall(IEnumerable<CodeInstruction> source)
        {
            var codes = source.ToList();
            int changed = 0;
            foreach (var code in codes)
                if (code.opcode == OpCodes.Call && (code.operand == null ||
                    code.operand is MethodInfo m && m.DeclaringType == typeof(NameGen) && m.Name == "_RandomStarName" && m.GetParameters().Length == 2))
                {
                    code.operand = AccessTools.Method(typeof(RuntimeCompatibility), nameof(RandomStarName));
                    changed++;
                }
            if (changed > 1) throw new InvalidOperationException("Ambiguous GS2 obsolete naming call; refusing patch.");
            return codes;
        }
    }
}
