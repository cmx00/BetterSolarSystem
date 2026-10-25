using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace BetterSolarSystem
{
    public enum BalancePreset { Custom, GalaxyOnly, Recommended, EnhancedDefense }

    // Presets select effective values, leaving the user's individual settings intact.
    public static class BalancePresets
    {
        private static ConfigEntry<BalancePreset> selection;
        public static BalancePreset Current => selection?.Value ?? BalancePreset.Custom;
        public static void Configure(ConfigFile config)
        {
            string existing = File.Exists(config.ConfigFilePath) ? File.ReadAllText(config.ConfigFilePath) : "";
            bool migrated = existing.Contains("Balance - Modules") || existing.Contains("全局范围倍率") ||
                File.Exists(Path.Combine(Paths.ConfigPath, "com.myself.globalsignaltower.cfg"));
            selection = config.Bind("Balance / 平衡", "Preset / 预设",
                migrated ? BalancePreset.Custom : BalancePreset.Recommended,
                "GalaxyOnly: generation only. Recommended: 1.5x ground range, 4x space range and travel adjustments. " +
                "EnhancedDefense: wider signal coverage. Custom: use the individual settings below. Restart after changing. " +
                "/ GalaxyOnly 只调星系；Recommended 对地 1.5 倍、对空 4 倍并调整航行；EnhancedDefense 扩大信号覆盖；Custom 使用下面的独立设置。修改后重启。已有配置默认 Custom。");
        }
        public static bool DefenseEnabled(bool custom) => Current == BalancePreset.Custom ? custom : Current != BalancePreset.GalaxyOnly;
        public static bool TravelEnabled(bool custom) => Current == BalancePreset.Custom ? custom : Current != BalancePreset.GalaxyOnly;
        public static bool GlobalSignals(bool custom) => Current == BalancePreset.Custom && custom;
        public static float Ground(float custom) => Current == BalancePreset.Custom ? custom : Current == BalancePreset.GalaxyOnly ? 1f : 1.5f;
        public static float Space(float custom) => Current == BalancePreset.Custom ? custom : Current == BalancePreset.GalaxyOnly ? 1f : 4f;
        public static float Signal(float custom) => Current == BalancePreset.Custom ? custom : Current == BalancePreset.GalaxyOnly ? 1f : Current == BalancePreset.EnhancedDefense ? 2f : 1.25f;
        public static float Station(float custom) => Current == BalancePreset.Custom ? custom : 110f;
        public static float Travel(float custom, float recommended) => Current == BalancePreset.Custom ? custom : Current == BalancePreset.GalaxyOnly ? 1f : recommended;
    }
}
