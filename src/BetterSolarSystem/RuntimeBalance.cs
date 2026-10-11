using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using BepInEx.Configuration;

namespace BetterSolarSystem
{
    public static class RuntimeBalance
    {
        private static ConfigEntry<bool> fogEnabled, sailsEnabled;
        private static ConfigEntry<float> fleetMultiplier, carrierMultiplier, sailMultiplier;
        private static ConfigEntry<int> cargoMultiplier;
        public static void Configure(ConfigFile config)
        {
            const string modules = "Balance - Modules / 平衡模块";
            const string travel = "Balance - Travel / 航行";
            fogEnabled = config.Bind(modules, "Dark Fog / 黑雾航行", true, "Restart required. / 修改后重启。");
            sailsEnabled = config.Bind(modules, "Solar sails / 太阳帆寿命", true, "Restart required. / 修改后重启。");
            fleetMultiplier = config.Bind(travel, "Fleet speed multiplier / 舰队速度倍率", 2f, new ConfigDescription("Stacks with the current modded speed. / 在现有模组速度上叠加。", new AcceptableValueRange<float>(0.25f, 10f)));
            carrierMultiplier = config.Bind(travel, "Carrier speed multiplier / 补给速度倍率", 2f, new ConfigDescription("", new AcceptableValueRange<float>(0.25f, 10f)));
            cargoMultiplier = config.Bind(travel, "Carrier cargo multiplier / 补给容量倍率", 2, new ConfigDescription("Load, source debit and dispatch threshold use the same multiplier. / 装载、扣款与门槛同步。", new AcceptableValueRange<int>(1, 10)));
            sailMultiplier = config.Bind(travel, "Sail lifetime multiplier / 帆寿命倍率", 4f, new ConfigDescription("Newly emitted sails; existing expiry timestamps are preserved. / 新发射生效。", new AcceptableValueRange<float>(1f, 10f)));
        }
        public static float FleetMultiplier() => fleetMultiplier?.Value ?? 2f;
        public static float CarrierLimit() => 1800f * (carrierMultiplier?.Value ?? 2f);
        public static int MatterCapacity() => 6000 * (cargoMultiplier?.Value ?? 2);
        public static int EnergyCapacity() => 5000000 * (cargoMultiplier?.Value ?? 2);
        public static float SailMultiplier() => sailMultiplier?.Value ?? 4f;
        public static void Apply(Harmony harmony)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            if (fogEnabled == null || fogEnabled.Value) {
            foreach (var method in typeof(EnemyUnitComponent).GetMethods(flags))
                if ((method.Name.Contains("_Space") || method.Name.Contains("_SLancer") || method.Name.Contains("_SHumpback") || method.Name.Contains("_SFortress")) &&
                    PatchProcessor.GetOriginalInstructions(method).Any(IsMovementField))
                    harmony.Patch(method, transpiler: new HarmonyMethod(typeof(RuntimeBalance), nameof(FleetSpeed)));
            foreach (string name in new[] { "CarrierSailLogic", "CarrierSailLogicVirtual", "SetCarrierRealized" })
                harmony.Patch(AccessTools.Method(typeof(DFRelayComponent), name), transpiler: new HarmonyMethod(typeof(RuntimeBalance), nameof(CarrierSpeed)));
            harmony.Patch(AccessTools.Method(typeof(DFRelayComponent), "LogicTickLogisticsOrders"), transpiler: new HarmonyMethod(typeof(RuntimeBalance), nameof(CarrierCargo)));
            }
            // Change consumers, never the serialized history field. Load/save and
            // technology upgrades cannot compound the multiplier. Existing sail
            // expiry timestamps remain intact; newly emitted sails use 4x life.
            if (sailsEnabled == null || sailsEnabled.Value) foreach (var type in new[] { typeof(DysonSwarm), typeof(DysonSphereLayer), typeof(PlayerMove_Sail), typeof(ItemProto), typeof(UIDESphereInfo), typeof(UIDESwarmOrbitInfo), typeof(UITechTree), typeof(UIStatisticDysonSphereGraph) })
                foreach (var method in type.GetMethods(flags | BindingFlags.DeclaredOnly))
                    if (method.GetMethodBody() != null && ReadsSailLife(method))
                        harmony.Patch(method, transpiler: new HarmonyMethod(typeof(RuntimeBalance), nameof(SailLife)));
        }

        private static bool ReadsSailLife(MethodInfo method)
        {
            var field = AccessTools.Field(typeof(GameHistoryData), "solarSailLife");
            return PatchProcessor.GetOriginalInstructions(method).Any(c => c.LoadsField(field));
        }

        public static IEnumerable<CodeInstruction> FleetSpeed(IEnumerable<CodeInstruction> source)
        {
            foreach (var code in source)
            {
                yield return code;
                if (IsMovementField(code))
                { yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RuntimeBalance), nameof(FleetMultiplier))); yield return new CodeInstruction(OpCodes.Mul); }
            }
        }

        public static bool IsMovementField(CodeInstruction code) => code.opcode == OpCodes.Ldfld && code.operand is FieldInfo f && f.DeclaringType == typeof(PrefabDesc) &&
            (f.Name == "unitMarchMovementSpeed" || f.Name == "unitMaxMovementSpeed" || f.Name == "unitMaxMovementAcceleration");

        public static IEnumerable<CodeInstruction> CarrierSpeed(IEnumerable<CodeInstruction> source)
        {
            int hits = 0;
            foreach (var code in source)
            {
                if (code.opcode == OpCodes.Ldc_R4 && (float)code.operand == 1800f)
                { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(RuntimeBalance), nameof(CarrierLimit)); hits++; }
                yield return code;
            }
            if (hits == 0) throw new InvalidOperationException("Carrier cruise-speed constant changed.");
        }

        public static IEnumerable<CodeInstruction> CarrierCargo(IEnumerable<CodeInstruction> source)
        {
            int matter = 0, energy = 0;
            foreach (var code in source)
            {
                if (code.LoadsConstant(6000)) { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(RuntimeBalance), nameof(MatterCapacity)); matter++; }
                else if (code.LoadsConstant(5000000)) { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(RuntimeBalance), nameof(EnergyCapacity)); energy++; }
                yield return code;
            }
            if (matter != 6 || energy != 4) throw new InvalidOperationException("Carrier cargo/debit contract changed: " + matter + "/" + energy);
        }

        public static IEnumerable<CodeInstruction> SailLife(IEnumerable<CodeInstruction> source)
        {
            var field = AccessTools.Field(typeof(GameHistoryData), "solarSailLife");
            foreach (var code in source)
            {
                yield return code;
                if (code.LoadsField(field)) { yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RuntimeBalance), nameof(SailMultiplier))); yield return new CodeInstruction(OpCodes.Mul); }
            }
        }
    }
}
