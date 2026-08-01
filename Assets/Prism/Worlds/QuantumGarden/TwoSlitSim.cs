using UnityEngine;

namespace Prism.Worlds.QuantumGarden
{
    /// <summary>
    /// The honest physics of the two-slit apparatus: real complex amplitudes, summed over
    /// discretised paths, with real path-length phase. Every number the world displays —
    /// fringe positions, the amplitude field, the distribution each detection is drawn from —
    /// comes out of this one sum. There is no separate "fringe spacing" formula standing in for
    /// the picture; the picture and the physics are the same computation.
    ///
    /// THE MODEL, stated plainly:
    ///   - A point source emits coherently at a single wavelength.
    ///   - Each slit is a finite-width APERTURE, discretised into <see cref="SubSourcesPerSlit"/>
    ///     evenly spaced coherent line elements — a small, direct Fresnel-Kirchhoff sum, not a
    ///     shortcut. A zero-width slit cannot diffract, so this discretisation is what makes the
    ///     single-slit envelope real physics rather than an assumed curve.
    ///   - Slits are treated as infinite LINES (cylindrical wave sources: amplitude ~ 1/sqrt(r)),
    ///     which is the physically correct treatment for a real slit — a gap much taller than it
    ///     is wide. The consequence, used honestly rather than hidden: the whole problem is
    ///     exactly 2D (x = across the slits, z = along the beam) and the pattern does not depend
    ///     on y. That is also why detections land at a uniformly random y on the screen: the
    ///     physics genuinely has nothing to say about y.
    ///   - Path length is computed EXACTLY (source to sub-source, sub-source to screen point),
    ///     never approximated with a far-field/paraxial formula. Dragging one slit off-centre
    ///     while the source stays fixed therefore genuinely shifts the whole fringe pattern
    ///     sideways — a real optical-path-difference effect that falls out of the sum for free,
    ///     rather than something scripted in.
    ///
    /// APPROXIMATIONS — see NOTES.md for the full account:
    ///   - Slit WIDTH is a fixed apparatus constant, not learner-adjustable (only separation and
    ///     wavelength are, matching the brief).
    ///   - The source sits fixed on the optical axis; only the two slits move.
    ///   - Detection ARRIVAL RATE (how many land per second) is a chosen constant pace, not
    ///     derived from total flux, which would honestly differ between one- and two-slit
    ///     configurations. The RATE is a pacing choice; the DISTRIBUTION each one is drawn from
    ///     is the real thing and is never faked.
    /// </summary>
    public class TwoSlitSim
    {
        public const int SubSourcesPerSlit = 9;

        // ---- fixed apparatus geometry, local to the world anchor (metres) ----
        public const float SourceZ = -0.16f;
        public const float SlitZ = 0f;
        public const float ScreenZ = 0.34f;
        public const float SlitWidth = 0.012f;

        public const float WavelengthMin = 0.008f;
        public const float WavelengthMax = 0.030f;
        public const float SlitXMin = -0.08f;
        public const float SlitXMax = 0.08f;
        public const float MinSeparation = 0.03f;

        public const float ScreenHalfWidth = 0.26f;
        public const float ScreenHalfHeight = 0.16f;

        // ---- learner-adjustable state ----
        public float LeftSlitX { get; private set; } = -0.03f;
        public float RightSlitX { get; private set; } = 0.03f;
        public bool LeftOpen { get; private set; } = true;
        public bool RightOpen { get; private set; } = true;
        public float Wavelength { get; private set; } = 0.016f;

        /// <summary>Centre-to-centre slit separation, metres. Derived, never stored twice.</summary>
        public float Separation => Mathf.Abs(RightSlitX - LeftSlitX);

        public bool SetLeftSlitX(float x)
        {
            x = Mathf.Clamp(x, SlitXMin, RightSlitX - MinSeparation);
            if (Mathf.Approximately(x, LeftSlitX)) return false;
            LeftSlitX = x; MarkDirty(); return true;
        }

        public bool SetRightSlitX(float x)
        {
            x = Mathf.Clamp(x, LeftSlitX + MinSeparation, SlitXMax);
            if (Mathf.Approximately(x, RightSlitX)) return false;
            RightSlitX = x; MarkDirty(); return true;
        }

        public bool SetWavelength(float lambda)
        {
            lambda = Mathf.Clamp(lambda, WavelengthMin, WavelengthMax);
            if (Mathf.Approximately(lambda, Wavelength)) return false;
            Wavelength = lambda; MarkDirty(); return true;
        }

        /// <summary>Returns true only on an actual open/closed transition (an edge, not a level).</summary>
        public bool SetOpen(bool left, bool open)
        {
            if (left) { if (LeftOpen == open) return false; LeftOpen = open; }
            else { if (RightOpen == open) return false; RightOpen = open; }
            MarkDirty();
            return true;
        }

        /// <summary>
        /// Fringe spacing from the standard far-field formula, lambda*L/d. Used only to lay out
        /// the Apply-stage target ghost and to judge that challenge — the pattern the learner
        /// actually sees always comes from the full sum below, never from this approximation.
        /// </summary>
        public float PredictedFringeSpacing()
        {
            float d = Separation;
            if (d < 1e-5f) return float.PositiveInfinity;
            return Wavelength * (ScreenZ - SlitZ) / d;
        }

        float _maxIntensity = 1f;
        bool _maxDirty = true;

        void MarkDirty() => _maxDirty = true;

        /// <summary>Complex amplitude at screen position x. y does not enter; see class remarks.</summary>
        public void Amplitude(float x, out float re, out float im)
        {
            re = 0f; im = 0f;
            float k = (2f * Mathf.PI) / Mathf.Max(Wavelength, 1e-5f);
            AccumulateSlit(LeftOpen, LeftSlitX, x, k, ref re, ref im);
            AccumulateSlit(RightOpen, RightSlitX, x, k, ref re, ref im);
        }

        static void AccumulateSlit(bool open, float slitX, float screenX, float k, ref float re, ref float im)
        {
            if (!open) return;

            const float sourceToSlitZ = SlitZ - SourceZ;
            const float slitToScreenZ = ScreenZ - SlitZ;
            const float weight = SlitWidth / SubSourcesPerSlit;

            for (int j = 0; j < SubSourcesPerSlit; j++)
            {
                float t = (float)j / (SubSourcesPerSlit - 1) - 0.5f;   // -0.5 .. +0.5 across the slit
                float sx = slitX + t * SlitWidth;

                float srcDist = Mathf.Sqrt(sx * sx + sourceToSlitZ * sourceToSlitZ);
                float dx = screenX - sx;
                float scrDist = Mathf.Sqrt(dx * dx + slitToScreenZ * slitToScreenZ);

                float amp = weight / Mathf.Sqrt(Mathf.Max(scrDist, 0.02f));
                float phase = k * (srcDist + scrDist);

                re += amp * Mathf.Cos(phase);
                im += amp * Mathf.Sin(phase);
            }
        }

        /// <summary>The Born rule: probability density is the squared magnitude of the amplitude.</summary>
        public float Intensity(float x)
        {
            Amplitude(x, out float re, out float im);
            return re * re + im * im;
        }

        /// <summary>Recomputes and caches the pattern's current peak, for sampling and display.</summary>
        public float MaxIntensity()
        {
            if (!_maxDirty) return _maxIntensity;
            _maxDirty = false;

            float best = 1e-9f;
            const int scan = 96;
            for (int i = 0; i <= scan; i++)
            {
                float x = Mathf.Lerp(-ScreenHalfWidth, ScreenHalfWidth, (float)i / scan);
                float v = Intensity(x);
                if (v > best) best = v;
            }
            _maxIntensity = best * 1.12f;   // headroom so rejection sampling terminates quickly
            return _maxIntensity;
        }

        /// <summary>
        /// One Born-rule sample: a real detection, drawn from |psi(x)|^2 by rejection sampling
        /// against the pattern's own peak. This is not a look-alike distribution — it is the
        /// actual function above, sampled honestly.
        /// </summary>
        public float SampleDetectionX()
        {
            float max = Mathf.Max(MaxIntensity(), 1e-9f);
            for (int tries = 0; tries < 48; tries++)
            {
                float x = Random.Range(-ScreenHalfWidth, ScreenHalfWidth);
                float u = Random.Range(0f, max);
                if (u <= Intensity(x)) return x;
            }
            // Exceedingly unlikely with the headroom above; still returns a legitimate screen
            // position rather than stalling emission if it is ever hit.
            return Random.Range(-ScreenHalfWidth, ScreenHalfWidth);
        }

        /// <summary>
        /// Writes the currently open sub-sources as (localX, distanceFromSource, weight, unused)
        /// into <paramref name="dest"/> and returns how many were written. This is the exact
        /// discretisation <see cref="Amplitude"/> sums over, exposed so the live field shader
        /// (Prism/QuantumField) can compute the identical sum per pixel — the picture on the
        /// screen and the number used for sampling are never allowed to drift apart because they
        /// are, deliberately, the same sum evaluated twice.
        /// </summary>
        public int FillActiveSources(Vector4[] dest)
        {
            int n = FillSlit(LeftOpen, LeftSlitX, dest, 0);
            n = FillSlit(RightOpen, RightSlitX, dest, n);
            return n;
        }

        static int FillSlit(bool open, float slitX, Vector4[] dest, int n)
        {
            if (!open) return n;
            const float sourceToSlitZ = SlitZ - SourceZ;
            const float weight = SlitWidth / SubSourcesPerSlit;

            for (int j = 0; j < SubSourcesPerSlit; j++)
            {
                if (n >= dest.Length) break;
                float t = (float)j / (SubSourcesPerSlit - 1) - 0.5f;
                float sx = slitX + t * SlitWidth;
                float srcDist = Mathf.Sqrt(sx * sx + sourceToSlitZ * sourceToSlitZ);
                dest[n] = new Vector4(sx, srcDist, weight, 0f);
                n++;
            }
            return n;
        }
    }
}
