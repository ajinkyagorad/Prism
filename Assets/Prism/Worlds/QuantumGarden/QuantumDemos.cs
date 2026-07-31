using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using UnityEngine;

namespace Prism.Worlds.QuantumGarden
{
    /// <summary>
    /// The two-point-source interference sum shared by SuperpositionDemo and MeasurementDemo, so
    /// the two pocket demonstrations run the identical honest formula rather than two copies that
    /// could quietly drift apart. Point sources, not slits (these are hand-sized objects, not the
    /// apparatus in the world) — ordinary spherical-wave amplitude, 1/r.
    /// </summary>
    static class DemoWave
    {
        public static void AddSource(Vector3 source, Vector3 point, float wavelength, ref float re, ref float im)
        {
            float r = Vector3.Distance(source, point);
            float k = 2f * Mathf.PI / Mathf.Max(wavelength, 1e-4f);
            float amp = 1f / Mathf.Max(r, 0.05f);
            float phase = k * r;
            re += amp * Mathf.Cos(phase);
            im += amp * Mathf.Sin(phase);
        }
    }

    /// <summary>
    /// SUPERPOSITION — every open path adds an amplitude, continuously, never collapsing.
    ///
    /// Two coherent point sources; a row of pillars reads out the combined amplitude along a line
    /// beneath them. Pillar HEIGHT is the Born-rule intensity |amplitude|^2; pillar COLOUR is the
    /// amplitude's PHASE mapped onto the spectral ramp — the same dual encoding the Quantum Garden
    /// world itself uses, so a learner who has stood in that world recognises this instantly. The
    /// free hand drags the second source around; the whole pattern reshapes live. Nothing here ever
    /// resolves to one outcome — that is deliberately Measurement's demonstration, not this one.
    /// </summary>
    [ConceptDemoFor("superposition")]
    public class SuperpositionDemo : ConceptDemo
    {
        const int Bins = 13;
        const float Lambda = 0.16f;
        const float RowY = -0.7f;

        static readonly Vector3 SourceAHome = new Vector3(-0.28f, 0.55f, 0.18f);

        Transform _sourceB;
        Transform[] _bins;
        Material[] _binMats;
        readonly float[] _intensity = new float[Bins];
        readonly float[] _phase = new float[Bins];
        Vector3 _posB = new Vector3(0.28f, 0.55f, 0.18f);

        public override void Build()
        {
            Ball(0.05f, PrismPalette.Cyan, 0.7f, "sourceA").localPosition = SourceAHome;
            _sourceB = Ball(0.05f, PrismPalette.Mint, 0.7f, "sourceB");
            _sourceB.localPosition = _posB;

            _bins = new Transform[Bins];
            _binMats = new Material[Bins];
            for (int i = 0; i < Bins; i++)
            {
                _binMats[i] = FlatMaterial(PrismPalette.Warm, 0.5f);
                _bins[i] = Body(PrismMesh.Icosphere(1), 1f, _binMats[i], $"bin{i}");
            }
        }

        static float BinX(int i) => Mathf.Lerp(-0.85f, 0.85f, i / (float)(Bins - 1));

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
            {
                _posB.x = Mathf.Clamp(hand.x, -0.85f, 0.85f);
                _posB.z = Mathf.Clamp(hand.z, -0.30f, 0.60f);
            }
            _sourceB.localPosition = _posB;

            float maxI = 1e-6f;
            for (int i = 0; i < Bins; i++)
            {
                var p = new Vector3(BinX(i), RowY, 0f);
                float re = 0f, im = 0f;
                DemoWave.AddSource(SourceAHome, p, Lambda, ref re, ref im);
                DemoWave.AddSource(_posB, p, Lambda, ref re, ref im);
                _intensity[i] = re * re + im * im;
                _phase[i] = Mathf.Atan2(im, re);
                if (_intensity[i] > maxI) maxI = _intensity[i];
            }

            for (int i = 0; i < Bins; i++)
            {
                float h = Mathf.Clamp01(_intensity[i] / maxI) * 0.62f;
                _bins[i].localPosition = new Vector3(BinX(i), RowY + h * 0.5f, 0f);
                _bins[i].localScale = new Vector3(0.09f, Mathf.Max(h, 0.006f), 0.09f);

                // Phase -> spectral ramp: two contributions arriving out of phase sit on opposite
                // sides of the ramp, and exactly there the pillar also goes low -- cancellation
                // made visible as a meeting of colour, not just an absence of height.
                float hue = Mathf.Repeat(_phase[i] / (2f * Mathf.PI) + 0.5f, 1f);
                _binMats[i].SetColor("_Tint", PrismPalette.Spectral(hue));
            }
        }
    }

    /// <summary>
    /// MEASUREMENT — the learner's own pinch collapses a spread to one definite outcome.
    ///
    /// The same two-source interference as Superposition, but the apparatus is fixed and the
    /// learner does not see the pattern directly. Each pinch draws ONE sample from the real
    /// |amplitude|^2 by rejection sampling — an honest Born-rule draw, not a look-alike curve —
    /// and a marker pops to that position. Repeat it, and the growing histogram is the only way
    /// the hidden distribution ever becomes visible, which is the actually honest situation: you
    /// do not get the wave function; you get outcomes, and their statistics ARE the wave function.
    /// </summary>
    [ConceptDemoFor("measurement")]
    public class MeasurementDemo : ConceptDemo
    {
        const int Bins = 13;
        const float Lambda = 0.15f;
        const float RowY = -0.7f;
        static readonly Vector3 SourceA = new Vector3(-0.30f, 0.55f, 0.18f);
        static readonly Vector3 SourceB = new Vector3(0.30f, 0.55f, 0.18f);

        Transform[] _bins;
        Material[] _binMats;
        readonly int[] _counts = new int[Bins];
        Transform _collapseMarker;
        bool _wasPinching;
        float _maxIntensity;

        public override void Build()
        {
            Ball(0.045f, PrismPalette.Lavender, 0.6f, "sourceA").localPosition = SourceA;
            Ball(0.045f, PrismPalette.Lavender, 0.6f, "sourceB").localPosition = SourceB;

            _bins = new Transform[Bins];
            _binMats = new Material[Bins];
            for (int i = 0; i < Bins; i++)
            {
                _binMats[i] = FlatMaterial(new Color(0.55f, 0.55f, 0.58f), 0.4f);
                _bins[i] = Body(PrismMesh.Icosphere(1), 1f, _binMats[i], $"bin{i}");
            }

            var collapseMat = FlatMaterial(PrismPalette.Gold, 0.9f);
            _collapseMarker = Body(PrismMesh.Icosphere(2), 0.05f, collapseMat, "collapse");
            _collapseMarker.gameObject.SetActive(false);

            // The apparatus never changes in this demo, so its peak only needs finding once —
            // unlike the world proper, where slits move and the peak is re-scanned live. Scanned
            // far finer than the 13 display bins: at this geometry the fringe spacing is only
            // about twice the bin spacing, so sampling the envelope at bin resolution alone could
            // miss a true peak sitting between two bins and understate it -- which would bias the
            // rejection sampler toward over-accepting near that underestimated peak. 80 samples
            // comfortably resolves every fringe; this runs once, not per frame.
            _maxIntensity = 1e-6f;
            const int scan = 80;
            for (int i = 0; i <= scan; i++)
            {
                float x = Mathf.Lerp(-0.85f, 0.85f, i / (float)scan);
                float v = IntensityAt(x);
                if (v > _maxIntensity) _maxIntensity = v;
            }
            _maxIntensity *= 1.15f;
        }

        static float BinX(int i) => Mathf.Lerp(-0.85f, 0.85f, i / (float)(Bins - 1));

        static float IntensityAt(float x)
        {
            var p = new Vector3(x, RowY, 0f);
            float re = 0f, im = 0f;
            DemoWave.AddSource(SourceA, p, Lambda, ref re, ref im);
            DemoWave.AddSource(SourceB, p, Lambda, ref re, ref im);
            return re * re + im * im;
        }

        protected override void OnTick(float dt)
        {
            bool pinching = FreePinch > 0.6f;
            if (pinching && !_wasPinching)
            {
                float x = 0f;
                for (int tries = 0; tries < 32; tries++)
                {
                    float cand = Random.Range(-0.85f, 0.85f);
                    float u = Random.Range(0f, _maxIntensity);
                    if (u <= IntensityAt(cand)) { x = cand; break; }
                }

                int bin = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(-0.85f, 0.85f, x) * (Bins - 1)), 0, Bins - 1);
                _counts[bin]++;

                _collapseMarker.gameObject.SetActive(true);
                _collapseMarker.localPosition = new Vector3(BinX(bin), RowY + 0.14f, 0f);
                _collapseMarker.localScale = Vector3.one * 0.085f;   // a pop on arrival
                Voice?.Settle(transform.position, 0.3f, 1.3f + bin * 0.04f);
            }
            _wasPinching = pinching;

            _collapseMarker.localScale = Vector3.Lerp(_collapseMarker.localScale, Vector3.one * 0.05f, dt * 5f);

            int peak = 1;
            for (int i = 0; i < Bins; i++) peak = Mathf.Max(peak, _counts[i]);

            for (int i = 0; i < Bins; i++)
            {
                float h = Mathf.Clamp01(_counts[i] / (float)peak) * 0.58f;
                _bins[i].localPosition = new Vector3(BinX(i), RowY + h * 0.5f, 0f);
                _bins[i].localScale = new Vector3(0.085f, Mathf.Max(h, 0.006f), 0.085f);
                _binMats[i].SetColor("_Tint", _counts[i] > 0 ? PrismPalette.Cyan : new Color(0.55f, 0.55f, 0.58f));
            }
        }
    }

    /// <summary>
    /// QUANTISATION — try to hold it between two allowed states, and find that you cannot.
    ///
    /// A string fixed at both ends can only actually stand in an integer number of half
    /// wavelengths; there is no such thing as a stable half-integer mode on a fixed-fixed string.
    /// The free hand's height sets a CONTINUOUS candidate wavenumber, but the drawn shape always
    /// uses the nearest INTEGER mode -- what changes continuously is whether the string can hold
    /// full amplitude and stay still. Near an allowed mode it stands tall and settles, with a
    /// consonance marking the catch; exactly between two modes its amplitude collapses toward zero
    /// and it visibly shivers. This is the same boundary-condition mechanism that quantises the
    /// energy levels of a bound particle -- made tactile rather than asserted.
    /// </summary>
    [ConceptDemoFor("quantisation")]
    public class QuantisationDemo : ConceptDemo
    {
        const float HalfLength = 0.8f;
        const int Samples = 40;
        const float KMin = 0.75f, KMax = 5.25f;

        CurveView _string;
        float _kWant = 2f;
        int _lastSnapped = -1;
        readonly List<Vector3> _pts = new List<Vector3>(Samples);

        public override void Build()
        {
            _string = Curve(Tint, 0.014f, 3f);
            Ball(0.04f, PrismPalette.Warm, 0.5f, "wallLeft").localPosition = new Vector3(-HalfLength, 0f, 0f);
            Ball(0.04f, PrismPalette.Warm, 0.5f, "wallRight").localPosition = new Vector3(HalfLength, 0f, 0f);
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
            {
                float t = Mathf.InverseLerp(-0.7f, 0.7f, hand.y);
                _kWant = Mathf.Lerp(KMin, KMax, Mathf.Clamp01(t));
            }

            int nAllowed = Mathf.Clamp(Mathf.RoundToInt(_kWant), 1, 5);
            float mismatch = Mathf.Abs(_kWant - nAllowed);            // 0 .. 0.5
            float snap = 1f - Mathf.Clamp01(mismatch / 0.5f);         // 1 on an allowed mode, 0 exactly between

            float amp = Mathf.Lerp(0.06f, 0.34f, snap);
            float jitter = (1f - snap) * 0.05f;

            _pts.Clear();
            for (int i = 0; i < Samples; i++)
            {
                float u = i / (float)(Samples - 1);
                float x = Mathf.Lerp(-HalfLength, HalfLength, u);
                float shake = jitter * Mathf.Sin(Age * 37f + i * 2.3f);
                float y = amp * Mathf.Sin(nAllowed * Mathf.PI * u) + shake;
                _pts.Add(new Vector3(x, y, 0f));
            }
            _string.Set(_pts);
            _string.Mat.SetColor("_Tint", Color.Lerp(PrismPalette.Coral, Tint, snap));

            if (snap > 0.9f && nAllowed != _lastSnapped)
            {
                _lastSnapped = nAllowed;
                Voice?.Consonance(transform.position, 0.3f, 1f + nAllowed * 0.12f);
            }
            else if (snap < 0.5f) _lastSnapped = -1;
        }
    }
}
