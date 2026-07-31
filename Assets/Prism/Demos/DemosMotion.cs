using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Demos
{
    /// <summary>
    /// CONSERVATION OF ENERGY — a bead on a wire that never gets higher than you released it.
    ///
    /// Two stacked bars show kinetic and potential energy; their sum is a third bar that does not
    /// move, ever. Lift the bead with the free hand and drop it from wherever you like: the bead
    /// climbs the far side to exactly the height it started, and the constant bar stays constant.
    /// That invariance is the entire concept, and it is visible rather than stated.
    /// </summary>
    public class EnergyDemo : ConceptDemo
    {
        CurveView _track;
        Transform _bead, _kinBar, _potBar, _totBar;
        float _s = -0.7f, _v;                     // position along the track, and speed
        bool _held;
        const float G = 1.15f;

        // A smooth valley. y is the height at track coordinate s.
        static float Y(float s) => 0.45f * s * s;
        static float Slope(float s) => 0.9f * s;

        public override void Build()
        {
            _track = Curve(Tint, 0.009f);
            _track.Set(t => { float s = Mathf.Lerp(-1f, 1f, t); return new Vector3(s, Y(s) - 0.35f, 0f); }, 60);

            _bead = Ball(0.055f, PrismPalette.Gold, 0.9f, "bead");
            _kinBar = Body(PrismMesh.Icosphere(1), 1f, FlatMaterial(PrismPalette.Coral, 0.7f), "kinetic");
            _potBar = Body(PrismMesh.Icosphere(1), 1f, FlatMaterial(PrismPalette.Cyan, 0.7f), "potential");
            _totBar = Body(PrismMesh.Icosphere(1), 1f, FlatMaterial(PrismPalette.Gold, 0.8f), "total");
        }

        protected override void OnTick(float dt)
        {
            bool hand = TryFreeLocal(out var h);
            // Grab the bead by bringing the free hand near it.
            if (hand && (h - _bead.localPosition).magnitude < 0.28f && FreePinch > 0.4f)
            {
                _held = true;
                _s = Mathf.Clamp(h.x, -1f, 1f);
                _v = 0f;
            }
            else if (_held && FreePinch < 0.3f) _held = false;

            if (!_held)
            {
                // Motion along the track under gravity, with no damping — so it really does return.
                for (int i = 0; i < 4; i++)
                {
                    float sub = dt / 4f;
                    float a = -G * Slope(_s) / (1f + Slope(_s) * Slope(_s));
                    _v += a * sub;
                    _s += _v * sub;
                    if (Mathf.Abs(_s) > 1f) { _s = Mathf.Sign(_s); _v = -_v * 0.999f; }
                }
            }

            _bead.localPosition = new Vector3(_s, Y(_s) - 0.35f + 0.05f, 0f);

            float kinetic = 0.5f * _v * _v;
            float potential = G * Y(_s);
            float total = kinetic + potential;

            // Bars, drawn as squashed spheres so there is no new mesh to author.
            void Bar(Transform t, float value, float x, float scale)
            {
                float hgt = Mathf.Clamp(value * scale, 0.01f, 0.9f);
                t.localScale = new Vector3(0.09f, hgt, 0.09f);
                t.localPosition = new Vector3(x, -0.55f + hgt, 0.45f);
            }
            Bar(_kinBar, kinetic, 0.55f, 0.55f);
            Bar(_potBar, potential, 0.72f, 0.55f);
            Bar(_totBar, total, 0.89f, 0.55f);
        }
    }

    /// <summary>
    /// ANGULAR MOMENTUM — pull the string in and it speeds up. It has to.
    ///
    /// The most tactile demonstration here, and the most surprising. r*v is held constant exactly,
    /// so halving the radius doubles the speed, and the learner feels that they caused it by
    /// pulling. The skater analogy usually has to be described; here it is done.
    /// </summary>
    public class AngularMomentumDemo : ConceptDemo
    {
        Transform _hub, _mass;
        CurveView _string, _trail;
        readonly List<Vector3> _history = new List<Vector3>();
        float _theta, _r = 0.75f;
        const float L = 0.34f;                    // the conserved quantity r*v

        public override void Build()
        {
            _hub = Ball(0.075f, Tint, 0.7f, "hub");
            _mass = Ball(0.055f, PrismPalette.Gold, 0.95f, "mass");
            _string = Curve(PrismPalette.Warm, 0.005f);
            _trail = Curve(PrismPalette.Coral, 0.006f);
        }

        protected override void OnTick(float dt)
        {
            // The free hand's distance from the hub sets the radius: pulling in is literally pulling.
            if (TryFreeLocal(out var hand))
                _r = Mathf.Lerp(_r, Mathf.Clamp(hand.magnitude, 0.22f, 1.0f), dt * 6f);

            // v = L / r, exactly. This is the whole lesson in one line.
            float v = L / _r;
            _theta += (v / _r) * dt;

            var p = new Vector3(Mathf.Cos(_theta), 0f, Mathf.Sin(_theta)) * _r;
            _mass.localPosition = p;
            Segment(_string, Vector3.zero, p);

            _history.Add(p);
            if (_history.Count > 70) _history.RemoveAt(0);
            if (_history.Count > 2) _trail.Set(_history);

            // The mass brightens as it speeds up, so the change is legible even at a glance.
            float speed01 = Mathf.InverseLerp(L / 1.0f, L / 0.22f, v);
            _mass.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.075f, speed01);
        }
    }

    /// <summary>
    /// PERIODIC MOTION — the pendulum wave.
    ///
    /// Fifteen pendulums whose lengths are tuned so their periods are consecutive integers over a
    /// cycle. They start together, drift into travelling waves, then into apparent chaos, then
    /// snap back into a line. Nothing is choreographed: every bob is independently solving its own
    /// pendulum equation, and the pattern is a consequence.
    /// </summary>
    public class PeriodicMotionDemo : ConceptDemo
    {
        const int N = 15;
        Transform[] _bobs;
        CurveView[] _strings;
        float[] _omega;
        float _t;
        float _rate = 1f;

        public override void Build()
        {
            _bobs = new Transform[N];
            _strings = new CurveView[N];
            _omega = new float[N];

            for (int i = 0; i < N; i++)
            {
                // Periods in the ratio (base + i), so the whole set realigns after one full cycle.
                float cycles = 24f + i;
                _omega[i] = cycles * (Mathf.PI * 2f) / 24f;
                _bobs[i] = Ball(0.035f, PrismPalette.Spectral(i / (float)(N - 1)), 0.9f, $"bob{i}");
                _strings[i] = Curve(PrismPalette.Warm, 0.003f);
            }
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand)) _rate = Mathf.Clamp(0.4f + (hand.y + 0.6f) * 1.4f, 0.05f, 3f);
            _t += dt * _rate * 0.55f;

            for (int i = 0; i < N; i++)
            {
                float x = Mathf.Lerp(-0.85f, 0.85f, i / (float)(N - 1));
                float angle = Mathf.Sin(_t * _omega[i]) * 0.55f;
                float len = 0.75f;
                var pivot = new Vector3(x, 0.62f, 0f);
                var bob = pivot + new Vector3(0f, -Mathf.Cos(angle) * len, Mathf.Sin(angle) * len);
                _bobs[i].localPosition = bob;
                Segment(_strings[i], pivot, bob);
            }
        }
    }

    /// <summary>
    /// WAVES — a string you shake, solving the real wave equation.
    ///
    /// The free hand drives the left end; the disturbance propagates at a finite speed, reflects
    /// off the fixed right end, and interferes with what is still coming. None of that is scripted.
    /// A learner who wiggles at the right rate finds a standing wave on their own.
    /// </summary>
    public class WaveDemo : ConceptDemo
    {
        const int N = 96;
        float[] _y, _yPrev, _next;
        CurveView _string;
        readonly List<Vector3> _pts = new List<Vector3>(N);
        Transform _driver;

        public override void Build()
        {
            _y = new float[N];
            _yPrev = new float[N];
            _next = new float[N];
            _string = Curve(Tint, 0.010f);
            _driver = Ball(0.05f, PrismPalette.Gold, 0.9f, "driver");
        }

        protected override void OnTick(float dt)
        {
            // Explicit finite difference for d2y/dt2 = c^2 d2y/dx2. c is set so a pulse crosses in
            // about a second, which is slow enough to watch a reflection happen.
            const float c2 = 0.25f;
            float h = Mathf.Min(dt, 0.016f);
            float k = c2 * (h * h) * (N * N) * 0.0006f;
            k = Mathf.Clamp(k, 0f, 0.5f);          // stability: the CFL condition, enforced

            for (int i = 1; i < N - 1; i++)
            {
                float lap = _y[i - 1] - 2f * _y[i] + _y[i + 1];
                _next[i] = 2f * _y[i] - _yPrev[i] + k * lap;
                _next[i] *= 0.9995f;                // a whisper of damping, or it never settles
            }

            // Left end is driven by the hand; right end is clamped, so waves reflect.
            float drive = 0f;
            if (TryFreeLocal(out var hand)) drive = Mathf.Clamp(hand.y, -0.55f, 0.55f);
            _next[0] = drive;
            _next[N - 1] = 0f;

            // Rotate the three buffers instead of allocating a new one each frame.
            var recycled = _yPrev;
            _yPrev = _y;
            _y = _next;
            _next = recycled;

            _pts.Clear();
            for (int i = 0; i < N; i++)
                _pts.Add(new Vector3(Mathf.Lerp(-0.95f, 0.95f, i / (float)(N - 1)), _y[i], 0f));
            _string.Set(_pts);
            _driver.localPosition = _pts[0];
        }
    }

    /// <summary>
    /// RHYTHM — tap a tempo and the world locks to it.
    ///
    /// Pinch to tap. After two taps the ring starts turning at your tempo; after four it is
    /// confident and the pulses land with it. Rhythm is the one concept here that cannot be
    /// understood by looking, only by doing, so this demonstration is the only one that requires
    /// the learner to supply the input rather than merely steer it.
    /// </summary>
    public class RhythmDemo : ConceptDemo
    {
        CurveView _ring;
        Transform[] _pulses;
        readonly List<float> _taps = new List<float>();
        float _period = 0.6f;
        float _phase;
        bool _wasPinching;

        public override void Build()
        {
            _ring = Curve(Tint, 0.010f);
            _ring.Set(t =>
            {
                float a = t * Mathf.PI * 2f;
                return new Vector3(Mathf.Cos(a) * 0.7f, 0f, Mathf.Sin(a) * 0.7f);
            }, 64);

            _pulses = new Transform[4];
            for (int i = 0; i < 4; i++)
                _pulses[i] = Ball(0.05f, PrismPalette.Spectral(i / 3f), 0.9f, $"pulse{i}");
        }

        protected override void OnTick(float dt)
        {
            bool pinching = FreePinch > 0.6f;
            if (pinching && !_wasPinching)
            {
                _taps.Add(Age);
                if (_taps.Count > 5) _taps.RemoveAt(0);
                if (_taps.Count >= 2)
                {
                    float sum = 0f;
                    for (int i = 1; i < _taps.Count; i++) sum += _taps[i] - _taps[i - 1];
                    _period = Mathf.Clamp(sum / (_taps.Count - 1), 0.18f, 2.5f);
                    _phase = 0f;
                }
                Voice?.Settle(transform.position, 0.5f, 1.2f);
            }
            _wasPinching = pinching;

            _phase += dt / _period;
            for (int i = 0; i < 4; i++)
            {
                float beatPhase = Mathf.Repeat(_phase - i * 0.25f, 1f);
                float a = beatPhase * Mathf.PI * 2f;
                _pulses[i].localPosition = new Vector3(Mathf.Cos(a) * 0.7f, 0f, Mathf.Sin(a) * 0.7f);
                // Swells on the beat: the downbeat is bigger, which is what makes it read as metre.
                float swell = Mathf.Exp(-beatPhase * 6f) * (i == 0 ? 1.4f : 0.8f);
                _pulses[i].localScale = Vector3.one * (0.04f + swell * 0.035f);
            }

            _ring.Mat.SetFloat("_Pulse", _taps.Count >= 2 ? 1f : 0.15f);
        }
    }
}
