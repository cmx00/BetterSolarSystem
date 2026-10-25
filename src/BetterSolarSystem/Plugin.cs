using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GalacticScale;
using GalacticScale.Generators;
using HarmonyLib;
using UnityEngine;
using BetterSolarSystem.Balance;

namespace BetterSolarSystem
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInIncompatibility("cmx.GS2Tweaks")]
    [BepInIncompatibility("com.myself.globalsignaltower")]
    [BepInDependency("dsp.galactic-scale.2", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "cmx.BetterSolarSystem";
        public const string PluginName = "BetterSolarSystem";
        public const string PluginVersion = "3.3.0";
        internal static ManualLogSource Log;
        internal static ConfigEntry<float> VeinBaseRadius, VeinExponent;
        internal static ConfigEntry<bool> ScaleRareVeins, SanitizeLegacySaves;
        private Harmony harmony;
        private Harmony travelHarmony, defenseHarmony;

        private void Awake()
        {
            Log = Logger;
            BalancePresets.Configure(Config);
            // These settings apply ONLY to migration of historical saves.
            VeinBaseRadius = ConfigMigration.BindLegacy(Config, "cmx.GS2Tweaks", "1 - Vein clusters / 矿团", "Base radius / 基准半径", 200f, "Legacy save repair only. New generation uses the balanced contract.");
            VeinExponent = ConfigMigration.BindLegacy(Config, "cmx.GS2Tweaks", "1 - Vein clusters / 矿团", "Exponent / 指数", 1f, "Legacy save repair only.");
            ScaleRareVeins = ConfigMigration.BindLegacy(Config, "cmx.GS2Tweaks", "1 - Vein clusters / 矿团", "Scale rare veins / 稀有矿也缩放", false, "Legacy save repair only.");
            SanitizeLegacySaves = ConfigMigration.BindLegacy(Config, "cmx.GS2Tweaks", "4 - Legacy saves / 旧存档修复", "Sanitize on load / 读档时修复", true, "Repair historical vein tables; never alter saved geometry.");
            harmony = new Harmony(PluginGuid);
            Patch(typeof(GameData), "Import", null, nameof(Patches.GameImportPostfix));
            Patch(typeof(PlanetData), "RegenerateName", nameof(Patches.PlanetNamePrefix));
            Patch(typeof(GS2Generator2), "Generate", nameof(Patches.GeneratePrefix), nameof(Patches.GeneratePostfix));
            harmony.Patch(AccessTools.Method(typeof(GS2Generator2), "GenerateStars"),
                postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.StarsPostfix)),
                transpiler: new HarmonyMethod(typeof(RuntimeCompatibility), nameof(RuntimeCompatibility.RepairStarNameCall)));
            harmony.Patch(AccessTools.Method(typeof(GS2Generator3), "GenerateStars"),
                transpiler: new HarmonyMethod(typeof(RuntimeCompatibility), nameof(RuntimeCompatibility.RepairStarNameCall)));
            Patch(typeof(GS2Generator2), "GeneratePlanetsForStar", nameof(Patches.SystemPrefix));
            Patch(typeof(GS2Generator2), "NamePlanets", nameof(Patches.NamePrefix));
            Patch(typeof(VeinAlgorithms), "AddSpecialVeins", nameof(Patches.SpecialVeinsPrefix));
            Patch(typeof(GS2), "Import", null, nameof(Patches.ImportPostfix));
            Patch(typeof(VeinAlgorithms), "GenerateVeinsGS2", null, nameof(Patches.VeinGenPostfix));
            Patch(typeof(VeinAlgorithms), "AddVeinToPlanet", nameof(Patches.VeinAmountPrefix));
            SurfaceGeneration.Apply(harmony);

            RuntimeBalance.Configure(Config);
            try {
                travelHarmony = new Harmony(PluginGuid + ".Balance.Travel");
                RuntimeBalance.Apply(travelHarmony);
            } catch (Exception error) {
                travelHarmony?.UnpatchSelf(); travelHarmony = null;
                Log.LogError("Travel balance failed; galaxy generation remains active. " + error);
            }
            try {
                defenseHarmony = new Harmony(PluginGuid + ".Balance.Defense");
                DefenseBalance.Initialize(Config, defenseHarmony);
            } catch (Exception error) {
                defenseHarmony?.UnpatchSelf(); defenseHarmony = null;
                DefenseBalance.Shutdown();
                Log.LogError("Defense balance failed; galaxy generation remains active. " + error);
            }
            Log.LogInfo("BetterSolarSystem " + PluginVersion + " enabled; balance preset=" + BalancePresets.Current + ", defense=" + DefenseBalance.Enabled + ".");
        }

        private void Patch(Type type, string name, string prefix = null, string postfix = null)
        {
            var method = AccessTools.Method(type, name);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix));
        }
        private void Update() { DefenseBalance.Tick(); }
        private void OnDestroy() { harmony?.UnpatchSelf(); travelHarmony?.UnpatchSelf(); defenseHarmony?.UnpatchSelf(); DefenseBalance.Shutdown(); }
    }

    internal static class Patches
    {
        internal static void GeneratePrefix(GS2Generator2 __instance, int starCount,
            Dictionary<GSStar, List<GS2Generator2.Orbit>> ___starOrbits)
        {
            ___starOrbits.Clear();
            BalancedGeneration.Configure(__instance, starCount);
        }
        internal static void StarsPostfix(GSStar ___birthStar)
        {
            BalancedGeneration.PrepareStars(___birthStar);
        }
        internal static bool SystemPrefix(GSStar star, GSStar ___birthStar, ref GSPlanet ___birthPlanet,
            Dictionary<GSStar, List<GS2Generator2.Orbit>> ___starOrbits)
        {
            return !BalancedGeneration.BuildSystem(star, star == ___birthStar, ref ___birthPlanet, ___starOrbits);
        }
        internal static bool NamePrefix(GSStar star) { return !star.genData.Get("balancedBuilt").Bool(false); }
        internal static bool SpecialVeinsPrefix() { return !BalancedGeneration.IsManagedGalaxy; }
        internal static void GeneratePostfix()
        {
            BalancedGeneration.Finish();
            Plugin.Log.LogInfo("[BALANCED] Validated seed=" + GSSettings.Seed + " stars=" + GSSettings.StarCount + " bodies=" + GSSettings.PlanetCount + " birth=" + GSSettings.BirthPlanetName);
        }
        internal static void VeinGenPostfix(GSPlanet gsPlanet)
        {
            var raw = gsPlanet?.planetData?.data;
            int minGroup = int.MaxValue, maxGroup = 0; double maxOil = 0;
            var groups = gsPlanet?.planetData?.veinGroups;
            if (groups != null) foreach (var group in groups)
                if (group.type != EVeinType.Oil && group.count > 0)
                { minGroup = Math.Min(minGroup, group.count); maxGroup = Math.Max(maxGroup, group.count); }
            if (raw?.veinPool != null) for (int i = 1; i < Math.Min(raw.veinCursor, raw.veinPool.Length); i++)
                if (raw.veinPool[i].id == i && raw.veinPool[i].type == EVeinType.Oil)
                    maxOil = Math.Max(maxOil, raw.veinPool[i].amount * (double)VeinData.oilSpeedMultiplier);
            Plugin.Log.LogInfo("[VEIN] DONE '" + gsPlanet?.Name + "' veinCursor=" + (raw?.veinCursor ?? -1) + " poolLen=" + (raw?.veinPool?.Length ?? -1) +
                " solidGroupNodes=" + (maxGroup == 0 ? 0 : minGroup) + "-" + maxGroup + " maxOilPerSec=" + maxOil.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        }
        internal static void ImportPostfix(bool __result)
        {
            if (!__result || GS2.SaveOrLoadWindowOpen || !GSSettings.Instance.imported || GS2.Vanilla) return;
            if (BalancedGeneration.UpgradeSun()) Plugin.Log.LogInfo("[UPGRADE] Sun radius=6, physicsRadius=7200; planetary orbits preserved.");
            if (!Plugin.SanitizeLegacySaves.Value) return;
            int changed = LegacyVeins.Repair(Plugin.VeinBaseRadius.Value, Plugin.VeinExponent.Value, Plugin.ScaleRareVeins.Value);
            if (changed > 0) Plugin.Log.LogInfo("[LEGACY] One-time repair: " + changed + " tables; saved geometry preserved.");
        }
        internal static void VeinAmountPrefix(ref int amount, EVeinType veinType, short groupIndex, PlanetData planet)
        {
            if (veinType == EVeinType.Oil && BalancedGeneration.IsManagedGalaxy)
                amount = BalancedGeneration.OilAmount(planet.seed, groupIndex, VeinData.oilSpeedMultiplier);
        }
        internal static void GameImportPostfix(GameData __instance)
        {
            if (!BalancedGeneration.IsManagedGalaxy) return;
            if (__instance.galaxy?.stars != null)
                foreach (var star in __instance.galaxy.stars)
                    if (star?.planets != null) foreach (var planet in star.planets)
                        if (planet != null) RuntimeNames.Restore(planet, GS2.GetGSPlanet(planet)?.Name);
            if (!Plugin.SanitizeLegacySaves.Value) return;
            int changed = RuntimeVeins.RepairLoadedOil(__instance);
            if (changed > 0) Plugin.Log.LogInfo("[OIL] Repaired " + changed + " excessive saved springs; deposits, buildings and miners preserved.");
        }
        internal static bool PlanetNamePrefix(PlanetData __instance, bool notifychange)
        {
            if (!BalancedGeneration.IsManagedGalaxy || !RuntimeNames.Restore(__instance, GS2.GetGSPlanet(__instance)?.Name)) return true;
            if (notifychange && string.IsNullOrEmpty(__instance.overrideName))
                __instance.galaxy?.NotifyAstroNameChange(__instance.astroId);
            return false;
        }
    }
}
