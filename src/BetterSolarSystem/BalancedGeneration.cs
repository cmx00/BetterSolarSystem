using System;
using System.Collections.Generic;
using System.Linq;
using GalacticScale;
using GalacticScale.Generators;
using UnityEngine;

namespace BetterSolarSystem
{
    // Compiled directly into BOTH the plugin and the simulator. No mirrored policy.
    // All bodies, sizes and orbit envelopes are built inside GeneratePlanetsForStar,
    // BEFORE GS2 names/phases/materializes PlanetData. Finish never changes geometry.
    public static class BalancedGeneration
    {
        public const string SaveMarker = "GS2Tweaks.Balanced.v2";
        public const string ResourceMarker = "GS2Tweaks.Resources.v2.2";
        public const string LayoutMarker = "GS2Tweaks.Layout.v2.3";
        public const float OrbitScale = 1.5f;
        public const string JupiterName = "木星";
        public const string IoName = "木卫一";
        public const string HomeName = "木卫二";
        public const string GanymedeName = "木卫三";
        public const string CallistoName = "木卫四";
        public const string SunMarker = "GS2Tweaks.Sun.v2.1";
        public static bool Enabled = true;
        public static readonly float[] SolarOrbits = { .387f, .723f, 1f, 1.524f, 5.203f, 9.537f, 19.191f, 30.069f };
        public static readonly string[] Profiles = { "温带多行星", "紧凑岩质", "外侧巨行星", "热木星", "海洋群岛", "巨星远轨", "遗迹资源" };
        private static readonly string[] StarLetters = { "K", "M", "G", "F", "A", "B", "O", "WD", "RG", "YG", "WG", "BG", "NS", "BH" };
        private static readonly int[] Frequencies = { 16, 16, 14, 12, 9, 7, 5, 5, 3, 2, 2, 3, 3, 3 };
        private static readonly float[][] Layouts = {
            new[] { .4f, .75f, 1f, 1.3f, 1.7f, 3.1f, 6f },
            new[] { .28f, .48f, .73f, 1f, 1.32f, 1.8f, 3f },
            new[] { .42f, .8f, 1.15f, 2.5f, 4.5f, 8f, 13f },
            new[] { .3f, .8f, 1.1f, 1.55f, 3f, 6.5f },
            new[] { .45f, .83f, 1.08f, 1.35f, 2f, 4.8f, 9f },
            new[] { 1f, 1.8f, 3f, 5.3f, 9f, 15f },
            new[] { .4f, .7f, 1.2f, 2.1f, 4f, 7.5f }
        };

        public static bool IsManagedGalaxy => GSSettings.ThemeLibrary != null && GSSettings.ThemeLibrary.ContainsKey(SaveMarker);

        public static void Configure(GS2Generator2 gen, int starCount)
        {
            // GS2's generated theme tables otherwise consume a process-global stream,
            // making identical seeds depend on previous previews and plugin boot order.
            GSVeinType.random = new GS2.Random(GSSettings.Seed);
            GSSettings.BirthIron = new GSVein(18, .1f);
            GSSettings.BirthCopper = new GSVein(18, .1f);
            // Enforce the requested contract even if GS2's UI resets Preferences.json.
            Action<string, object> set = (key, value) => gen.OnUpdate(key, new Val(value));
            set("birthStar", 3); set("birthPlanetSize", 300);
            set("birthPlanetMoon", true); set("birthPlanetGasMoon", true);
            set("birthPlanetUnlock", true); set("birthPlanetSiTi", false);
            set("birthRareDisable", false); set("noRaresStartingSystem", true);
            set("starSizeMulti", 2f); set("galaxyDensity", 11);
            set("luminosityBoost", 1f); set("binaryChance", 0);
            set("secondarySatellites", false); set("hugeGasGiants", true);
            set("cometsEnabled", false); // small bodies are included in the initial plan
            foreach (int size in new[] { 20, 50, 100, 200, 300, 400, 500 })
                set("limitPlanetSize" + size, size == 50 || size == 200 || size == 300);
            for (int i = 0; i < StarLetters.Length; i++)
            {
                string t = StarLetters[i];
                set("freq" + t, Frequencies[i]);
                // Leave ample room for the native forced-birth selection and RNG.
                int floor = starCount >= 32 ? Math.Max(1, starCount / 64) : 0;
                if (starCount >= 64 && (t == "BH" || t == "NS")) floor *= 2;
                if (starCount >= 64 && t == "O") floor *= 3;
                set(t + "minStars", floor);
                set(t + "luminosityBoost", 1f);
            }
        }

        public static void PrepareStars(GSStar birthStar)
        {
            // Allocate the five main-sequence architectures evenly, shuffle by seed.
            var rng = new GS2.Random(GSSettings.Seed ^ 0x53656374);
            var normal = GSSettings.Stars.Where(s => !s.Decorative && s != birthStar && s.Type == EStarType.MainSeqStar).ToList();
            for (int i = normal.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1); var swap = normal[i]; normal[i] = normal[j]; normal[j] = swap;
            }
            for (int i = 0; i < normal.Count; i++) normal[i].genData.Set("balancedProfile", i % 5);
            foreach (var star in GSSettings.Stars)
            {
                if (star.Type == EStarType.GiantStar) star.genData.Set("balancedProfile", 5);
                else if (star.Type != EStarType.MainSeqStar) star.genData.Set("balancedProfile", 6);
            }
            // Stellar radius is intentionally compressed for the playable orbit scale.
            // Planet-to-planet orbital ratios remain solar; this is not an N-body model.
            birthStar.Name = "太阳 · Sol";
            birthStar.radius = 6f; birthStar.physicsRadius = 7200f;
            birthStar.mass = 1f; birthStar.luminosity = 1f;
            birthStar.temperature = 5772f; birthStar.age = .46f; birthStar.dysonRadius = 1f;
            birthStar.genData.Set("balancedProfile", -1);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var star in GSSettings.Stars)
            {
                string original = star.Name; int suffix = 2;
                while (!names.Add(star.Name)) star.Name = original + " · " + suffix++;
            }
        }

        public static bool BuildSystem(GSStar star, bool birth, ref GSPlanet birthPlanet,
            Dictionary<GSStar, List<GS2Generator2.Orbit>> orbitCache)
        {
            if (!Enabled) return false;
            if (GSSettings.Instance.imported) throw new InvalidOperationException("Balanced generation cannot rebuild an imported save.");
            var rng = new GS2.Random(star.Seed ^ 0x506c616e);
            star.Planets = new GSPlanets();
            if (birth) BuildSolar(star, rng, ref birthPlanet);
            else BuildVaried(star, rng);
            // Transform only newly authored geometry, before the native orbit cache
            // and PlanetData exist. Never multiply loaded orbits.
            foreach (var planet in star.Planets) ExpandOrbits(planet);
            foreach (string key in new[] { "minOrbit", "maxOrbit", "minHZ", "maxHZ" })
                star.genData.Set(key, star.genData.Get(key).Float() * OrbitScale);
            star.MaxOrbit *= OrbitScale;
            var orbits = new List<GS2Generator2.Orbit>();
            foreach (var planet in star.Planets)
            {
                var orbit = new GS2Generator2.Orbit(planet.OrbitRadius);
                orbit.planets.Add(planet); orbits.Add(orbit);
            }
            // Keep GS2's phase cache consistent with the geometry on its FIRST creation.
            orbitCache[star] = orbits;
            star.genData.Set("balancedBuilt", true);
            return true;
        }

        private static void ExpandOrbits(GSPlanet body)
        {
            bool locked = Math.Abs(body.RotationPeriod - body.OrbitalPeriod) < .001;
            body.OrbitRadius *= OrbitScale;
            body.OrbitalPeriod = Utils.CalculateOrbitPeriod(body.OrbitRadius);
            if (locked) body.RotationPeriod = body.OrbitalPeriod;
            foreach (var moon in body.Moons) ExpandOrbits(moon);
        }

        private static GSPlanet Body(string name, string theme, int radius, bool gas, GS2.Random rng)
        {
            var body = new GSPlanet(name, theme, radius, 0f, 0f, 1f, rng.Next(360), 0f, 600f, rng.Next(360), 1f);
            body.Scale = gas ? 10f : 1f; body.Seed = rng.Next();
            body.OrbitLongitude = rng.Next(360);
            body.Obliquity = rng.NextFloat(0, 28);
            body.OrbitInclination = rng.NextFloat(0, 4);
            body.RotationPeriod = gas ? rng.Next(240, 720) : rng.Next(420, 1800);
            body.veinSettings = GSSettings.ThemeLibrary[theme].VeinSettings.Clone();
            return body;
        }

        private static void Place(GSPlanet body, float orbit, float solar)
        {
            body.OrbitRadius = orbit;
            body.OrbitalPeriod = Utils.CalculateOrbitPeriod(orbit);
            body.Luminosity = Mathf.Clamp(solar, .06f, 4f);
        }

        private static void AddMoon(GSPlanet host, string name, string theme, int radius, float orbit, GS2.Random rng)
        {
            var moon = Body(name, theme, radius, false, rng);
            Place(moon, orbit, 1f);
            moon.genData.Set("hosttype", "planet"); moon.genData.Set("hostname", host.Name);
            moon.OrbitInclination = rng.NextFloat(0, 8);
            moon.RotationPeriod = moon.OrbitalPeriod;
            host.Moons.Add(moon);
        }

        private static void BuildSolar(GSStar star, GS2.Random rng, ref GSPlanet birthPlanet)
        {
            string[] names = { "水星", "金星", "地球", "火星", JupiterName, "土星", "天王星", "海王星" };
            string[] themes = { "Barren", "VolcanicAsh", "Mediterranean", "Gobi", "GasGiant", "GasGiant2", "IceGiant", "IceGiant2" };
            // Internal gas radii are multiplied by Scale=10: 3300/2800/1200/1200.
            int[] radii = { 200, 300, 300, 200, 330, 280, 120, 120 };
            float[] inclinations = { 7f, 3.4f, 0f, 1.85f, 1.3f, 2.49f, .77f, 1.77f };
            float[] tilts = { .034f, 177.4f, 23.44f, 25.19f, 3.13f, 26.73f, 97.77f, 28.32f };
            for (int i = 0; i < 8; i++)
            {
                var planet = Body(names[i], themes[i], radii[i], i >= 4, rng);
                planet.OrbitInclination = inclinations[i]; planet.Obliquity = tilts[i];
                Place(planet, SolarOrbits[i], 1f / (SolarOrbits[i] * SolarOrbits[i]));
                planet.genData.Set("hosttype", "star"); planet.genData.Set("hostname", star.Name);
                star.Planets.Add(planet);
            }
            AddMoon(star.Planets[2], "月球", "Barren", 200, .04f, rng);
            var jupiter = star.Planets[4];
            AddMoon(jupiter, IoName, "Lava", 200, .115f, rng);
            AddMoon(jupiter, HomeName, "Prairie", 300, .175f, rng);
            AddMoon(jupiter, GanymedeName, "IceGelisol", 200, .245f, rng);
            AddMoon(jupiter, CallistoName, "IceGelisol2", 200, .325f, rng);
            AddMoon(jupiter, "木卫五", "Barren", 50, .395f, rng);
            birthPlanet = jupiter.Moons[1]; birthPlanet.genData.Set("birthPlanet", true);
            birthPlanet.RotationPeriod = 600f; birthPlanet.Obliquity = 8f;
            AddMoon(star.Planets[5], "土卫二", "IceLake", 200, .11f, rng);
            AddMoon(star.Planets[5], "土卫六", "AshenGelisol", 200, .19f, rng);
            AddMoon(star.Planets[5], "土卫八", "IceGelisol2", 200, .28f, rng);
            AddMoon(star.Planets[6], "天卫三", "IceGelisol3", 200, .07f, rng);
            AddMoon(star.Planets[6], "天卫四", "CrystalDesert", 200, .13f, rng);
            AddMoon(star.Planets[7], "海卫一", "IceGelisol", 200, .08f, rng);
            var ceres = Body("谷神星", "Barren", 50, false, rng);
            Place(ceres, 2.77f, .13f); star.Planets.Add(ceres);
            var pluto = Body("冥王星", "IceGelisol", 50, false, rng);
            Place(pluto, 39.482f, .06f); pluto.OrbitInclination = 17.16f;
            AddMoon(pluto, "卡戎", "IceGelisol2", 50, .025f, rng);
            star.Planets.Add(pluto);
            star.Planets.Sort((a, b) => a.OrbitRadius.CompareTo(b.OrbitRadius));
            star.genData.Set("minOrbit", .30f); star.genData.Set("maxOrbit", 42f);
            star.genData.Set("minHZ", .95f); star.genData.Set("maxHZ", 1.67f);
            // Only the starting moon has an authored temperate climate/solar exception.
            foreach (var planet in star.Planets)
                foreach (var moon in planet.Moons) moon.Luminosity = moon == birthPlanet ? 1f : planet.Luminosity;
        }

        private static bool GasSlot(int profile, int i, int length)
        {
            if (profile == 2) return i >= 3;
            if (profile == 3) return i == 0 || i == length - 1;
            if (profile == 1 || profile == 6) return i == length - 1;
            return i >= length - 2;
        }

        private static EThemeHeat Heat(float distance)
        {
            if (distance < .5f) return EThemeHeat.Hot;
            if (distance < .82f) return EThemeHeat.Warm;
            if (distance <= 1.7f) return EThemeHeat.Temperate;
            if (distance < 5f) return EThemeHeat.Cold;
            return EThemeHeat.Frozen;
        }

        private static string Theme(GS2.Random rng, EThemeHeat heat, int radius, bool moon, bool habitable, HashSet<string> used)
        {
            float[] bounds = { 3f, 1f, 0f, -3f, -6f, 6f };
            int h = (int)heat; float low = bounds[h], high = h == 0 ? 6 : bounds[h - 1];
            var candidates = GSSettings.ThemeLibrary.Values.Where(t =>
                (t.ThemeType == EThemeType.Telluric || t.ThemeType == (moon ? EThemeType.Moon : EThemeType.Planet)) &&
                t.PlanetType != EPlanetType.Gas && t.MinRadius <= radius && t.MaxRadius >= radius &&
                t.Temperature >= low && t.Temperature < high && (!habitable || t.Habitable)).Select(t => t.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (candidates.Count == 0) throw new InvalidOperationException("No supported theme for " + heat + "/" + radius);
            var fresh = candidates.Where(n => !used.Contains(n)).ToList();
            if (fresh.Count > 0) candidates = fresh;
            string result = candidates[rng.Next(candidates.Count)]; used.Add(result); return result;
        }

        private static void BuildVaried(GSStar star, GS2.Random rng)
        {
            int profile = star.genData.Get("balancedProfile").Int(0);
            var countRng = new GS2.Random(star.Seed ^ 0x436f756e);
            // A gameplay prior inspired by diverse planetary multiplicities,
            // not a fit to the detection-biased exoplanet catalogue.
            int roll = countRng.Next(100);
            int count = roll < 12 ? 1 : roll < 32 ? 2 : roll < 54 ? 3 : roll < 72 ? 4 : roll < 84 ? 5 : roll < 92 ? 6 : roll < 97 ? 7 : 8;
            star.genData.Set("primaryCount", count);
            float[] template = Layouts[profile];
            var slots = new float[count];
            for (int j = 0; j < count; j++)
            {
                float t = count == 1 ? (profile == 6 ? .55f : .3f) : (float)j / (count - 1);
                float index = t * (template.Length - 1);
                int lo = (int)index, hi = Math.Min(lo + 1, template.Length - 1);
                slots[j] = template[lo] + (template[hi] - template[lo]) * (index - lo);
            }
            float unit = Mathf.Clamp((float)Math.Pow(Math.Max(.05f, star.luminosity), .25), .7f, 2.2f);
            float offset = star.RadiusAU * 1.3f;
            var used = new HashSet<string>();
            float previousOuter = star.RadiusAU + .05f;
            for (int i = 0; i < slots.Length; i++)
            {
                bool gas = count > 1 && GasSlot(profile, i, slots.Length) && (i > 0 || profile == 3);
                bool ice = slots[i] >= 4f;
                if (profile == 3 && count == 2 && i == 1) gas = false;
                int radius = gas ? (ice ? rng.Next(12, 19) * 10 : rng.Next(22, 41) * 10) : (rng.NextDouble() < .55 ? 300 : 200);
                EThemeHeat heat = profile == 6 ? (i < 3 ? EThemeHeat.Cold : EThemeHeat.Frozen) : Heat(slots[i]);
                string theme = gas ? (ice ? (i % 2 == 0 ? "IceGiant" : "IceGiant2") : (i % 2 == 0 ? "GasGiant" : "GasGiant2")) : Theme(rng, heat, radius, false, profile == 4 && heat == EThemeHeat.Temperate, used);
                var planet = Body(star.Name + " " + (i + 1), theme, radius, gas, rng);
                int moons = gas ? (profile == 2 ? rng.Next(1, 4) : rng.Next(0, 3)) : (radius == 300 && rng.NextDouble() < .15 ? 1 : 0);
                float moonOrbit = planet.RadiusAU + .035f;
                for (int m = 0; m < moons; m++)
                {
                    string mt = Theme(rng, heat, 200, true, false, used);
                    AddMoon(planet, planet.Name + "-" + (m + 1), mt, 200, moonOrbit, rng);
                    moonOrbit += .035f + rng.NextFloat(0, .025f);
                }
                float desired = offset + slots[i] * unit * rng.NextFloat(.97f, 1.03f);
                float orbit = Math.Max(desired, previousOuter + Envelope(planet) + .06f);
                Place(planet, orbit, 1f / (slots[i] * slots[i]));
                foreach (var moon in planet.Moons) moon.Luminosity = planet.Luminosity;
                planet.genData.Set("hosttype", "star"); planet.genData.Set("hostname", star.Name);
                star.Planets.Add(planet); previousOuter = orbit + Envelope(planet);
            }
            // Occasional resource outposts; sparse systems can genuinely have one body.
            int smallCount = countRng.NextDouble() < (profile == 6 ? .65 : .25) ? 1 : 0;
            for (int i = 0; i < smallCount; i++)
            {
                string smallTheme = Theme(rng, i == 0 ? EThemeHeat.Frozen : EThemeHeat.Cold, 50, false, false, used);
                var small = Body(star.Name + " · " + (i == 0 ? "外带" : "碎片"), smallTheme, 50, false, rng);
                Place(small, previousOuter + .35f + .3f * i, .06f);
                small.OrbitInclination = rng.NextFloat(8, 24);
                star.Planets.Add(small); previousOuter = small.OrbitRadius + small.RadiusAU;
            }
            var smallRng = new GS2.Random(star.Seed ^ 0x536d616c);
            var hosts = star.Planets.Where(p => p.Scale == 10f && p.Moons.Count >= 2).ToList();
            int largeMoons = star.Planets.Sum(p => p.Moons.Count);
            int smallLimit = Math.Min(hosts.Count, largeMoons / 3);
            for (int i = 0; i < smallLimit; i++)
            {
                var host = hosts[i];
                float innerLimit = host.Moons[0].OrbitRadius - host.Moons[0].RadiusAU;
                float orbit = host.RadiusAU + (innerLimit - host.RadiusAU) * .5f;
                AddMoon(host, host.Name + " · 星砂", host.Theme.StartsWith("IceGiant") ? "IceGelisol2" : "Barren", 50, orbit, smallRng);
                host.Moons.Sort((a, b) => a.OrbitRadius.CompareTo(b.OrbitRadius));
                foreach (var moon in host.Moons) moon.Luminosity = host.Luminosity;
            }
            star.genData.Set("minOrbit", star.RadiusAU + .05f);
            star.genData.Set("maxOrbit", previousOuter + 1f);
            star.genData.Set("minHZ", offset + .82f * unit);
            star.genData.Set("maxHZ", offset + 1.7f * unit);
        }

        // Maximum over ALL descendants; GS2's helper assumes the last moon is widest.
        public static float Envelope(GSPlanet body)
        {
            float radius = body.RadiusAU;
            foreach (var moon in body.Moons) radius = Math.Max(radius, moon.OrbitRadius + Envelope(moon));
            return radius;
        }

        public static IEnumerable<GSPlanet> Bodies(GSPlanet body)
        {
            yield return body;
            foreach (var moon in body.Moons) foreach (var child in Bodies(moon)) yield return child;
        }

        private static void EnsureVein(GSPlanet body, EVeinType kind, int groups, int count, float richness)
        {
            var vt = body.veinSettings.VeinTypes.FirstOrDefault(v => v.type == kind);
            if (vt != null && !vt.rare && vt.veins.Any(v => v.count > 0)) return;
            if (vt == null) { vt = new GSVeinType(kind); body.veinSettings.VeinTypes.Add(vt); }
            vt.rare = false; vt.veins = new List<GSVein>();
            for (int i = 0; i < groups; i++) vt.veins.Add(new GSVein(count, richness));
        }

        private static int BaseNodeBudget(GSPlanet body) => body.Radius == 50 ? 80 :
            (body.Name == GSSettings.BirthPlanetName ? 384 : 420);
        public static int NodeBudget(GSPlanet body) => BaseNodeBudget(body) * (body.Radius == 300 ? 2 : 1);

        public static int Nodes(GSPlanet body)
        {
            if (body.veinSettings == null) return 0;
            int total = 0;
            foreach (var vt in body.veinSettings.VeinTypes)
                foreach (var v in vt.veins) total += vt.type == EVeinType.Oil ? 1 : Math.Max(1, v.count);
            return total;
        }

        // Keep natural-sized deposits. When a planet is crowded, consolidate GROUPS
        // of the same ore, preserving their total solid reserve, rather than shaving
        // every deposit down to six nodes. Oil never participates in reserve scaling.
        public static void NormalizeResources(GSPlanet body)
        {
            if (body.Scale == 10f || body.veinSettings == null) return;
            var types = body.veinSettings.VeinTypes;
            types.RemoveAll(vt => vt.veins.Count == 0);
            if (body.Radius == 50)
            {
                // A small outpost has room for five mineral deposits at most.
                var retained = types.OrderByDescending(vt => (int)vt.type > 7).ThenBy(vt => (int)vt.type).Take(5).ToList();
                types.RemoveAll(vt => !retained.Contains(vt));
                foreach (var vt in types)
                    vt.veins = vt.veins.Take(1).Select(v => new GSVein(v.count, Math.Max(.1f, v.richness) * .25f)).ToList();
            }
            var rng = new GS2.Random(body.Seed ^ 0x4f726532);
            var reserves = new Dictionary<GSVeinType, double>();
            foreach (var vt in types.OrderBy(vt => (int)vt.type))
            {
                if (vt.type == EVeinType.Oil)
                {
                    vt.veins = vt.veins.Take(body.Radius == 50 ? 1 : 12).ToList();
                    foreach (var v in vt.veins) { v.count = 1; v.richness = .7f; }
                    continue;
                }
                reserves[vt] = vt.veins.Sum(v => (double)Math.Max(1, v.count < 0 ? 20 : v.count) *
                    (Finite(v.richness) && v.richness > 0 ? v.richness : .6f));
                foreach (var v in vt.veins) v.count = body.Radius == 50 ? rng.Next(12, 17) : rng.Next(16, 25);
            }
            while (Nodes(body) > BaseNodeBudget(body))
            {
                var candidate = types.Where(vt => vt.type != EVeinType.Oil && vt.veins.Count > 1)
                    .OrderByDescending(vt => vt.veins.Count).ThenBy(vt => (int)vt.type).FirstOrDefault();
                if (candidate == null) throw new InvalidOperationException("Cannot fit natural deposits: " + body.Name);
                candidate.veins.RemoveAt(candidate.veins.Count - 1);
            }
            foreach (var vt in types)
            {
                if (vt.type != EVeinType.Oil)
                {
                    float richness = (float)(reserves[vt] / vt.veins.Sum(v => v.count));
                    foreach (var v in vt.veins) v.richness = richness;
                }
                if (body.Radius == 300 && vt.type != EVeinType.Oil)
                    vt.veins.AddRange(vt.veins.Select(v => v.Clone()).ToList());
                vt.veins = vt.veins.OrderBy(v => v.count).ThenBy(v => v.richness).ToList();
            }
            body.randomizeVeinCounts = false;
        }

        // Single-spring base flow, independent of the solid resource slider,
        // distance coefficient and GS2's preceding-vein-group indexing bug.
        public static int OilAmount(int planetSeed, int groupIndex, float speedMultiplier)
        {
            if (!Finite(speedMultiplier) || speedMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(speedMultiplier));
            var rng = new GS2.Random(unchecked(planetSeed ^ (groupIndex * 16777619) ^ 0x4f494c32));
            double flow = 1.5 + rng.NextDouble() * 4.5; // 1.5 .. 6.0 units/s before mining bonuses
            return Math.Max(1, (int)Math.Round(flow / speedMultiplier, MidpointRounding.ToEven));
        }

        public static void AddMarker(string key)
        {
            if (GSSettings.ThemeLibrary.ContainsKey(key)) return;
            var marker = GSSettings.ThemeLibrary["Barren"].Clone();
            marker.Name = key; marker.DisplayName = key; marker.ThemeType = EThemeType.Private;
            GSSettings.ThemeLibrary.Add(key, marker);
        }

        public static bool UpgradeSun()
        {
            if (!IsManagedGalaxy || GSSettings.ThemeLibrary.ContainsKey(SunMarker)) return false;
            var star = GSSettings.BirthStar;
            if (star == null || star.Name != "太阳 · Sol") return false;
            star.radius = 6f; star.physicsRadius = 7200f;
            AddMarker(SunMarker);
            return true;
        }

        public static void Finish()
        {
            if (IsManagedGalaxy) return; // saved solid deposits are not regenerated or relocated
            var birth = GSSettings.BirthPlanet;
            foreach (var star in GSSettings.Stars)
                foreach (var planet in star.Planets)
                    foreach (var body in Bodies(planet))
                    {
                        if (body.Scale == 10f) { body.veinSettings = new GSVeinSettings(); continue; }
                        if (body.veinSettings == null) body.veinSettings = body.GsTheme.VeinSettings.Clone();
                        if (body == birth)
                        {
                            EnsureVein(body, EVeinType.Stone, 5, 10, .7f);
                            EnsureVein(body, EVeinType.Iron, 7, 12, .8f);
                            EnsureVein(body, EVeinType.Copper, 5, 10, .8f);
                            EnsureVein(body, EVeinType.Coal, 5, 10, .8f);
                            EnsureVein(body, EVeinType.Oil, 8, 1, .7f);
                            EnsureVein(body, EVeinType.Silicium, 3, 8, .6f);
                            EnsureVein(body, EVeinType.Titanium, 3, 8, .6f);
                            foreach (var kind in new[] { EVeinType.Bamboo, EVeinType.Crysrub })
                                EnsureVein(body, kind, 2, 20, .8f);
                            body.randomizeVeinCounts = false;
                        }
                        if (body.Radius >= 200 && (star.Type == EStarType.BlackHole || star.Type == EStarType.NeutronStar))
                            EnsureVein(body, EVeinType.Mag, 4, 8, .75f);
                        if (body.Radius >= 200 && star.Type == EStarType.WhiteDwarf)
                        {
                            EnsureVein(body, EVeinType.Grat, 3, 8, .7f);
                            EnsureVein(body, EVeinType.Fireice, 3, 8, .7f);
                        }
                        if (star == GSSettings.BirthStar && planet.Name == JupiterName)
                        {
                            if (body.Name == IoName) EnsureVein(body, EVeinType.Diamond, 2, 20, .8f);
                            if (body.Name == GanymedeName) EnsureVein(body, EVeinType.Grat, 2, 20, .8f);
                            if (body.Name == CallistoName)
                            {
                                EnsureVein(body, EVeinType.Silicium, 3, 20, .8f);
                                EnsureVein(body, EVeinType.Titanium, 3, 20, .8f);
                            }
                            body.randomizeVeinCounts = false;
                        }
                        if (body.Radius == 50 && body != planet)
                        {
                            // Mineral specialties, not universal rare-resource jackpots.
                            EVeinType specialty = body.Name.StartsWith("卡戎") || body.Name == "木卫五"
                                ? EVeinType.Fractal
                                : new[] { EVeinType.Fractal, EVeinType.Diamond, EVeinType.Grat, EVeinType.Fireice }[(body.Seed & int.MaxValue) % 4];
                            EnsureVein(body, specialty, 1, 16, 2f);
                            if (body.Name.StartsWith("卡戎")) EnsureVein(body, EVeinType.Fireice, 1, 16, 2f);
                        }
                        if (star == GSSettings.BirthStar && body.Name == CallistoName)
                            EnsureVein(body, EVeinType.Fractal, 2, 20, .8f);
                        NormalizeResources(body);
                    }
            AddMarker(SaveMarker); AddMarker(ResourceMarker); AddMarker(SunMarker); AddMarker(LayoutMarker); AddMarker("LivingWorlds.Generation.v1");
            AddMarker("BetterSolarSystem.Surface.v3.1");
            Validate();
        }

        public static void Validate()
        {
            int birthCount = 0;
            var birth = GSSettings.BirthPlanet;
            var starNames = new HashSet<string>(StringComparer.Ordinal);
            var bodyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var star in GSSettings.Stars)
            {
                if (!starNames.Add(star.Name)) throw new InvalidOperationException("Duplicate star name: " + star.Name);
                if (star.Decorative) continue;
                float outer = star.RadiusAU;
                foreach (var planet in star.Planets)
                {
                    if (planet.OrbitRadius - Envelope(planet) <= outer) throw new InvalidOperationException("Orbit envelopes overlap: " + planet.Name);
                    outer = planet.OrbitRadius + Envelope(planet);
                    foreach (var body in Bodies(planet))
                    {
                        if (!bodyNames.Add(body.Name)) throw new InvalidOperationException("Duplicate body name: " + body.Name);
                        bool moon = body != planet;
                        if (body == birth) { birthCount++; if (!moon || body.Radius != 300 || body.Theme != "Prairie") throw new InvalidOperationException("Invalid birth moon"); }
                        else if (moon && ((body.Radius != 200 && body.Radius != 50) || body.Scale != 1f)) throw new InvalidOperationException("Invalid moon size: " + body.Name);
                        if (body.Scale != 10 && body.Radius != 50 && body.Radius != 200 && body.Radius != 300) throw new InvalidOperationException("Invalid planet size: " + body.Name);
                        if (body.Scale == 10 && body.Radius * body.Scale < 1000) throw new InvalidOperationException("Gas giant too small");
                        if (!Finite(body.OrbitRadius) || body.OrbitRadius <= 0 || !Finite(body.OrbitalPeriod) || body.OrbitalPeriod <= 0) throw new InvalidOperationException("Invalid orbit: " + body.Name);
                        float moonOuter = body.RadiusAU;
                        foreach (var satellite in body.Moons)
                        {
                            if (satellite.OrbitRadius - Envelope(satellite) <= moonOuter) throw new InvalidOperationException("Moon orbit overlap: " + satellite.Name);
                            moonOuter = satellite.OrbitRadius + Envelope(satellite);
                        }
                        if (body.Scale != 10 && Nodes(body) > NodeBudget(body)) throw new InvalidOperationException("Vein budget exceeded: " + body.Name);
                    }
                }
                int smallMoons = star.Planets.SelectMany(p => Bodies(p).Skip(1)).Count(p => p.Radius == 50);
                int largeMoons = star.Planets.SelectMany(p => Bodies(p).Skip(1)).Count(p => p.Radius == 200);
                if (smallMoons > 0 && smallMoons >= largeMoons) throw new InvalidOperationException("Small moons must remain a minority: " + star.Name);
                if (star.bodyCount >= 100 || outer > star.MaxOrbit) throw new InvalidOperationException("System capacity exceeded: " + star.Name);
            }
            if (birthCount != 1) throw new InvalidOperationException("Birth moon is not unique");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
