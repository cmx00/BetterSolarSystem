using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace BetterSolarSystem
{
    internal static class ConfigMigration
    {
        private static readonly Dictionary<string, ConfigFile> LegacyFiles = new Dictionary<string, ConfigFile>();
        public static T LegacyValue<T>(string guid, string section, string key, T fallback)
        {
            ConfigFile legacy;
            if (!LegacyFiles.TryGetValue(guid, out legacy))
            {
                string path = Path.Combine(Paths.ConfigPath, guid + ".cfg");
                legacy = File.Exists(path) ? new ConfigFile(path, false) { SaveOnConfigSet = false } : null;
                LegacyFiles[guid] = legacy;
            }
            return legacy == null ? fallback : legacy.Bind(section, key, fallback).Value;
        }
        public static ConfigEntry<T> BindLegacy<T>(ConfigFile config, string guid, string section, string key, T fallback, string description)
            => config.Bind(section, key, LegacyValue(guid, section, key, fallback), description);
        public static ConfigEntry<T> BindDefense<T>(ConfigFile config, string section, string key, T fallback, ConfigDescription description)
            => config.Bind("Balance - Defense / 防御 - " + section, key, LegacyValue("com.myself.globalsignaltower", section, key, fallback), description);
        public static ConfigEntry<T> BindDefense<T>(ConfigFile config, string section, string key, T fallback, string description)
            => BindDefense(config, section, key, fallback, new ConfigDescription(description));
    }
}
