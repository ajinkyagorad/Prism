using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using UnityEngine;

namespace Prism.Worlds.MathMotion
{
    /// <summary>
    /// DERIVATIVE — an arrow that IS the slope, riding a curve you can reshape.
    ///
    /// A bead paces the curve at a steady rate and carries an arrow: tangent to the path, longer
    /// where the curve is steeper. Pinch a point on the curve with the free hand and pull — the
    /// whole shape answers in real time, the same local edit the full Mathematics of Motion world
    /// is built on, only softer and in miniature. The arrow is deliberately the same shape as a
    /// moon's velocity arrow in the orbital world: a small arrow riding a moving body, tangent to
    /// where it is going. A learner who has thrown a moon should recognise this on sight.
    /// </summary>
    [ConceptDemoFor("derivative")]
    public class DerivativeDemo : ConceptDemo
    {
        const int N = 33;
        const float Dx = 2f / (N - 1);
        readonly float[] _y = new float[N];
        readonly float[] _d = new float[N];
        readonly List<Vector3> _pts = new List<Vector3>(N);

        CurveView _curve;
        Transform _bead;
        Material _beadMat;
        ArrowView _arrow;
        float _t;

        public override void Build()
        {
            for (int i = 0; i < N; i++)
                _y[i] = 0.30f * Mathf.Sin(Mathf.Lerp(-1f, 1f, i / (float)(N - 1)) * Mathf.PI * 1.3f);
            Recompute();

            _curve = Curve(Tint, 0.011f);
            _beadMat = FlatMaterial(PrismPalette.Warm, 0.85f);
            _bead = Body(PrismMesh.Icosphere(2), 0.032f, _beadMat, "bead");
            _arrow = MakeArrow(PrismPalette.Warm, 1.1f);
        }

        void Recompute()
        {
            _d[0] = (_y[1] - _y[0]) / Dx;
            for (int i = 1; i < N - 1; i++) _d[i] = (_y[i + 1] - _y[i - 1]) / (2f * Dx);
            _d[N - 1] = (_y[N - 1] - _y[N - 2]) / Dx;
        }

        static float Sample(float[] arr, float x)
        {
            float t = Mathf.Clamp((x + 1f) / Dx, 0f, N - 1.001f);
            int i0 = (int)t;
            return Mathf.Lerp(arr[i0], arr[Mathf.Min(i0 + 1, N - 1)], t - i0);
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var h) && FreePinch > 0.5f)
            {
                // A real local basis-function edit: a Gaussian bump nudged toward the hand every
                // frame — exactly the gesture the full curve-sculpting world runs on.
                float delta = (Mathf.Clamp(h.y, -0.6f, 0.6f) - Sample(_y, h.x)) * Mathf.Clamp01(dt * 6f);
                for (int i = 0; i < N; i++)
                {
                    float x = Mathf.Lerp(-1f, 1f, i / (float)(N - 1));
                    float w = Mathf.Exp(-(x - h.x) * (x - h.x) / (2f * 0.16f * 0.16f));
                    _y[i] = Mathf.Clamp(_y[i] + delta * w, -0.8f, 0.8f);
                }
                Recompute();
            }

            _pts.Clear();
            for (int i = 0; i < N; i++)
                _pts.Add(new Vector3(Mathf.Lerp(-1f, 1f, i / (float)(N - 1)), _y[i], 0f));
            _curve.Set(_pts);

            // The bead paces the curve on its own, so the arrow is always alive even empty-handed.
            _t += dt * 0.20f;
            float bx = Mathf.PingPong(_t, 2f) - 1f;
            float by = Sample(_y, bx);
            float slope = Sample(_d, bx);
            _bead.localPosition = new Vector3(bx, by, 0f);

            // Mint rising, coral falling — the same law the full world reads a curve by.
            Color c = slope >= 0f ? Color.Lerp(PrismPalette.Warm, PrismPalette.Mint, Mathf.Clamp01(slope))
                                  : Color.Lerp(PrismPalette.Warm, PrismPalette.Coral, Mathf.Clamp01(-slope));
            _beadMat.SetColor("_Tint", c);
            _arrow.Mat.SetColor("_Tint", c);
            _arrow.Aim(new Vector3(bx, by, 0f), new Vector3(1f, slope, 0f),
                      Mathf.Clamp(0.16f + Mathf.Abs(slope) * 0.12f, 0.12f, 0.36f));
        }
    }

    /// <summary>
    /// INTEGRAL — sweep a hand under a curve and watch the total accumulate.
    ///
    /// The free hand's position along a fixed curve sets how far the sweep has gone; a real
    /// trapezoidal sum of everything under the curve up to that point — full slices plus one
    /// honest partial slice at the sweep edge — grows or shrinks a crystal beside it, coloured by
    /// its own sign. Let go and the sweep keeps going on its own, so there is always a running
    /// total to watch rather than a frozen picture.
    /// </summary>
    [ConceptDemoFor("integral")]
    public class IntegralDemo : ConceptDemo
    {
        const int N = 33;
        const float Dx = 2f / (N - 1);
        readonly float[] _y = new float[N];
        readonly List<Vector3> _pts = new List<Vector3>(N);

        CurveView _curve;
        Transform _marker, _bar;
        Material _markerMat, _barMat;
        float _t;

        public override void Build()
        {
            for (int i = 0; i < N; i++)
                _y[i] = 0.28f * Mathf.Sin(Mathf.Lerp(-1f, 1f, i / (float)(N - 1)) * Mathf.PI * 1.6f - 0.4f);

            _pts.Clear();
            for (int i = 0; i < N; i++) _pts.Add(new Vector3(Mathf.Lerp(-1f, 1f, i / (float)(N - 1)), _y[i], 0f));
            _curve = Curve(Tint, 0.011f);
            _curve.Set(_pts);

            _markerMat = FlatMaterial(PrismPalette.Warm, 0.9f);
            _marker = Body(PrismMesh.Icosphere(2), 0.026f, _markerMat, "marker");
            _barMat = FlatMaterial(PrismPalette.Warm, 0.7f);
            _bar = Body(PrismMesh.Icosphere(1), 1f, _barMat, "bar");
        }

        static float Sample(float[] arr, float x)
        {
            float t = Mathf.Clamp((x + 1f) / Dx, 0f, N - 1.001f);
            int i0 = (int)t;
            return Mathf.Lerp(arr[i0], arr[Mathf.Min(i0 + 1, N - 1)], t - i0);
        }

        protected override void OnTick(float dt)
        {
            float sweepX;
            if (TryFreeLocal(out var h)) { sweepX = Mathf.Clamp(h.x, -1f, 1f); _t = (sweepX + 1f) * 0.5f; }
            else { _t = Mathf.Repeat(_t + dt * 0.08f, 1f); sweepX = _t * 2f - 1f; }

            // Real trapezoidal accumulation from the left edge to the sweep - full slices, plus
            // one honest partial slice at the boundary, exactly the rule the full world runs on.
            float raw = (sweepX + 1f) / Dx;
            int upto = Mathf.Clamp((int)raw, 0, N - 1);
            float area = 0f;
            for (int i = 1; i <= upto; i++) area += (_y[i - 1] + _y[i]) * 0.5f * Dx;
            if (upto < N - 1) area += (_y[upto] + Sample(_y, sweepX)) * 0.5f * (raw - upto) * Dx;

            float atSweep = Sample(_y, sweepX);
            _marker.localPosition = new Vector3(sweepX, atSweep, 0f);
            _markerMat.SetColor("_Tint", atSweep >= 0f
                ? Color.Lerp(PrismPalette.Warm, PrismPalette.Mint, Mathf.Clamp01(atSweep * 3f))
                : Color.Lerp(PrismPalette.Warm, PrismPalette.Coral, Mathf.Clamp01(-atSweep * 3f)));

            float barH = Mathf.Clamp(Mathf.Abs(area) * 1.1f, 0.02f, 0.85f);
            const float baseline = -0.6f;
            _bar.localPosition = new Vector3(0.74f, baseline + Mathf.Sign(area) * barH * 0.5f, 0f);
            _bar.localScale = new Vector3(0.10f, barH, 0.10f);
            _barMat.SetColor("_Tint", area >= 0f
                ? Color.Lerp(PrismPalette.Warm, PrismPalette.Mint, Mathf.Clamp01(Mathf.Abs(area) * 2.5f))
                : Color.Lerp(PrismPalette.Warm, PrismPalette.Coral, Mathf.Clamp01(Mathf.Abs(area) * 2.5f)));
        }
    }

    /// <summary>
    /// RATE OF CHANGE — feel how fast, before you ever see a slope.
    ///
    /// A level rises or falls; the free hand's height sets how fast, and which way. There is
    /// deliberately no graph to read here — this is the pre-formal, felt version of the idea
    /// Derivative later gives a tangent line and a formula. The faint sweeping trail behind the
    /// level is a rate of change too, quietly, for a learner who comes back to this one after
    /// visiting Derivative and starts to notice the trail's own slope is the story.
    /// </summary>
    [ConceptDemoFor("rate-of-change")]
    public class RateOfChangeDemo : ConceptDemo
    {
        const int H = 40;
        readonly float[] _hist = new float[H];
        readonly List<Vector3> _pts = new List<Vector3>(H);

        Transform _level;
        Material _levelMat;
        CurveView _trail;
        float _y, _rate, _writeT;
        int _head;

        public override void Build()
        {
            _levelMat = FlatMaterial(PrismPalette.Warm, 0.85f);
            _level = Body(PrismMesh.Icosphere(2), 0.05f, _levelMat, "level");
            _trail = Curve(PrismPalette.Warm, 0.006f);
        }

        protected override void OnTick(float dt)
        {
            _rate = TryFreeLocal(out var h) ? Mathf.Clamp(h.y, -1f, 1f) * 0.7f : Mathf.Sin(Age * 0.6f) * 0.35f;
            _y = Mathf.Clamp(_y + _rate * dt, -0.85f, 0.85f);
            _level.localPosition = new Vector3(-0.9f, _y, 0f);

            // Brighter and quicker to pulse the faster it is changing, so the rate is felt rather
            // than read - the entire point of a demonstration that is deliberately not a graph.
            float mag = Mathf.Abs(_rate);
            Color c = _rate >= 0f ? Color.Lerp(PrismPalette.Warm, PrismPalette.Mint, mag)
                                  : Color.Lerp(PrismPalette.Warm, PrismPalette.Coral, mag);
            _levelMat.SetColor("_Tint", c);
            _level.localScale = Vector3.one * (0.05f + mag * 0.02f) *
                                (1f + Mathf.Sin(Age * (4f + mag * 10f)) * 0.06f);

            // A circular buffer, written a few times a second - an oscilloscope sweep, not a
            // list that grows or gets rebuilt.
            _writeT += dt;
            if (_writeT > 0.05f) { _writeT = 0f; _hist[_head] = _y; _head = (_head + 1) % H; }

            _pts.Clear();
            for (int i = 0; i < H; i++)
            {
                float v = _hist[(_head + i) % H];
                _pts.Add(new Vector3(Mathf.Lerp(-0.72f, 0.55f, i / (float)(H - 1)), v * 0.4f + 0.35f, 0f));
            }
            _trail.Set(_pts);
            _trail.Mat.SetColor("_Tint", c);
        }
    }

    /// <summary>
    /// CRITICAL POINT — slide along the curve and feel it stop turning.
    ///
    /// The free hand scrubs a marker along a fixed curve. Its real slope is measured continuously
    /// by central difference at wherever the hand actually is — but nothing is shown except when
    /// that slope crosses zero: the marker snaps, brightens gold, and sounds a consonance,
    /// precisely at a peak, a valley or a flat shoulder. No number is ever printed; the point is
    /// to recognise the moment by feel, the way the full world's Explain stage asks a learner to
    /// predict one before it is ever measured for them.
    /// </summary>
    [ConceptDemoFor("critical-point")]
    public class CriticalPointDemo : ConceptDemo
    {
        const int N = 41;
        const float Dx = 2f / (N - 1);
        readonly float[] _y = new float[N];
        readonly List<Vector3> _pts = new List<Vector3>(N);

        CurveView _curve;
        Transform _marker;
        Material _markerMat;
        float _x;
        int _lastSnap = -99;

        public override void Build()
        {
            for (int i = 0; i < N; i++)
            {
                float x = Mathf.Lerp(-1f, 1f, i / (float)(N - 1));
                _y[i] = 0.30f * Mathf.Sin(x * Mathf.PI * 1.15f) + 0.10f * Mathf.Sin(x * Mathf.PI * 3.4f + 0.5f);
            }
            _pts.Clear();
            for (int i = 0; i < N; i++) _pts.Add(new Vector3(Mathf.Lerp(-1f, 1f, i / (float)(N - 1)), _y[i], 0f));
            _curve = Curve(Tint, 0.010f);
            _curve.Set(_pts);

            _markerMat = FlatMaterial(PrismPalette.Warm, 0.85f);
            _marker = Body(PrismMesh.Icosphere(2), 0.030f, _markerMat, "marker");
        }

        float Sample(float x)
        {
            float t = Mathf.Clamp((x + 1f) / Dx, 0f, N - 1.001f);
            int i0 = (int)t;
            return Mathf.Lerp(_y[i0], _y[Mathf.Min(i0 + 1, N - 1)], t - i0);
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var h)) _x = Mathf.Clamp(h.x, -1f, 1f);
            else { _x += dt * 0.12f; if (_x > 1f) _x -= 2f; }

            // Central difference taken fresh at whatever x the hand is actually at, not a lookup
            // into a precomputed table entry - a genuine measurement, made on demand.
            float slope = (Sample(_x + Dx) - Sample(_x - Dx)) / (2f * Dx);
            float snap = Mathf.Clamp01(1f - Mathf.Abs(slope) / 0.35f);

            float y = Sample(_x);
            _marker.localPosition = new Vector3(_x, y, 0f);
            _marker.localScale = Vector3.one * (0.030f * (1f + snap * 0.5f));
            _markerMat.SetColor("_Tint", Color.Lerp(Tint, PrismPalette.Gold, snap));

            int idx = Mathf.RoundToInt(_x / Dx);
            if (snap > 0.9f && idx != _lastSnap)
            {
                _lastSnap = idx;
                Voice?.Consonance(transform.position, 0.35f, 1.5f);
            }
            else if (snap < 0.5f && idx == _lastSnap) _lastSnap = -99;
        }
    }
}
