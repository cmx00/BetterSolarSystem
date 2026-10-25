using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BetterSolarSystem.Balance
{
    internal static class GroundFireSafety
    {
        [ThreadStatic] private static List<int> vegetation;
        internal static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(TurretComponent), "Shoot_Gauss"),
                transpiler: new HarmonyMethod(typeof(GroundFireSafety), nameof(ProjectileLife)));
            harmony.Patch(AccessTools.Method(typeof(TurretComponent), "Shoot_LocalPlasma"),
                transpiler: new HarmonyMethod(typeof(GroundFireSafety), nameof(ProjectileLife)));
            harmony.Patch(AccessTools.Method(typeof(TurretComponent), "Shoot_Laser"),
                prefix: new HarmonyMethod(typeof(GroundFireSafety), nameof(CheckLaser)));
            harmony.Patch(AccessTools.Method(typeof(TurretComponent), "CheckEnemyIsInAttackRange"),
                postfix: new HarmonyMethod(typeof(GroundFireSafety), nameof(CheckCandidate)));
            harmony.Patch(AccessTools.Method(typeof(TurretComponent), "CheckTargetValidBeforeShoot"),
                postfix: new HarmonyMethod(typeof(GroundFireSafety), nameof(CheckShot)));
        }
        private static IEnumerable<CodeInstruction> ProjectileLife(IEnumerable<CodeInstruction> input)
        {
            int fields = 0;
            foreach (var code in input)
            {
                if (code.opcode == OpCodes.Stfld && code.operand is FieldInfo f && (f.DeclaringType == typeof(LocalGeneralProjectile) || f.DeclaringType == typeof(GeneralProjectile)) &&
                    (f.Name == "life" || f.Name == "lifemax"))
                {
                    var descriptor = new CodeInstruction(OpCodes.Ldarg_2);
                    descriptor.labels.AddRange(code.labels); code.labels.Clear();
                    descriptor.blocks.AddRange(code.blocks); code.blocks.Clear();
                    yield return descriptor;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DefenseBalance), "GroundProjectileLife"));
                    fields++;
                }
                yield return code;
            }
            if (fields != 2) throw new InvalidOperationException("Unsupported ground projectile lifetime API: " + fields);
        }
        private static void CheckCandidate(TurretComponent __instance, PlanetFactory factory, PrefabDesc pdesc,
            [HarmonyArgument(3)] ref SkillTarget target, ref bool __result)
        {
            if (__result) __result = Clear(__instance, factory, pdesc, target);
        }
        private static void CheckShot(ref TurretComponent __instance, PlanetFactory factory, PrefabDesc pdesc, ref bool __result)
        {
            if (__result && !Clear(__instance, factory, pdesc, __instance.target))
            { __result = false; __instance.target = default(SkillTarget); }
        }
        private static bool CheckLaser(ref TurretComponent __instance, PlanetFactory factory, PrefabDesc pdesc)
        {
            if (Clear(__instance, factory, pdesc, __instance.target)) return true;
            __instance.target = default(SkillTarget);
            return false;
        }
        private static bool Clear(TurretComponent turret, PlanetFactory factory, PrefabDesc desc, SkillTarget target)
        {
            if (!DefenseBalance.Enabled || (turret.type != ETurretType.Gauss && turret.type != ETurretType.Laser && turret.type != ETurretType.LocalPlasma) ||
                factory?.planet == null || target.type != ETargetType.Enemy || target.astroId != factory.planet.astroId ||
                factory.enemyPool == null || target.id <= 0 || target.id >= factory.enemyPool.Length) return true;
            var enemy = factory.enemyPool[target.id];
            if (enemy.id != target.id) return false;
            Vector3 start = turret.muzzleWPos, end = enemy.pos;
            if (start.sqrMagnitude < 1f) return true; // No initialized muzzle: keep the native decision.
            float native = DefenseBalance.NativeGroundRange(desc);
            if ((end - start).sqrMagnitude <= native * native) return true;
            var planet = factory.planet;
            if (!AboveSurface(start, end, planet.realRadius - 2f,
                planet.data == null ? (Func<Vector3, float>)null : p => planet.data.QueryHeight(p))) return false;
            return ClearVegetation(factory, start, end);
        }
        internal static bool AboveSurface(Vector3 start, Vector3 end, float solidRadius, Func<Vector3, float> height = null)
        {
            Vector3 delta = end - start;
            float length2 = delta.sqrMagnitude;
            if (length2 < 1e-6f) return true;
            float closest = Mathf.Clamp01(-Vector3.Dot(start, delta) / length2);
            if ((start + delta * closest).sqrMagnitude < solidRadius * solidRadius) return false;
            if (height == null) return true;
            int steps = Math.Max(4, Math.Min(64, (int)Math.Ceiling(Math.Sqrt(length2) / 8d)));
            for (int i = 1; i < steps; i++)
            {
                var point = start + delta * (i / (float)steps);
                if (point.magnitude + .25f < height(point)) return false;
            }
            return true;
        }
        private static bool ClearVegetation(PlanetFactory factory, Vector3 start, Vector3 end)
        {
            var physics = factory.planet.physics;
            if (physics?.nearColliderLogic == null || factory.vegePool == null) return true;
            Vector3 delta = end - start;
            float distance = delta.magnitude - 2f;
            if (distance <= 0f) return true;
            Vector3 direction = delta.normalized;
            if (vegetation == null) vegetation = new List<int>(512);
            VegetationCandidates.Collect(physics.colChunks, (start + end) * .5f, delta.magnitude * .5f + 32f, vegetation);
            for (int i = 0; i < vegetation.Count; i++)
            {
                int id = vegetation[i];
                if (id <= 0 || id >= factory.vegePool.Length || factory.vegePool[id].id != id) continue;
                int colliderId = factory.vegePool[id].colliderId;
                for (int link = 0; colliderId > 0 && link < 16; link++)
                {
                    ColliderData collider;
                    try { collider = physics.GetColliderData(colliderId); }
                    catch (IndexOutOfRangeException) { break; }
                    if (collider.objType != EObjectType.Vegetable || collider.objId != id) break;
                    // Match the projectile's handling of build-only linked volumes.
                    if ((collider.notForBuild || collider.link == 0) && Blocks(collider, start, direction, distance)) return false;
                    colliderId = collider.link;
                }
            }
            return true;
        }
        internal static bool Blocks(ColliderData collider, Vector3 start, Vector3 direction, float distance)
        {
            RCHCPU hit;
            return collider.shape == EColliderShape.Sphere && Phys.RayCastSphere(start, direction, distance, collider.pos, collider.radius, out hit) ||
                collider.shape == EColliderShape.Box && Phys.RayCastOBB(start, direction, distance, collider.ext, collider.pos, collider.q, out hit) ||
                collider.shape == EColliderShape.Capsule && Phys.RayCastCapsule(start, direction, distance, collider.pos - collider.ext, collider.pos + collider.ext, collider.radius, out hit);
        }
    }
}
