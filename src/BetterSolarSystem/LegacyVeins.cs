using System;
using System.Collections.Generic;
using System.Linq;
using GalacticScale;
using UnityEngine;

namespace BetterSolarSystem
{
    // Versioned, one-time historical repair. No geometry writes and no factory edits.
    public static class LegacyVeins
    {
        public const string Marker = "GS2Tweaks.LegacyRepaired.v2";
        public static int Repair(float baseRadius = 200f, float exponent = 1f, bool scaleRare = false)
        {
            var lib = GSSettings.ThemeLibrary;
            if (lib == null || BalancedGeneration.IsManagedGalaxy || lib.ContainsKey(Marker)) return 0;
            int changed = 0;
            foreach (var star in GSSettings.Stars)
                foreach (var planet in star.Planets)
                    foreach (var body in BalancedGeneration.Bodies(planet))
                    {
                        if (body.Scale == 10 || body.veinSettings == null || !lib.ContainsKey(body.Theme)) continue;
                        var template = lib[body.Theme].VeinSettings;
                        int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Pow(body.Radius / Mathf.Max(1, baseRadius), exponent)), 1, 4);
                        foreach (var vt in body.veinSettings.VeinTypes)
                        {
                            if (vt.veins == null || vt.veins.Count == 0) continue;
                            // The reliable birth kit is authored data, including its rare=false bit.
                            if (body.Name == GSSettings.BirthPlanetName && !vt.rare &&
                                (vt.type == EVeinType.Bamboo || vt.type == EVeinType.Crysrub || vt.type == EVeinType.Grat || vt.type == EVeinType.Diamond)) continue;
                            var canonical = template?.VeinTypes.FirstOrDefault(t => t.type == vt.type);
                            if (canonical == null)
                            {
                                // Uniform authored deposits are NOT evidence of old clone-doubling.
                                if (NonUniformMirror(vt.veins))
                                {
                                    vt.veins.RemoveRange(vt.veins.Count / 2, vt.veins.Count / 2); changed++;
                                }
                                continue;
                            }
                            if (canonical.veins.Count == 0) continue;
                            var rebuilt = new List<GSVein>();
                            foreach (var v in canonical.veins)
                            {
                                int count = v.count < 0 ? 12 : v.count;
                                int parts = vt.rare && !scaleRare ? 1 : n;
                                if (vt.type != EVeinType.Oil) parts = Math.Min(parts, Math.Max(1, count));
                                for (int i = 0; i < parts; i++)
                                {
                                    int c = vt.type == EVeinType.Oil ? 1 : count / parts + (i < count % parts ? 1 : 0);
                                    if (c <= 0) continue;
                                    rebuilt.Add(new GSVein(c, vt.type == EVeinType.Oil ? v.richness : v.richness * count / c));
                                }
                            }
                            vt.veins = rebuilt; changed++;
                        }
                    }
            // Serialized in the existing theme-library extension point. It is never selected.
            if (!lib.ContainsKey("Barren")) throw new InvalidOperationException("Legacy repair needs Barren marker template");
            var marker = lib["Barren"].Clone(); marker.Name = Marker; marker.ThemeType = EThemeType.Private;
            marker.DisplayName = "GS2Tweaks historical vein repair version 2"; lib.Add(Marker, marker);
            return changed;
        }

        private static bool NonUniformMirror(List<GSVein> veins)
        {
            if (veins.Count < 4 || veins.Count % 2 != 0) return false;
            int half = veins.Count / 2; bool diverse = false;
            for (int i = 0; i < half; i++)
            {
                if (veins[i].count != veins[i + half].count || Math.Abs(veins[i].richness - veins[i + half].richness) > .0001f) return false;
                if (i > 0 && (veins[i].count != veins[0].count || Math.Abs(veins[i].richness - veins[0].richness) > .0001f)) diverse = true;
            }
            return diverse;
        }
    }
}
