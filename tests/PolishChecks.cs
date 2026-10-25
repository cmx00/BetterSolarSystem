using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using GalacticScale;
using UnityEngine;
using BetterSolarSystem;
using BetterSolarSystem.Balance;

public static class PolishChecks
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string scratch)
    {
        Directory.CreateDirectory(scratch);
        typeof(Paths).GetProperty("ConfigPath").GetSetMethod(true).Invoke(null, new object[] { scratch });
        var fresh = new ConfigFile(Path.Combine(scratch, "fresh.cfg"), false) { SaveOnConfigSet = false };
        BalancePresets.Configure(fresh);
        Check(BalancePresets.Current == BalancePreset.Recommended && BalancePresets.Ground(4) == 1.5f &&
            BalancePresets.Space(4) == 4f && BalancePresets.Signal(4) == 1.25f, "Recommended ground/space ranges differ from 1.5x/4x.");
        fresh.Bind("Balance / 平衡", "Preset / 预设", BalancePreset.Custom).Value = BalancePreset.GalaxyOnly;
        Check(!BalancePresets.DefenseEnabled(true) && !BalancePresets.TravelEnabled(true) && BalancePresets.Travel(4, 2) == 1f && BalancePresets.Ground(4) == 1f && BalancePresets.Space(4) == 1f,
            "Generation-only preset still changes combat/travel.");
        fresh.Bind("Balance / 平衡", "Preset / 预设", BalancePreset.Custom).Value = BalancePreset.EnhancedDefense;
        Check(BalancePresets.Ground(4) == 1.5f && BalancePresets.Space(1) == 4, "Enhanced preset changes the intended ground/space ranges.");
        string oldPath = Path.Combine(scratch, "old.cfg");
        File.WriteAllText(oldPath, "[Balance - Modules / 平衡模块]\nDefense / 防御增强 = true\n");
        var old = new ConfigFile(oldPath, false) { SaveOnConfigSet = false };
        BalancePresets.Configure(old);
        Check(BalancePresets.Current == BalancePreset.Custom && BalancePresets.Ground(5) == 5 && BalancePresets.Space(3) == 3,
            "Existing settings were replaced by a preset.");

        var safety = typeof(DefenseBalance).Assembly.GetType("BetterSolarSystem.Balance.GroundFireSafety");
        var above = safety.GetMethod("AboveSurface", Hidden);
        Func<Vector3, Vector3, float, Func<Vector3, float>, bool> line = (a,b,r,h) => (bool)above.Invoke(null, new object[] { a,b,r,h });
        Check(line(new Vector3(0,210,0), new Vector3(40,210,0), 200, null), "Clear above-surface shot rejected.");
        Check(!line(new Vector3(-180,100,0), new Vector3(180,100,0), 198, null), "Shot through the planet accepted.");
        Check(!line(new Vector3(-40,205,0), new Vector3(40,205,0), 198, p => Math.Abs(p.x) < 10 ? 210 : 200),
            "Terrain obstruction accepted.");
        var blocks = safety.GetMethod("Blocks", Hidden);
        var box = new ColliderData { idType = (int)EColliderShape.Box << 24, pos = new Vector3(5,0,0), ext = Vector3.one, q = Quaternion.identity };
        Check((bool)blocks.Invoke(null, new object[] { box, Vector3.zero, Vector3.right, 10f }), "Visible box obstruction missed.");
        Check(!(bool)blocks.Invoke(null, new object[] { box, Vector3.zero, Vector3.right, 2f }), "Obstruction beyond the target blocks the shot.");
        var sphere = new ColliderData { idType = (int)EColliderShape.Sphere << 24, pos = new Vector3(5,0,0), radius = 1f };
        Check((bool)blocks.Invoke(null, new object[] { sphere, Vector3.zero, Vector3.right, 10f }), "Visible spherical obstruction missed.");
        var descriptor = new PrefabDesc { turretMaxAttackRange = 176 };
        var ranges = typeof(DefenseBalance).GetField("nativeGroundRanges", Hidden).GetValue(null);
        ranges.GetType().GetMethod("TryAdd").Invoke(ranges, new object[] { descriptor, 44f });
        var lifetime = typeof(DefenseBalance).GetMethod("GroundProjectileLife", Hidden);
        Check((int)lifetime.Invoke(null, new object[] { 20, descriptor }) == 80, "Extended gauss range outlives its projectile.");
        descriptor.turretMaxAttackRange = 44;
        Check((int)lifetime.Invoke(null, new object[] { 20, descriptor }) == 20, "Native gauss lifetime changed.");

        for (int seed = 1; seed <= 8; seed++)
        {
            var home = Body("木卫二", 300, seed);
            home.genData.Set("birthPlanet", true);
            Add(home, EVeinType.Iron, 6); Add(home, EVeinType.Copper, 4); Add(home, EVeinType.Coal, 4); Add(home, EVeinType.Stone, 4); Add(home, EVeinType.Oil, 6);
            ApplyRole(home, true); BalancedGeneration.NormalizeResources(home);
            Check(!home.veinSettings.VeinTypes.Any(v => v.type == EVeinType.Crysrub), "Starter still contains the advanced resource jackpot.");
            Check(home.veinSettings.VeinTypes.First(v => v.type == EVeinType.Silicium).veins.Count == 2 &&
                home.veinSettings.VeinTypes.First(v => v.type == EVeinType.Titanium).veins.Count == 2, "Starter samples did not obey radius-300 doubling.");
            Check(BalancedGeneration.Nodes(home) <= BalancedGeneration.NodeBudget(home), "Starter resource budget exceeded.");
            foreach (var kind in home.veinSettings.VeinTypes)
            {
                Check(kind.type == EVeinType.Oil || kind.veins.Count % 2 == 0, "A solid resource missed radius-300 doubling.");
                foreach (var vein in kind.veins) Check(kind.type == EVeinType.Oil ? vein.count == 1 : vein.count >= 16 && vein.count <= 24,
                    "Natural deposit sizes changed.");
            }
            var callisto = Body("木卫四", 200, seed); ApplyRole(callisto, false); BalancedGeneration.NormalizeResources(callisto);
            Check(callisto.veinSettings.VeinTypes.Any(v => v.type == EVeinType.Fractal) && callisto.veinSettings.VeinTypes.First(v => v.type == EVeinType.Silicium).veins.Count == 3,
                "Mid-game moon lost its silicon/titanium/fractal role.");
            var ganymede = Body("木卫三", 200, seed); ApplyRole(ganymede, false); BalancedGeneration.NormalizeResources(ganymede);
            Check(ganymede.veinSettings.VeinTypes.Any(v => v.type == EVeinType.Fireice) && ganymede.veinSettings.VeinTypes.Any(v => v.type == EVeinType.Grat),
                "Ganymede lost its ice/gas resource role.");
            double oil = BalancedGeneration.OilAmount(seed, 2, .00004f) * .00004d;
            Check(oil >= 1.4999 && oil <= 6.0001, "Oil flow leaves the independent spring limits.");
        }
        Console.WriteLine("PASS: fresh/legacy presets, separate ground/space/signal ranges, ground curvature/terrain, gauss lifetime, 8 resource fixtures, radius-300 doubling and oil limits.");
    }
    private static void ApplyRole(GSPlanet body, bool home) => typeof(BalancedGeneration).GetMethod("ApplySolarResourceRoles", Hidden).Invoke(null, new object[] { body, home });
    private static GSPlanet Body(string name, int radius, int seed) => new GSPlanet { Name = name, Radius = radius, Seed = seed, Scale = 1, veinSettings = new GSVeinSettings() };
    private static void Add(GSPlanet body, EVeinType type, int groups)
    {
        var v = new GSVeinType(type) { rare = false };
        for (int i = 0; i < groups; i++) v.veins.Add(new GSVein(type == EVeinType.Oil ? 1 : 20, .8f));
        body.veinSettings.VeinTypes.Add(v);
    }
}
