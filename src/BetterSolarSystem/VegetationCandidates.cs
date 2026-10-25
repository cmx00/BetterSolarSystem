using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterSolarSystem.Balance
{
    // Use the native query's chunk footprint and collider rules, but grow the
    // result list ourselves. The native NonAlloc method writes beyond capacity
    // before its caller has an opportunity to retry with a larger array.
    internal static class VegetationCandidates
    {
        [ThreadStatic] private static HashSet<int> chunkIds;
        [ThreadStatic] private static HashSet<int> plantIds;

        internal static void Collect(ColliderContainer[] chunks, Vector3 center, float radius, List<int> output)
        {
            output.Clear();
            if (chunks == null) return;
            if (chunkIds == null) chunkIds = new HashSet<int>(); else chunkIds.Clear();
            if (plantIds == null) plantIds = new HashSet<int>(); else plantIds.Clear();
            radius = Math.Max(0f, radius);
            Vector3 normal = center.normalized;
            Vector3 across = Vector3.Cross(normal, Vector3.up).normalized;
            Vector3 along;
            if (across.sqrMagnitude < .25f) { across = Vector3.right; along = Vector3.forward; }
            else along = Vector3.Cross(across, normal).normalized;
            across *= (radius + 3f) * .5f;
            along *= (radius + 3f) * .5f;
            Mark(center, chunks.Length);
            Mark(center + across, chunks.Length); Mark(center - across, chunks.Length);
            Mark(center + along, chunks.Length); Mark(center - along, chunks.Length);
            Mark(center + across + along, chunks.Length); Mark(center - across + along, chunks.Length);
            Mark(center + across - along, chunks.Length); Mark(center - across - along, chunks.Length);
            Mark(center + across * 2f, chunks.Length); Mark(center - across * 2f, chunks.Length);
            Mark(center + along * 2f, chunks.Length); Mark(center - along * 2f, chunks.Length);
            Mark(center + across * 2f + along * 2f, chunks.Length);
            Mark(center - across * 2f + along * 2f, chunks.Length);
            Mark(center + across * 2f - along * 2f, chunks.Length);
            Mark(center - across * 2f - along * 2f, chunks.Length);
            float radius2 = radius * radius;
            foreach (int hash in chunkIds)
            {
                var chunk = chunks[hash];
                var pool = chunk?.colliderPool;
                if (pool == null) continue;
                int end = Math.Min(chunk.cursor, pool.Length);
                for (int i = 1; i < end; i++)
                {
                    var collider = pool[i];
                    if (collider.idType == 0 || collider.isForBuild || collider.objType != EObjectType.Vegetable ||
                        (collider.pos - center).sqrMagnitude > radius2 + collider.ext.sqrMagnitude) continue;
                    if (plantIds.Add(collider.objId)) output.Add(collider.objId);
                }
            }
        }
        private static void Mark(Vector3 point, int count)
        {
            int hash = PlanetPhysics.HashPhysBlock(point);
            if (hash >= 0 && hash < count) chunkIds.Add(hash);
        }
    }
}
