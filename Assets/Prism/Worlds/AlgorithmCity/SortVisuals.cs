using UnityEngine;

namespace Prism.Worlds.AlgorithmCity
{
    /// <summary>
    /// Turns one <see cref="SortStep"/> into bead motion. Shared by every flat (single-row)
    /// structure — the Insertion track and both halves plus the combine pass of the Create-stage
    /// pipeline bench — so the "what does a comparison look like" answer is written exactly once.
    /// The cascading Merge structure gets its own variant because it is the one place a step also
    /// changes which ROW a bead lives on.
    /// </summary>
    public static class SortVisuals
    {
        /// <summary>
        /// Apply a step from a process whose beads sit at <c>localIndex + offset</c> in a shared
        /// BeadField. Offset is 0 for a structure that owns the whole field (Insertion) and equal
        /// to the pipeline bench's split point for a sub-process that only owns the right half.
        /// </summary>
        public static void ApplyFlatStep(BeadField beads, ISortProcess process, SortStep step, int offset)
        {
            int gA = step.A < 0 ? -1 : step.A + offset;
            int gB = step.B < 0 ? -1 : step.B + offset;

            switch (step.Kind)
            {
                case SortStep.StepKind.Compare:
                    Recolor(beads, process, step.A, gA);
                    Recolor(beads, process, step.B, gB);
                    // The two tokens visually CROSS: each keeps its own fixed home slot as its
                    // target but takes on the other's current position, so it glides back into a
                    // different slot rather than the two colours instantly trading places.
                    if (step.Swapped && gA >= 0 && gB >= 0) CrossPositions(beads, gA, gB);
                    Glow(beads, gA);
                    Glow(beads, gB);
                    break;

                case SortStep.StepKind.Drain:
                    Glow(beads, gA);
                    Glow(beads, gB);
                    break;

                case SortStep.StepKind.Publish:
                    for (int x = step.WindowLo; x < step.WindowHi; x++)
                    {
                        Recolor(beads, process, x, x + offset);
                        Glow(beads, x + offset);
                    }
                    break;
            }
        }

        /// <summary>
        /// Apply a step from the cascading Merge structure: on Publish, every bead in the finished
        /// window not only recolours but rises to its new row — the physical "more merged, higher
        /// up" reading that makes the shape of divide-and-conquer visible.
        /// </summary>
        public static void ApplyCascadeStep(BeadField beads, MergeSortProcess process, SortStep step,
                                            System.Func<int, int, Vector3> slotPos)
        {
            switch (step.Kind)
            {
                case SortStep.StepKind.Compare:
                case SortStep.StepKind.Drain:
                    Glow(beads, step.A);
                    Glow(beads, step.B);
                    break;

                case SortStep.StepKind.Publish:
                    for (int x = step.WindowLo; x < step.WindowHi; x++)
                    {
                        Recolor(beads, process, x, x);
                        var b = beads.Beads[x];
                        b.Target = slotPos(x, process.RowOf[x]);
                        beads.Beads[x] = b;
                        Glow(beads, x);
                    }
                    break;
            }
        }

        static void Recolor(BeadField beads, ISortProcess process, int localIndex, int globalIndex)
        {
            if (localIndex < 0 || globalIndex < 0 || globalIndex >= beads.Beads.Length) return;
            int n = process.Values.Length;
            float v = n <= 1 ? 0.5f : (float)(process.Values[localIndex] - 1) / (n - 1);
            var b = beads.Beads[globalIndex];
            b.Value01 = v;
            beads.Beads[globalIndex] = b;
        }

        static void CrossPositions(BeadField beads, int a, int b)
        {
            var ba = beads.Beads[a];
            var bb = beads.Beads[b];
            var tmp = ba.Position;
            ba.Position = bb.Position;
            bb.Position = tmp;
            beads.Beads[a] = ba;
            beads.Beads[b] = bb;
        }

        static void Glow(BeadField beads, int index)
        {
            if (index < 0 || index >= beads.Beads.Length) return;
            var b = beads.Beads[index];
            b.Glow = 1f;
            beads.Beads[index] = b;
        }
    }
}
