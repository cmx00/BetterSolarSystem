using System;
using System.Collections.Generic;

namespace BetterSolarSystem.Balance
{
    // Balanced point tree; geometry has no Unity or game pool dependency.
    internal sealed class EnemyCandidateIndex
    {
        internal struct Candidate
        {
            internal int Id, Origin, Model, Prototype, Revision;
            internal double X, Y, Z;
            internal double Axis(int axis) => axis == 0 ? X : axis == 1 ? Y : Z;
        }
        private readonly Candidate[] points;
        internal int Count => points.Length;
        internal EnemyCandidateIndex(List<Candidate> source)
        {
            points = source.ToArray();
            Partition(0, points.Length, 0);
        }
        private void Partition(int start, int end, int depth)
        {
            if (end - start < 2) return;
            int middle = start + (end - start) / 2, axis = depth % 3;
            int left = start, right = end - 1;
            // Select a median without repeatedly sorting each subtree.
            while (left < right)
            {
                double pivot = points[left + (right - left) / 2].Axis(axis);
                int low = left, high = right;
                while (low <= high)
                {
                    while (points[low].Axis(axis) < pivot) low++;
                    while (points[high].Axis(axis) > pivot) high--;
                    if (low <= high) { var temp = points[low]; points[low++] = points[high]; points[high--] = temp; }
                }
                if (middle <= high) right = high;
                else if (middle >= low) left = low;
                else break;
            }
            Partition(start, middle, depth + 1);
            Partition(middle + 1, end, depth + 1);
        }
        internal Candidate? Nearest(double x, double y, double z, double range, Func<Candidate, bool> valid)
        {
            if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range)) return null;
            double bestDistance = range * range;
            Candidate? result = null;
            Visit(0, points.Length, 0, x, y, z, valid, ref bestDistance, ref result);
            return result;
        }
        private void Visit(int start, int end, int depth, double x, double y, double z,
            Func<Candidate, bool> valid, ref double bestDistance, ref Candidate? result)
        {
            if (start >= end) return;
            int middle = start + (end - start) / 2;
            var point = points[middle];
            double dx = point.X - x, dy = point.Y - y, dz = point.Z - z;
            double distance = dx * dx + dy * dy + dz * dz;
            if (distance <= bestDistance && valid(point)) { bestDistance = distance; result = point; }
            double split = depth % 3 == 0 ? dx : depth % 3 == 1 ? dy : dz;
            if (split > 0)
            {
                Visit(start, middle, depth + 1, x, y, z, valid, ref bestDistance, ref result);
                if (split * split <= bestDistance) Visit(middle + 1, end, depth + 1, x, y, z, valid, ref bestDistance, ref result);
            }
            else
            {
                Visit(middle + 1, end, depth + 1, x, y, z, valid, ref bestDistance, ref result);
                if (split * split <= bestDistance) Visit(start, middle, depth + 1, x, y, z, valid, ref bestDistance, ref result);
            }
        }
        internal static bool Matches(Candidate candidate, int id, int origin, int model, int prototype, int revision = 0) =>
            id > 0 && candidate.Id == id && candidate.Origin == origin && candidate.Model == model && candidate.Prototype == prototype && candidate.Revision == revision;
    }
}
