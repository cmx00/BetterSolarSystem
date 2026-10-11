using GalacticScale;
using HarmonyLib;
using UnityEngine;

namespace BetterSolarSystem
{
    public static class SurfaceGeneration
    {
        public const string Marker = "BetterSolarSystem.Surface.v3.1";
        public const float BirthOreBias = .18f;
        public static Vector3 WeakenBias(Vector3 original) => original.normalized * BirthOreBias;

        public static void Apply(Harmony harmony)
        {
            var method = AccessTools.Method(typeof(VeinAlgorithms), "InitVeinGroupVector");
            if (method == null || method.ReturnType != typeof(Vector3))
                throw new System.MissingMethodException("GS2 birth vein bias method changed.");
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(SurfaceGeneration), nameof(AdjustBirthBias)));
        }
        public static void AdjustBirthBias(bool birth, ref Vector3 __result)
        {
            // Only new 3.1 galaxies: older saves keep exactly their original placement recipe.
            // Starting iron/copper are explicitly added by GS2 and do not use this vector.
            if (birth && BalancedGeneration.IsManagedGalaxy && GSSettings.ThemeLibrary.ContainsKey(Marker))
                __result = WeakenBias(__result);
        }
    }
}
