using UnityEngine;

namespace Prism.Worlds.SoundSculptor
{
    /// <summary>
    /// The arithmetic this whole world is built on, kept in one place the way PrismScale keeps
    /// orbital mechanics' one derived constant in one place.
    ///
    /// A taut string fixed at both ends of length L, carrying a wave at speed v, resonates at
    /// f_n = n * v / (2L) for a positive integer mode n (n half-wavelengths fit between the fixed
    /// ends). Every string in this world shares the SAME wave speed — as if they were the same
    /// material under the same tension — which is what makes the headline honesty of the world
    /// work: a simple ratio of LENGTHS is a simple ratio of FREQUENCIES, exactly inverted, with
    /// nothing else in play. A learner who can eyeball "these two lengths look like 2:1" has
    /// already predicted the pitch relationship correctly.
    ///
    /// v is derived, not chosen, the same way PrismScale derives mu: pick one reference length and
    /// the frequency it should sound at, and the wave speed falls out.
    /// </summary>
    public static class SoundScale
    {
        /// <summary>Reference string length used to derive the wave speed, metres.</summary>
        public const float ReferenceLength = 0.30f;

        /// <summary>Frequency that length should sound at in its fundamental — A3, the same low
        /// root PrismVoice's own consonance and tension tones use, so this world's ear-reference
        /// matches the companion's.</summary>
        public const float ReferenceFrequency = 220f;

        /// <summary>Wave speed shared by every string, m/s. Derived: v = 2 * L_ref * f_ref.</summary>
        public const float WaveSpeed = 2f * ReferenceLength * ReferenceFrequency;

        /// <summary>
        /// Hard bounds on string length, both for safety and for reach. A learner's hands can
        /// close to almost nothing, and f = v/(2L) diverges as L to 0 — clamping length rather
        /// than frequency keeps the visible shape and the audible pitch always agreeing (the
        /// alternative, clamping frequency only, would let the drawn string keep shrinking past
        /// the point where the sound stops following it, which is exactly the kind of dishonesty
        /// this world exists to avoid). The resulting frequency range is roughly 106-660 Hz,
        /// comfortably within arm's reach of the anchor (see SoundSculptorWorld.Reach).
        /// </summary>
        public const float MinLength = 0.10f;
        public const float MaxLength = 0.62f;

        /// <summary>Frequency of mode n on a string of the given length. n = 1 is the fundamental.</summary>
        public static float FrequencyOf(float length, int mode = 1) =>
            Mathf.Max(1, mode) * WaveSpeed / (2f * Mathf.Max(length, 1e-4f));

        /// <summary>Length that puts mode n at the given frequency. Inverse of <see cref="FrequencyOf"/>.</summary>
        public static float LengthFor(float frequency, int mode = 1) =>
            Mathf.Max(1, mode) * WaveSpeed / (2f * Mathf.Max(frequency, 1e-4f));

        /// <summary>
        /// COLOUR ENCODING for this world: hue = pitch class, the position within one octave,
        /// wrapped so a frequency and its octave (2x, 4x, half, ...) always land on the exact same
        /// point of the spectral ramp. This is chosen over, say, hue = raw frequency because pitch
        /// class is the thing that is actually invariant under "the same note, a register away" —
        /// an octave doubling is inaudible as a change of KIND, only of register, and the colour
        /// says so before anyone is told why. See NOTES.md.
        /// </summary>
        public static float PitchClass(float frequencyHz) =>
            Mathf.Repeat(Mathf.Log(Mathf.Max(frequencyHz, 1e-4f) / ReferenceFrequency, 2f), 1f);

        /// <summary>A small-integer frequency ratio p:q (p >= q, coprime), with its common name.</summary>
        public readonly struct Ratio
        {
            public readonly int P, Q;
            public readonly string Name;
            public Ratio(int p, int q, string name) { P = p; Q = q; Name = name; }
            public float Value => (float)P / Q;
        }

        /// <summary>
        /// The just-intonation ratios this world recognises by ear. Deliberately a short list of
        /// musically simple fractions rather than every rational number — "simple" is the whole
        /// point, and a table that accepted 47:31 would not be teaching that.
        /// </summary>
        public static readonly Ratio[] SimpleRatios =
        {
            new Ratio(1, 1, "unison"),
            new Ratio(6, 5, "minor third"),
            new Ratio(5, 4, "major third"),
            new Ratio(4, 3, "fourth"),
            new Ratio(3, 2, "fifth"),
            new Ratio(8, 5, "minor sixth"),
            new Ratio(5, 3, "major sixth"),
            new Ratio(2, 1, "octave"),
        };

        public struct Coincidence
        {
            public Ratio Ratio;
            /// <summary>Hz. The real difference between the coinciding harmonics — see below.</summary>
            public float BeatHz;
        }

        /// <summary>
        /// The physics behind "simple ratios lock, complex ones beat".
        ///
        /// For a candidate ratio p:q (p >= q) to be what is actually sounding, the q-th harmonic of
        /// the higher tone must nearly coincide with the p-th harmonic of the lower tone (that is
        /// what p:q MEANS: q * fHigh = p * fLow). The gap between those two real harmonics,
        /// |q*fHigh - p*fLow|, is a beat frequency in the ordinary sense — it is the difference of
        /// two real, physically present frequencies (both are genuinely partials of the additive
        /// synthesis in SoundSynth) — and it is exactly zero when the ratio is exact. At p=q=1 this
        /// formula IS the textbook "beat frequency equals the difference of the two frequencies",
        /// because the nearest coincidence to a unison is the fundamentals themselves; the general
        /// case is the same idea one level deeper. This function searches the whole table and
        /// returns whichever candidate coincides most closely, which is also, physically, the
        /// candidate doing the most to determine how consonant the pair currently sounds.
        /// </summary>
        public static Coincidence NearestCoincidence(float f1, float f2)
        {
            float lo = Mathf.Min(f1, f2);
            float hi = Mathf.Max(f1, f2);

            Coincidence best = new Coincidence { Ratio = SimpleRatios[0], BeatHz = float.MaxValue };
            for (int i = 0; i < SimpleRatios.Length; i++)
            {
                var r = SimpleRatios[i];
                float beat = Mathf.Abs(r.Q * hi - r.P * lo);
                if (beat < best.BeatHz) best = new Coincidence { Ratio = r, BeatHz = beat };
            }
            return best;
        }

        /// <summary>Below this, the nearest coincidence is close enough that the ear hears "locked".</summary>
        public const float LockBeatHz = 1.5f;

        /// <summary>The window in which the beat reads as a clear, countable pulse rather than an
        /// imperceptibly slow drift (below) or a rough buzz indistinguishable from noise (above).</summary>
        public const float BeatMinHz = 2.5f;
        public const float BeatMaxHz = 18f;

        public static bool IsLocked(float beatHz) => beatHz < LockBeatHz;
        public static bool IsBeating(float beatHz) => beatHz > BeatMinHz && beatHz < BeatMaxHz;

        /// <summary>
        /// Soft tuning assist: if `rawLength` is close (within `captureFraction` of `otherLength`)
        /// to a length that would put it in a simple ratio with `otherLength`, return that exact
        /// target length; otherwise return `rawLength` unchanged. The caller eases toward this
        /// rather than snapping to it, so it feels like the string wants to settle rather than
        /// teleporting — see SoundString.ApplyLengthConstraint. This exists because hand tracking
        /// jitter of a few millimetres is a large fraction of the length change that separates
        /// "locked" from "just missed it" (see NOTES.md), so exact-by-hand tuning would be an
        /// exercise in frustration rather than in hearing.
        /// </summary>
        public static float MagnetLength(float rawLength, float otherLength, float captureFraction = 0.045f)
        {
            float best = rawLength;
            float bestRel = captureFraction;

            for (int i = 0; i < SimpleRatios.Length; i++)
            {
                var r = SimpleRatios[i];

                float above = otherLength * r.P / r.Q;
                float relAbove = Mathf.Abs(rawLength - above) / Mathf.Max(otherLength, 1e-4f);
                if (relAbove < bestRel) { bestRel = relAbove; best = above; }

                float below = otherLength * r.Q / r.P;
                float relBelow = Mathf.Abs(rawLength - below) / Mathf.Max(otherLength, 1e-4f);
                if (relBelow < bestRel) { bestRel = relBelow; best = below; }
            }
            return best;
        }
    }
}
