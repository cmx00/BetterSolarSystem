using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using BetterSolarSystem.Balance;

public static class DefenseRewriteChecks
{
    private static readonly BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static string Geometry()
    {
        var random = new System.Random(731);
        var source = new List<EnemyCandidateIndex.Candidate>();
        for (int i = 1; i <= 6000; i++) source.Add(new EnemyCandidateIndex.Candidate { Id = i, Origin = i % 11, Model = i, Prototype = 1,
            X = random.NextDouble() * 100000 - 50000, Y = random.NextDouble() * 100000 - 50000, Z = random.NextDouble() * 100000 - 50000 });
        var timer = Stopwatch.StartNew();
        var index = new EnemyCandidateIndex(source);
        long build = timer.ElapsedMilliseconds;
        int nativeCandidates = 0;
        timer.Restart();
        for (int query = 0; query < 250; query++)
        {
            double x = random.NextDouble() * 20000 - 10000, y = random.NextDouble() * 20000 - 10000, z = random.NextDouble() * 20000 - 10000;
            var actual = index.Nearest(x, y, z, 26000, point => { nativeCandidates++; return point.Id % 7 != 0; });
            int expected = 0; double nearest = 26000d * 26000;
            foreach (var point in source)
            {
                double dx = point.X - x, dy = point.Y - y, dz = point.Z - z, distance = dx * dx + dy * dy + dz * dz;
                if (distance <= nearest && point.Id % 7 != 0) { nearest = distance; expected = point.Id; }
            }
            Require(actual.GetValueOrDefault().Id == expected, "KD result differs from exhaustive nearest valid search");
        }
        long queryTime = timer.ElapsedMilliseconds;
        Require(nativeCandidates < 250 * 6000 / 20, "Query failed to reduce candidate checks");
        Require(!index.Nearest(1000000, 1000000, 1000000, 1, point => true).HasValue, "Out of range selection");
        var pair = new List<EnemyCandidateIndex.Candidate> { new EnemyCandidateIndex.Candidate { Id = 1, X = 1 }, new EnemyCandidateIndex.Candidate { Id = 2, X = 2 } };
        Require(new EnemyCandidateIndex(pair).Nearest(0, 0, 0, 3, point => point.Id != 1).Value.Id == 2, "Rejected nearest candidate must not hide next candidate");
        Require(!EnemyCandidateIndex.Matches(source[0], 0, source[0].Origin, source[0].Model, 1), "Destroyed ID accepted");
        Require(!EnemyCandidateIndex.Matches(source[0], 1, source[0].Origin, source[0].Model + 1, 1), "Reused pool ID accepted");
        return "6000 candidates; 250 queries match exhaustive search; build " + build + " ms; query+oracle " + queryTime + " ms; predicate calls " + nativeCandidates;
    }
    private static T Empty<T>() where T : class => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static ConfigEntry<T> Setting<T>(string name) => (ConfigEntry<T>)typeof(DefenseBalance).GetField(name, Hidden).GetValue(null);
    private static void Invoke(string name, params object[] args) => typeof(DefenseBalance).GetMethod(name, Hidden).Invoke(null, args);
    private static EnemyData SpaceEnemy(int id, double x, double y)
    {
        return new EnemyData { id = id, protoId = 1, modelId = id + 100, astroId = 0, originAstroId = 0, isSpace = true, pos = new VectorLF3(x, y, 0) };
    }
    public static string Native(string scratch)
    {
        Directory.CreateDirectory(scratch);
        typeof(Paths).GetProperty("ConfigPath").GetSetMethod(true).Invoke(null, new object[] { scratch });
        var legacy = new ConfigFile(Path.Combine(scratch, "com.myself.globalsignaltower.cfg"), false) { SaveOnConfigSet = false };
        legacy.Bind("1. 范围增强", "全局范围倍率(其他炮塔/信号塔)", 5f);
        legacy.Bind("1. 范围增强", "电浆炮对天射程倍率", 3f);
        legacy.Bind("1. 范围增强", "导弹塔额外对天倍率", 2f);
        legacy.Bind("2. 战场基站", "拾取范围", 650f);
        legacy.Bind("2. 战场基站", "建造范围", 550f);
        legacy.Save();
        var config = new ConfigFile(Path.Combine(scratch, "new.cfg"), false) { SaveOnConfigSet = false };
        var harmony = new Harmony("cmx.private.defense.rewrite.checks");
        try
        {
            DefenseBalance.Initialize(config, harmony);
            Require(Setting<float>("range").Value == 5f && Setting<float>("plasmaRange").Value == 3f && Setting<float>("missileRange").Value == 2f, "Old weapon config not inherited");
            Require(Setting<float>("pickRange").Value == 650f && Setting<float>("buildRange").Value == 550f, "Old station config not inherited");
            int patched = harmony.GetPatchedMethods().Count(); Require(patched == 12, "Native patch binding count: " + patched);
            int flightTicks = (int)typeof(DefenseBalance).GetMethod("FlightTicks", Hidden).Invoke(null, new object[] { 500000d, 80000d });
            Require(flightTicks >= 500000d / 80000d * 60d + 120, "Plasma expires before reaching extended range");
            var star = Empty<StarData>(); star.id = 1; star.planetCount = 1; star.uPosition = new VectorLF3(1000000, 0, 0);
            var planet = Empty<PlanetData>(); planet.id = 101; planet.star = star; planet.radius = 300; planet.scale = 1;
            var galaxy = Empty<GalaxyData>(); galaxy.stars = new[] { star }; galaxy.astrosFactory = new PlanetFactory[102];
            var sector = Empty<SpaceSector>(); sector.galaxy = galaxy; sector.galaxyAstros = new AstroData[102]; sector.astros = new AstroData[1];
            sector.galaxyAstros[100] = new AstroData { id = 100, uPos = star.uPosition, uRot = Quaternion.identity, uRadius = 1000 };
            sector.galaxyAstros[101] = new AstroData { id = 101, uPos = new VectorLF3(0, 0, 0), uRot = Quaternion.identity, uRadius = 300 };
            sector.enemyPool = new EnemyData[5]; sector.enemyCursor = 5;
            sector.enemyPool[1] = SpaceEnemy(1, 100, -1000); // closer, but behind the planet
            sector.enemyPool[2] = SpaceEnemy(2, 1000, 12000); // farther, valid extended-range target
            sector.enemyPool[3] = SpaceEnemy(3, 1000, 28000); // outside configured range
            var factory = Empty<PlanetFactory>();
            typeof(PlanetFactory).GetField("<planet>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(factory, planet);
            factory.sector = sector; factory.entityPool = new EntityData[3]; factory.entityPool[1] = new EntityData { id = 1, pos = new Vector3(0, 300, 0), rot = Quaternion.identity };
            factory.defenseSystem = Empty<DefenseSystem>(); factory.defenseSystem.globalTargets = new TimedSkillTarget[16];
            factory.enemyPool = new EnemyData[4]; factory.enemyCursor = 4;
            galaxy.astrosFactory[101] = factory;
            var descriptor = new PrefabDesc { turretSpaceAttackRange = 26000, turretMaxAttackRange = 26000, turretPitchUpMax = 89, turretPitchDownMax = 0 };
            var turret = new TurretComponent { entityId = 1, type = ETurretType.Plasma, vsCaps = VSLayerMask.SpaceHigh, vsSettings = VSLayerMask.SpaceHigh, muzzleWPos = new Vector3(0, 310, 0) };
            object[] args = { turret, factory, descriptor, 100L };
            Invoke("Search", args); turret = (TurretComponent)args[0];
            Require(turret.target.id == 2, "Native extended search failed to reject occluded nearest enemy and select farther valid target: " + turret.target.id);
            sector.enemyPool[2].id = 0; turret.target = default(SkillTarget); args[0] = turret; args[3] = 101L; Invoke("Search", args);
            Require(((TurretComponent)args[0]).target.id == 0, "Destroyed or out-of-range enemy selected from stale snapshot");
            sector.enemyPool[2] = SpaceEnemy(2, 1000, 12000); sector.enemyPool[2].modelId++; args[0] = turret; args[3] = 102L; Invoke("Search", args);
            Require(((TurretComponent)args[0]).target.id == 0, "Reused ID selected from stale snapshot");
            args[0] = turret; args[3] = 106L; Invoke("Search", args); Require(((TurretComponent)args[0]).target.id == 2, "New lifecycle not selected after snapshot refresh");
            Invoke("Retire", 2); args[0] = turret; args[3] = 107L; Invoke("Search", args);
            Require(((TurretComponent)args[0]).target.id == 0, "Identical model/prototype reused ID accepted despite lifecycle retirement");
            args[0] = turret; args[3] = 112L; Invoke("Search", args); Require(((TurretComponent)args[0]).target.id == 2, "Retired incarnation not recognized after refresh");
            turret.type = ETurretType.Missile; turret.target = default(SkillTarget); turret.vsSettings = VSLayerMask.GroundHigh;
            args[0] = turret; args[3] = 107L; Invoke("Search", args); Require(((TurretComponent)args[0]).target.id == 0, "Disabled space attack layer ignored");
            factory.enemyPool[1] = new EnemyData { id = 1, originAstroId = 101 };
            factory.enemyPool[2] = new EnemyData { id = 2, originAstroId = 101 };
            factory.defenseSystem.beacons = new DataPool<BeaconComponent>(); factory.defenseSystem.beacons.buffer = new BeaconComponent[3]; factory.defenseSystem.beacons.cursor = 3;
            Invoke("GroundSignals", factory, 200L); Require(factory.defenseSystem.localGlobalTargetCursor == 0, "Signals registered without a signal tower");
            factory.defenseSystem.beacons.buffer[1] = new BeaconComponent { id = 1, entityId = 1 };
            Invoke("GroundSignals", factory, 200L); Require(factory.defenseSystem.localGlobalTargetCursor == 2, "Ground signals not registered");
            Invoke("GroundSignals", factory, 206L); Require(factory.defenseSystem.localGlobalTargetCursor == 2, "Duplicate ground signals appended");
            return "12 actual Harmony bindings; inherited old config; native extended range/occlusion/layers; destroyed/reused/identical-model IDs; refresh; ground signal presence and deduplication passed";
        }
        finally { harmony.UnpatchSelf(); DefenseBalance.Shutdown(); }
    }
}
