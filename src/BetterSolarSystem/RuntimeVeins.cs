using System;

namespace BetterSolarSystem
{
    // Loaded factory data is authoritative. Never regenerate terrain, move deposits,
    // alter their IDs, reset depletion, or touch miners/buildings in an existing save.
    public static class RuntimeVeins
    {
        public static int RepairPool(VeinData[] pool, int cursor, VeinGroup[] groups, int seed)
        {
            if (pool == null || groups == null) return 0;
            int changed = 0;
            for (int i = 1; i < Math.Min(cursor, pool.Length); i++)
            {
                var vein = pool[i];
                if (vein.id != i || vein.type != EVeinType.Oil ||
                    vein.amount * VeinData.oilSpeedMultiplier < 10f) continue;
                if (vein.groupIndex <= 0 || vein.groupIndex >= groups.Length || groups[vein.groupIndex].type != EVeinType.Oil) continue;
                int amount = BalancedGeneration.OilAmount(seed, vein.groupIndex - 1, VeinData.oilSpeedMultiplier);
                groups[vein.groupIndex].amount += (long)amount - vein.amount;
                pool[i].amount = amount;
                changed++;
            }
            return changed;
        }

        public static int RepairLoadedOil(GameData game)
        {
            int changed = 0;
            if (game?.factories == null) return changed;
            for (int i = 0; i < Math.Min(game.factoryCount, game.factories.Length); i++)
            {
                var factory = game.factories[i];
                if (factory?.planet == null) continue;
                changed += RepairPool(factory.veinPool, factory.veinCursor, factory.veinGroups, factory.planet.seed);
            }
            return changed;
        }
    }
}
