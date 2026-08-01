using UnityEngine;

namespace Prism.Worlds.MathMotion
{
    /// <summary>
    /// The honest mathematics behind the world: a sampled function, its derivative and its
    /// integral, computed for real every time they are asked for.
    ///
    /// No MonoBehaviour, no rendering, no hands - plain C#, exactly like OrbitalSim, and for the
    /// same reason: the physics (here, the calculus) should be checkable on its own, and should
    /// not know that a headset exists.
    ///
    /// THE THREE HONESTY COMMITMENTS THIS CLASS MAKES:
    ///
    /// 1. The derivative is a real central-difference approximation of the CURRENT sampled curve,
    ///    recomputed from scratch whenever <see cref="RecomputeDerivative"/> is called. It is
    ///    never smoothed, cached across an edit, or faked from the shape of the deformation - it
    ///    is read back out of the numbers the same way a learner could with a ruler and a lot of
    ///    patience.
    ///
    /// 2. The integral (<see cref="TotalArea"/> and <see cref="AccumulateDerivative"/>) is a real
    ///    trapezoidal accumulation. Accumulating the derivative back up reproduces the original
    ///    function to within ordinary O(dx^2) numerical error - at 121 samples across a domain of
    ///    width 2 that error is far below anything visible, so the match the learner sees at
    ///    Formalize is the Fundamental Theorem of Calculus actually happening, not staged.
    ///
    /// 3. Deformation (<see cref="ApplyGaussianBump"/>) is a real local basis-function edit: a
    ///    Gaussian of the requested height is added at the requested centre. Nothing about the
    ///    curve away from that centre is touched except by the Gaussian's own rapidly vanishing
    ///    tail, so the derivative elsewhere responds correctly - including by not moving where it
    ///    should not.
    /// </summary>
    public class CurveField
    {
        /// <summary>Samples across the domain. Dense enough that linear interpolation between
        /// samples is imperceptible at display scale, cheap enough to rebuild every frame.</summary>
        public const int SampleCount = 121;

        public const float DomainMin = -1f;
        public const float DomainMax = 1f;

        /// <summary>Hard clamp on any sample's value, so repeated edits cannot send the curve
        /// off into territory the display was never scaled for.</summary>
        public const float DisplacementLimit = 1.3f;

        public readonly float[] X = new float[SampleCount];
        public readonly float[] Y = new float[SampleCount];
        public readonly float[] Derivative = new float[SampleCount];

        public readonly float Dx;

        public CurveField()
        {
            Dx = (DomainMax - DomainMin) / (SampleCount - 1);
            for (int i = 0; i < SampleCount; i++) X[i] = DomainMin + Dx * i;
            ResetToPreset();
            RecomputeDerivative();
        }

        /// <summary>
        /// The curve a learner meets at Wonder: smooth, with one clear hump and one clear dip, so
        /// the derivative already has real structure (a positive lobe, a negative lobe, two
        /// zero-crossings) before anyone has touched anything.
        /// </summary>
        public void ResetToPreset()
        {
            for (int i = 0; i < SampleCount; i++)
            {
                float x = X[i];
                Y[i] = 0.20f * Mathf.Sin(Mathf.PI * x) + 0.11f * Mathf.Sin(2.3f * Mathf.PI * x + 0.6f);
            }
        }

        /// <summary>
        /// Central differences on the interior, one-sided at the two ends. The end points are
        /// therefore first-order rather than second-order accurate - the one honest approximation
        /// in this class, noted here because it is the only place accuracy is deliberately traded
        /// away, and it costs nothing a learner could ever notice at two sample widths from a
        /// domain edge nobody interacts with directly.
        /// </summary>
        public void RecomputeDerivative()
        {
            int n = SampleCount;
            Derivative[0] = (Y[1] - Y[0]) / Dx;
            for (int i = 1; i < n - 1; i++)
                Derivative[i] = (Y[i + 1] - Y[i - 1]) / (2f * Dx);
            Derivative[n - 1] = (Y[n - 1] - Y[n - 2]) / Dx;
        }

        /// <summary>
        /// A single local basis-function edit: add a Gaussian of the given signed height, sigma
        /// wide, centred at xCenter. Calling this once per frame while a hand drags across the
        /// curve is what "sculpting" is in this world - each call is honest on its own, and a
        /// drag is simply many honest edits in a row.
        /// </summary>
        public void ApplyGaussianBump(float xCenter, float amplitude, float sigma)
        {
            if (Mathf.Abs(amplitude) < 1e-6f) return;
            float inv2s2 = 1f / (2f * sigma * sigma);
            for (int i = 0; i < SampleCount; i++)
            {
                float d = X[i] - xCenter;
                float w = Mathf.Exp(-d * d * inv2s2);
                if (w < 1e-4f) continue;
                Y[i] = Mathf.Clamp(Y[i] + amplitude * w, -DisplacementLimit, DisplacementLimit);
            }
        }

        /// <summary>Linear interpolation between samples.</summary>
        public float ValueAt(float x) => Interpolate(Y, x);

        /// <summary>Linear interpolation of the already-computed derivative array. Call
        /// <see cref="RecomputeDerivative"/> first if Y has changed since the last call.</summary>
        public float DerivativeAt(float x) => Interpolate(Derivative, x);

        float Interpolate(float[] arr, float x)
        {
            x = Mathf.Clamp(x, DomainMin, DomainMax);
            float t = (x - DomainMin) / Dx;
            int i0 = Mathf.Clamp((int)t, 0, SampleCount - 2);
            float f = t - i0;
            return Mathf.Lerp(arr[i0], arr[i0 + 1], f);
        }

        public int NearestIndex(float x)
        {
            x = Mathf.Clamp(x, DomainMin, DomainMax);
            return Mathf.Clamp(Mathf.RoundToInt((x - DomainMin) / Dx), 0, SampleCount - 1);
        }

        /// <summary>The real, signed, definite integral of Y over the whole domain (trapezoidal).</summary>
        public float TotalArea()
        {
            float sum = 0f;
            for (int i = 1; i < SampleCount; i++) sum += (Y[i - 1] + Y[i]) * 0.5f * Dx;
            return sum;
        }

        public float MaxValue()
        {
            float m = Y[0];
            for (int i = 1; i < SampleCount; i++) if (Y[i] > m) m = Y[i];
            return m;
        }

        /// <summary>
        /// Cumulative trapezoidal integral of the CURRENT derivative array, from the domain start
        /// up to sample `uptoIndex` inclusive, offset so it starts at Y[0]. This is the
        /// Fundamental Theorem of Calculus made into a curve: `into[i]` is what f(X[i]) must be if
        /// all you are told is f(domain start) and f'(x) everywhere in between - and because it is
        /// built purely from the derivative array, it lands back on the real Y array (to within
        /// numerical error) whenever `uptoIndex` reaches the far end.
        ///
        /// `into` is caller-owned (length >= SampleCount) so nothing allocates here.
        /// </summary>
        public void AccumulateDerivative(int uptoIndex, float[] into)
        {
            into[0] = Y[0];
            int n = Mathf.Clamp(uptoIndex, 0, SampleCount - 1);
            for (int i = 1; i <= n; i++)
                into[i] = into[i - 1] + (Derivative[i - 1] + Derivative[i]) * 0.5f * Dx;
        }

        /// <summary>
        /// The most prominent interior local maximum: a sample strictly higher than both
        /// neighbours, scored by how far it stands above the higher of its two flanking valleys
        /// (a cheap, honest stand-in for topographic prominence that ignores sample-to-sample
        /// noise there is none of, since the curve is an analytic sum of smooth terms).
        /// </summary>
        public bool BestMaximum(out int index, float minProminence = 0.05f) => BestExtremum(true, minProminence, out index);

        public bool BestMinimum(out int index, float minProminence = 0.05f) => BestExtremum(false, minProminence, out index);

        bool BestExtremum(bool wantMax, float minProminence, out int bestIndex)
        {
            bestIndex = -1;
            float bestProminence = minProminence;
            for (int i = 1; i < SampleCount - 1; i++)
            {
                bool isCandidate = wantMax ? (Y[i] > Y[i - 1] && Y[i] > Y[i + 1])
                                            : (Y[i] < Y[i - 1] && Y[i] < Y[i + 1]);
                if (!isCandidate) continue;

                float leftFlank = FlankExtent(i, -1, wantMax);
                float rightFlank = FlankExtent(i, 1, wantMax);
                float prominence = wantMax ? Y[i] - Mathf.Max(leftFlank, rightFlank)
                                            : Mathf.Min(leftFlank, rightFlank) - Y[i];
                if (prominence > bestProminence) { bestProminence = prominence; bestIndex = i; }
            }
            return bestIndex >= 0;
        }

        /// <summary>Walk outward from i until the curve turns back past the peak's own height,
        /// tracking the shallowest point seen - the col this peak would need to be reconnected
        /// through, in that direction.</summary>
        float FlankExtent(int i, int dir, bool wantMax)
        {
            float extreme = Y[i];
            float best = Y[i];
            for (int k = i + dir; k >= 0 && k < SampleCount; k += dir)
            {
                if (wantMax) { if (Y[k] < best) best = Y[k]; if (Y[k] > extreme) break; }
                else         { if (Y[k] > best) best = Y[k]; if (Y[k] < extreme) break; }
            }
            return best;
        }
    }
}
