using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;

namespace BetterSolarSystem.Balance
{
    public static class DefenseBalance
    {
        private static ConfigEntry<float> range, spaceRange, signalRange, plasmaRange, missileRange, pickRange, buildRange;
        private static readonly ConcurrentDictionary<PrefabDesc, float> nativeGroundRanges = new ConcurrentDictionary<PrefabDesc, float>();
        private static ConfigEntry<bool> signals, plasma;
        private static volatile float effectivePlasmaReach = 26000f;
        private static readonly ConcurrentDictionary<int, PlanetState> planets = new ConcurrentDictionary<int, PlanetState>();
        private static readonly ConcurrentDictionary<int, StarState> stars = new ConcurrentDictionary<int, StarState>();
        private static readonly ConcurrentDictionary<int, int> incarnations = new ConcurrentDictionary<int, int>();
        private static readonly Dictionary<int, Baseline> baselines = new Dictionary<int, Baseline>();
        public static bool Enabled { get; private set; }
        private sealed class PlanetState
        {
            internal readonly PlanetFactory Factory;
            internal readonly object Gate = new object();
            internal readonly Dictionary<int, int> SignalSlots = new Dictionary<int, int>();
            internal long SignalTick = -1;
            internal PlanetState(PlanetFactory factory) { Factory = factory; }
        }
        private sealed class StarState
        {
            internal readonly object Gate = new object();
            internal long Tick = -1;
            internal SpaceSector Sector;
            internal EnemyCandidateIndex Index;
        }
        private sealed class Baseline
        {
            internal PrefabDesc Descriptor;
            internal float Ground, Space, Signal, Pick, Build;
        }

        public static void Initialize(ConfigFile config, Harmony harmony)
        {
            range = ConfigMigration.BindDefense(config, "1. 范围增强", "全局范围倍率(其他炮塔/信号塔)", 1.5f,
                "Ground range in Custom. Historical key retained; space and signal settings below are independent. / Custom 地面射程，保留旧键名；对空和信号范围在下方独立设置。");
            spaceRange = config.Bind("Balance - Defense / 防御 - 1. 范围增强", "Space range multiplier / 对空射程倍率", BalancePresets.Current == BalancePreset.Custom ? range.Value : 4f, RangeDescription());
            signalRange = config.Bind("Balance - Defense / 防御 - 1. 范围增强", "Signal radius multiplier / 信号覆盖倍率", range.Value, RangeDescription());
            plasmaRange = ConfigMigration.BindDefense(config, "1. 范围增强", "电浆炮对天射程倍率", 4f, RangeDescription());
            missileRange = ConfigMigration.BindDefense(config, "1. 范围增强", "导弹塔额外对天倍率", 1f, RangeDescription());
            pickRange = ConfigMigration.BindDefense(config, "2. 战场基站", "拾取范围", 110f, new ConfigDescription("", new AcceptableValueRange<float>(100f, 700f)));
            buildRange = ConfigMigration.BindDefense(config, "2. 战场基站", "建造范围", 110f, new ConfigDescription("", new AcceptableValueRange<float>(50f, 700f)));
            signals = ConfigMigration.BindDefense(config, "3. 开关", "启用全局标记", true, "Registers ground targets only on planets with a signal tower.");
            plasma = ConfigMigration.BindDefense(config, "3. 开关", "启用电浆炮增强", true, "Enables extended plasma targeting.");
            ConfigMigration.BindDefense(config, "4. 性能优化", "标记逻辑使用内层锁", false, "Legacy key retained; each planet cache now owns its synchronization.");
            Enabled = BalancePresets.DefenseEnabled(config.Bind("Balance - Modules / 平衡模块", "Defense / 防御增强", true, "Custom preset; restart required. / Custom 预设下生效，修改后重启。").Value);
            foreach (var entry in new[] { range, spaceRange, signalRange, plasmaRange, missileRange, pickRange, buildRange }) entry.SettingChanged += (sender, args) => ApplyRanges();
            if (!Enabled) return;
            foreach (var type in new[] { typeof(LoadRanges), typeof(ResetSession), typeof(RefreshGroundSignals), typeof(SupplementSearch), typeof(ScaledPlanetRadius), typeof(PlasmaFlight), typeof(RetiredEnemy) })
                harmony.CreateClassProcessor(type).Patch();
            GroundFireSafety.Apply(harmony);
        }
        private static ConfigDescription RangeDescription() => new ConfigDescription("Applied from the original prototype value.", new AcceptableValueRange<float>(1f, 10f));
        public static void Shutdown() { Enabled = false; planets.Clear(); stars.Clear(); incarnations.Clear(); baselines.Clear(); nativeGroundRanges.Clear(); }
        public static void Tick() { } // Simulation work follows game ticks, including remote factories.
        private static PlanetState State(PlanetFactory factory)
        {
            var state = planets.GetOrAdd(factory.planetId, id => new PlanetState(factory));
            if (!ReferenceEquals(state.Factory, factory)) { state = new PlanetState(factory); planets[factory.planetId] = state; }
            return state;
        }
        private static void ApplyRanges()
        {
            if (!Enabled || LDB.items?.dataArray == null) return;
            float furthestPlasma = 0f;
            foreach (var item in LDB.items.dataArray)
            {
                var descriptor = item?.prefabDesc;
                if (descriptor == null || (!descriptor.isTurret && !descriptor.isBeacon && !descriptor.isBattleBase)) continue;
                Baseline original;
                if (!baselines.TryGetValue(item.ID, out original) || !ReferenceEquals(original.Descriptor, descriptor))
                {
                    original = new Baseline { Descriptor = descriptor, Ground = descriptor.turretMaxAttackRange,
                        Space = descriptor.turretSpaceAttackRange, Signal = descriptor.beaconSignalRadius,
                        Pick = descriptor.battleBasePickRange, Build = descriptor.constructionRange };
                    baselines[item.ID] = original;
                }
                if (descriptor.isTurret)
                {
                    bool isPlasma = descriptor.turretType == ETurretType.Plasma;
                    nativeGroundRanges[descriptor] = original.Ground;
                    descriptor.turretMaxAttackRange = isPlasma ? original.Space * BalancePresets.Space(plasmaRange.Value) : original.Ground * BalancePresets.Ground(range.Value);
                    descriptor.turretSpaceAttackRange = original.Space * (isPlasma ? BalancePresets.Space(plasmaRange.Value) : BalancePresets.Space(spaceRange.Value) * (descriptor.turretType == ETurretType.Missile && BalancePresets.Current == BalancePreset.Custom ? missileRange.Value : 1f));
                    if (isPlasma) { descriptor.turretPitchUpMax = 89f; descriptor.turretPitchDownMax = 0f; furthestPlasma = Math.Max(furthestPlasma, descriptor.turretSpaceAttackRange); }
                }
                if (descriptor.isBeacon) descriptor.beaconSignalRadius = original.Signal * BalancePresets.Signal(signalRange.Value);
                if (descriptor.isBattleBase) { descriptor.battleBasePickRange = BalancePresets.Station(pickRange.Value); descriptor.constructionRange = BalancePresets.Station(buildRange.Value); }
            }
            effectivePlasmaReach = furthestPlasma > 0 ? furthestPlasma : 6500f * plasmaRange.Value;
            var factories = GameMain.data?.factories;
            if (factories != null) foreach (var factory in factories)
            {
                if (factory?.defenseSystem == null) continue;
                factory.defenseSystem.RefreshTurretSearchPair();
                var bases = factory.defenseSystem.battleBases;
                if (bases?.buffer == null) continue;
                for (int i = 1; i < Math.Min(bases.cursor, bases.buffer.Length); i++)
                {
                    var station = bases.buffer[i];
                    if (station == null || station.id != i) continue;
                    station.pickRange = BalancePresets.Station(pickRange.Value); station.constructRange = BalancePresets.Station(buildRange.Value);
                }
            }
        }
        private static EnemyCandidateIndex Snapshot(PlanetFactory factory, long tick)
        {
            var star = factory.planet.star;
            var state = stars.GetOrAdd(star.id, id => new StarState());
            lock (state.Gate)
            {
                if (state.Index != null && ReferenceEquals(state.Sector, factory.sector) && tick >= state.Tick && tick - state.Tick < 6) return state.Index;
                var source = new List<EnemyCandidateIndex.Candidate>();
                var sector = factory.sector;
                if (sector?.enemyPool != null) for (int i = 1; i < Math.Min(sector.enemyCursor, sector.enemyPool.Length); i++)
                {
                    ref var enemy = ref sector.enemyPool[i];
                    if (enemy.id != i || !enemy.isSpace || enemy.isInvincible || !enemy.positionIsValid) continue;
                    var hive = sector.IsHiveAstro(enemy.originAstroId) ? sector.GetHiveByAstroId(enemy.originAstroId) : null;
                    if (hive != null && hive.starData != star) continue;
                    VectorLF3 world;
                    sector.TransformFromAstro_ref(enemy.astroId, out world, ref enemy.pos);
                    if (!Finite(world.x) || !Finite(world.y) || !Finite(world.z)) continue;
                    if (hive == null && NearestStar(sector.galaxy, world) != star.id) continue;
                    source.Add(new EnemyCandidateIndex.Candidate { Id = i, Origin = enemy.originAstroId,
                        Model = enemy.modelId, Prototype = enemy.protoId, Revision = Incarnation(i),
                        X = world.x - star.uPosition.x, Y = world.y - star.uPosition.y, Z = world.z - star.uPosition.z });
                }
                state.Index = new EnemyCandidateIndex(source); state.Tick = tick; state.Sector = sector;
                return state.Index;
            }
        }
        private static int NearestStar(GalaxyData galaxy, VectorLF3 point)
        {
            double closest = double.MaxValue; int id = 0;
            foreach (var star in galaxy.stars)
            {
                double x = point.x - star.uPosition.x, y = point.y - star.uPosition.y, z = point.z - star.uPosition.z;
                double distance = x * x + y * y + z * z;
                if (distance < closest) { closest = distance; id = star.id; }
            }
            return id;
        }
        private static void Search(ref TurretComponent turret, PlanetFactory factory, PrefabDesc descriptor, long tick)
        {
            if (!Enabled || factory?.planet?.star == null || factory.sector == null || turret.target.id > 0 ||
                (turret.type == ETurretType.Plasma && !plasma.Value) || turret.entityId <= 0 || turret.entityId >= factory.entityPool.Length || descriptor.turretSpaceAttackRange <= 0f) return;
            ref var entity = ref factory.entityPool[turret.entityId];
            if (entity.id != turret.entityId) return;
            VectorLF3 position = entity.pos, world;
            factory.sector.TransformFromAstro_ref(factory.planet.astroId, out world, ref position);
            var star = factory.planet.star;
            var checkTurret = turret; var checkEntity = entity;
            var match = Snapshot(factory, tick).Nearest(world.x - star.uPosition.x, world.y - star.uPosition.y, world.z - star.uPosition.z,
                descriptor.turretSpaceAttackRange + 2000f, candidate =>
                {
                    var pool = factory.sector.enemyPool;
                    if (candidate.Id <= 0 || candidate.Id >= Math.Min(factory.sector.enemyCursor, pool.Length)) return false;
                    ref var enemy = ref pool[candidate.Id];
                    if (!EnemyCandidateIndex.Matches(candidate, enemy.id, enemy.originAstroId, enemy.modelId, enemy.protoId, Incarnation(candidate.Id)) ||
                        !enemy.isSpace || enemy.isInvincible || !enemy.positionIsValid) return false;
                    VectorLF3 current;
                    factory.sector.TransformFromAstro_ref(enemy.astroId, out current, ref enemy.pos);
                    double x = current.x - world.x, y = current.y - world.y, z = current.z - world.z;
                    double distance = x * x + y * y + z * z;
                    // Native global signals can bypass the distance check for missiles.
                    // Supplemental searches still require the actual configured range.
                    if (!Finite(distance) || distance > descriptor.turretSpaceAttackRange * (double)descriptor.turretSpaceAttackRange) return false;
                    var target = new SkillTarget { id = enemy.id, astroId = enemy.originAstroId, type = ETargetType.Enemy };
                    return checkTurret.CheckEnemyIsInAttackRange(factory, descriptor, ref checkEntity, ref target);
                });
            if (match.HasValue) turret.target = new SkillTarget { id = match.Value.Id, astroId = match.Value.Origin, type = ETargetType.Enemy };
        }
        private static void GroundSignals(PlanetFactory factory, long tick)
        {
            if (!Enabled || !BalancePresets.GlobalSignals(signals.Value) || factory?.defenseSystem == null || factory.enemyPool == null) return;
            var beacons = factory.defenseSystem.beacons;
            bool hasTower = false;
            if (beacons?.buffer != null) for (int i = 1; i < Math.Min(beacons.cursor, beacons.buffer.Length); i++)
            {
                var beacon = beacons.buffer[i];
                if (beacon.id == i && beacon.entityId > 0 && beacon.entityId < factory.entityPool.Length && factory.entityPool[beacon.entityId].id == beacon.entityId) { hasTower = true; break; }
            }
            if (!hasTower) return;
            var state = State(factory);
            lock (state.Gate)
            {
                if (tick >= state.SignalTick && tick - state.SignalTick < 6) return;
                state.SignalTick = tick;
                var defense = factory.defenseSystem;
                state.SignalSlots.Clear();
                int limit = Math.Min(defense.localGlobalTargetCursor, defense.globalTargets.Length);
                for (int slot = 0; slot < limit; slot++)
                {
                    var signal = defense.globalTargets[slot];
                    if (signal.type == ETargetType.Enemy && signal.astroId == factory.planet.astroId && signal.id > 0) state.SignalSlots[signal.id] = slot;
                }
                for (int id = 1; id < Math.Min(factory.enemyCursor, factory.enemyPool.Length); id++)
                {
                    ref var enemy = ref factory.enemyPool[id];
                    if (enemy.id != id || enemy.isSpace || enemy.isInvincible) continue;
                    int existing;
                    if (state.SignalSlots.TryGetValue(id, out existing)) { defense.globalTargets[existing].lifeTick = Math.Max(12, defense.globalTargets[existing].lifeTick); continue; }
                    var signal = new TimedSkillTarget { id = id, astroId = factory.planet.astroId, type = ETargetType.Enemy, lifeTick = 12 };
                    defense.AddGlobalTargets(ref signal);
                    state.SignalSlots[id] = defense.localGlobalTargetCursor - 1;
                }
            }
        }
        internal static float ActualRadius(PlanetFactory factory) => factory.planet.realRadius;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static int Incarnation(int id) { int value; return incarnations.TryGetValue(id, out value) ? value : 0; }
        private static void Retire(int id) { if (id > 0) incarnations.AddOrUpdate(id, 1, (key, prior) => unchecked(prior + 1)); }
        internal static double PlasmaSpeed() => 20000d * BalancePresets.Space(plasmaRange?.Value ?? 4f);
        internal static float NativeGroundRange(PrefabDesc descriptor)
        {
            float value;
            return descriptor != null && nativeGroundRanges.TryGetValue(descriptor, out value) ? value : descriptor?.turretMaxAttackRange ?? 0f;
        }
        internal static int GroundProjectileLife(int originalTicks, PrefabDesc descriptor)
        {
            float native = NativeGroundRange(descriptor);
            double factor = native > 0f ? Math.Max(1d, descriptor.turretMaxAttackRange / native) : 1d;
            return (int)Math.Min(int.MaxValue, Math.Ceiling(originalTicks * factor));
        }
        internal static int FlightTicks(double reach, double speed) => Math.Max(300, (int)Math.Ceiling(reach / speed * 60d) + 120);
        internal static int PlasmaLife() => FlightTicks(effectivePlasmaReach, PlasmaSpeed());
        [HarmonyPatch(typeof(VFPreload), "InvokeOnLoadWorkEnded")]
        private static class LoadRanges { private static void Postfix() { ApplyRanges(); } }
        [HarmonyPatch(typeof(GameMain), "Begin")]
        private static class ResetSession { private static void Postfix() { planets.Clear(); stars.Clear(); incarnations.Clear(); ApplyRanges(); } }
        [HarmonyPatch(typeof(SpaceSector), "RemoveEnemyWithComponents")]
        private static class RetiredEnemy
        {
            private static void Prefix([HarmonyArgument(0)] int id) { if (Enabled) Retire(id); }
        }
        [HarmonyPatch(typeof(CombatGroundSystem), "GameTick")]
        private static class RefreshGroundSignals
        {
            private static void Postfix(CombatGroundSystem __instance, [HarmonyArgument(0)] long tick) { GroundSignals(__instance.factory, tick); }
        }
        [HarmonyPatch]
        private static class SupplementSearch
        {
            private static IEnumerable<MethodBase> TargetMethods() { yield return AccessTools.Method(typeof(TurretComponent), "Search_Missile"); yield return AccessTools.Method(typeof(TurretComponent), "Search_Plasma"); }
            private static void Postfix(ref TurretComponent __instance, PlanetFactory factory, PrefabDesc pdesc, [HarmonyArgument(2)] long tick) { Search(ref __instance, factory, pdesc, tick); }
        }
        [HarmonyPatch(typeof(TurretComponent), "CheckEnemyIsInAttackRange")]
        private static class ScaledPlanetRadius
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
            {
                var code = new List<CodeInstruction>(input); int count = 0;
                for (int i = 0; i < code.Count; i++) if (code[i].opcode == OpCodes.Ldc_R4 && Equals(code[i].operand, 200f))
                {
                    code[i].opcode = OpCodes.Ldarg_1; code[i].operand = null;
                    code.Insert(++i, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DefenseBalance), nameof(ActualRadius)))); count++;
                }
                if (count != 1) throw new InvalidOperationException("Unsupported native turret radius API: " + count);
                return code;
            }
        }
        [HarmonyPatch(typeof(TurretComponent), "Shoot_Plasma")]
        private static class PlasmaFlight
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
            {
                var code = new List<CodeInstruction>(input); int speeds = 0, lives = 0;
                for (int i = 0; i < code.Count; i++)
                {
                    if (code[i].opcode == OpCodes.Ldc_R8 && Equals(code[i].operand, 20000d))
                    { code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(DefenseBalance), nameof(PlasmaSpeed)); speeds++; }
                    if (i > 0 && code[i].opcode == OpCodes.Stfld && code[i].operand is FieldInfo field && field.DeclaringType == typeof(GeneralProjectile) && (field.Name == "life" || field.Name == "lifemax"))
                    {
                        if (!code[i - 1].LoadsConstant(120)) throw new InvalidOperationException("Unsupported plasma lifetime API.");
                        code[i - 1].opcode = OpCodes.Call; code[i - 1].operand = AccessTools.Method(typeof(DefenseBalance), nameof(PlasmaLife)); lives++;
                    }
                }
                if (speeds != 2 || lives != 2) throw new InvalidOperationException("Unsupported native plasma API: " + speeds + "/" + lives);
                return code;
            }
        }
    }
}
