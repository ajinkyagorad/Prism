using System.Collections.Generic;
using UnityEngine;

namespace Prism.Worlds.SoundSculptor
{
    /// <summary>Which grab point on a string a hand is holding.</summary>
    public enum HandleKind { EndA, EndB, Mid }

    /// <summary>
    /// One taut, fixed-fixed vibrating string: two endpoints in world space and a mode number, and
    /// everything else — frequency, drawn shape, colour — computed from exactly those, so the
    /// visible mode count and the audible pitch can never disagree.
    /// </summary>
    public class SoundString
    {
        public string Name;
        public Vector3 EndA;
        public Vector3 EndB;
        public int Mode = 1;

        /// <summary>False until this string has been pulled out of its cradle. An unsummoned
        /// string is silent and invisible, both ends resting together at <see cref="CradleLocalOffset"/>.</summary>
        public bool Summoned;
        public Vector3 CradleLocalOffset;

        /// <summary>0..1, this string's current audible volume as a fraction of its own maximum.
        /// Drives the visible amplitude so a not-yet-audible string does not show a full-strength
        /// wave, and a fading one visibly settles rather than popping off.</summary>
        public float Loudness01;

        public Transform View;
        public Mesh TubeMesh;
        public Material Mat;
        public SoundSynth.ToneVoice Voice;

        /// <summary>Visual half-width at an antinode, metres. A display choice, not a physical
        /// string displacement — a real string moves under a millimetre at these lengths, which
        /// would be invisible. The RULE (radius proportional to |sin(mode*pi*x/L)|) is exact; only
        /// the scale is stylised, the same way OrbitalWorld draws a 4 cm planet honestly rather
        /// than to scale.</summary>
        public float Amplitude = 0.017f;

        public float Length => Vector3.Distance(EndA, EndB);
        public float Frequency => SoundScale.FrequencyOf(Length, Mode);
        public Vector3 Midpoint => (EndA + EndB) * 0.5f;

        const int Segments = 32;
        const int Sides = 8;

        readonly List<Vector3> _verts = new List<Vector3>((Segments + 1) * (Sides + 1));
        readonly List<Vector2> _uv0   = new List<Vector2>((Segments + 1) * (Sides + 1));
        readonly List<Vector2> _uv1   = new List<Vector2>((Segments + 1) * (Sides + 1));
        readonly List<int>     _tris  = new List<int>(Segments * Sides * 6);

        /// <summary>
        /// Rebuild the tube from the current endpoints. Radius at each point along the axis is the
        /// real standing-wave ENVELOPE, amplitude * |sin(mode*pi*x/L)| — the locus of maximum
        /// excursion a point on the string reaches over one full cycle, not an animation of any one
        /// instant of it. This is a deliberate honesty choice, not a simplification: at audible
        /// frequencies (tens to hundreds of Hz) no real-time headset display can honestly redraw
        /// individual cycles at 72-90 Hz without aliasing into nonsense, and the human eye does not
        /// see individual cycles of a plucked string either — it integrates to exactly this
        /// envelope. n lobes for mode n, pinched to (near) zero at every node, including both fixed
        /// ends, so counting the lobes IS reading off the mode number.
        /// </summary>
        public void RebuildMesh()
        {
            Vector3 axis = EndB - EndA;
            float len = axis.magnitude;
            if (len < 1e-4f || TubeMesh == null)
            {
                if (TubeMesh != null) TubeMesh.Clear();
                return;
            }
            axis /= len;

            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, axis);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.forward, axis);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.right;
            up.Normalize();
            Vector3 right = Vector3.Cross(up, axis).normalized;

            float pitchClass = SoundScale.PitchClass(Frequency);
            float amp = Amplitude * Mathf.Clamp01(0.15f + 0.85f * Loudness01);

            _verts.Clear(); _uv0.Clear(); _uv1.Clear(); _tris.Clear();

            for (int i = 0; i <= Segments; i++)
            {
                float x01 = (float)i / Segments;
                Vector3 centre = Vector3.Lerp(EndA, EndB, x01);
                float env = Mathf.Abs(Mathf.Sin(Mode * Mathf.PI * x01));
                float radius = Mathf.Max(amp * env, 0.0005f);

                for (int s = 0; s <= Sides; s++)
                {
                    float ang = (float)s / Sides * Mathf.PI * 2f;
                    Vector3 n = right * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                    _verts.Add(centre + n * radius);
                    // Constant (0, 0.5): no age-fade along the tube (this is not a trail, it is
                    // a persistent shape rebuilt fresh every frame) and uniform cross brightness.
                    _uv0.Add(new Vector2(0f, 0.5f));
                    // Colour IS pitch class, constant along the whole string — see SoundScale and
                    // NOTES.md. Prism/Trail maps uv1.x through the spectral ramp for us.
                    _uv1.Add(new Vector2(pitchClass, 0f));
                }
            }

            int stride = Sides + 1;
            for (int i = 0; i < Segments; i++)
            {
                for (int s = 0; s < Sides; s++)
                {
                    int i0 = i * stride + s, i1 = i0 + 1, i2 = i0 + stride, i3 = i2 + 1;
                    _tris.Add(i0); _tris.Add(i2); _tris.Add(i1);
                    _tris.Add(i1); _tris.Add(i2); _tris.Add(i3);
                }
            }

            TubeMesh.Clear();
            TubeMesh.SetVertices(_verts);
            TubeMesh.SetUVs(0, _uv0);
            TubeMesh.SetUVs(1, _uv1);
            TubeMesh.SetTriangles(_tris, 0);
            TubeMesh.RecalculateBounds();
        }

        /// <summary>
        /// Keep the length inside safe, reachable bounds, and — while `magnetize` is true — ease it
        /// toward whichever nearby simple-ratio length would lock with `otherLength`. Always
        /// rewrites BOTH endpoints from (midpoint, axis, length), so the drawn string and the heard
        /// pitch are always exactly the same string, never a visual one and an audible one that
        /// have quietly drifted apart.
        /// </summary>
        public void ApplyLengthConstraint(float otherLength, bool magnetize, float dt)
        {
            Vector3 mid = Midpoint;
            Vector3 axis = EndB - EndA;
            float len = axis.magnitude;
            axis = len > 1e-5f ? axis / len : Vector3.right;

            float target = Mathf.Clamp(len, SoundScale.MinLength, SoundScale.MaxLength);
            if (magnetize && otherLength > 1e-4f)
            {
                float magnet = SoundScale.MagnetLength(target, otherLength);
                float rate = 1f - Mathf.Exp(-dt * 10f);
                target = Mathf.Lerp(target, magnet, rate);
            }

            EndA = mid - axis * (target * 0.5f);
            EndB = mid + axis * (target * 0.5f);
        }
    }
}
