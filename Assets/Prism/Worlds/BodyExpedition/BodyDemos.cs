using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using UnityEngine;

namespace Prism.Worlds.BodyExpedition
{
    /// <summary>
    /// PRESSURE AND FLOW — flow is what a pressure difference DOES.
    ///
    /// Two reservoirs joined by a pipe. The free hand raises one; flow follows the difference,
    /// linearly and immediately, and stops dead the instant the two levels match. That last part is
    /// the lesson: it is not the pressure that drives flow, it is the DIFFERENCE. A learner who has
    /// held both ends high and watched nothing happen has understood something most diagrams fail
    /// to convey.
    /// </summary>
    [ConceptDemoFor("pressure-and-flow")]
    public class PressureFlowDemo : ConceptDemo
    {
        Transform _left, _right, _pipe;
        CurveView _stream;
        Material _leftMat, _rightMat;
        readonly List<Vector3> _path = new List<Vector3>(12);
        float _pL = 0.55f, _pR = 0.25f;   // reservoir heights, normalised
        float _carried;                   // volume moved, so the effect accumulates visibly

        const float Resistance = 1.4f;

        public override void Build()
        {
            _leftMat = FlatMaterial(PrismPalette.Coral, 0.6f);
            _rightMat = FlatMaterial(PrismPalette.Cyan, 0.6f);
            _left = Body(PrismMesh.Icosphere(2), 1f, _leftMat, "left");
            _right = Body(PrismMesh.Icosphere(2), 1f, _rightMat, "right");
            _pipe = Ball(0.03f, PrismPalette.Warm, 0.4f, "pipe");
            _stream = Curve(PrismPalette.Gold, 0.010f, 4f);
        }

        protected override void OnTick(float dt)
        {
            // The hand lifts the left reservoir. The right one drains toward it.
            if (TryFreeLocal(out var hand))
                _pL = Mathf.Clamp(Mathf.InverseLerp(-0.7f, 0.7f, hand.y), 0.05f, 1f);

            // Q = dP / R, and the sign of dP is the direction of flow. Nothing else.
            float dP = _pL - _pR;
            float q = dP / Resistance;

            _pR = Mathf.Clamp(_pR + q * dt * 0.35f, 0.02f, 1f);
            _carried += Mathf.Abs(q) * dt;

            void Column(Transform t, float level, float x)
            {
                t.localScale = new Vector3(0.16f, Mathf.Max(level * 0.8f, 0.02f), 0.16f);
                t.localPosition = new Vector3(x, -0.75f + level * 0.8f, 0f);
            }
            Column(_left, _pL, -0.55f);
            Column(_right, _pR, 0.55f);
            _pipe.localPosition = new Vector3(0f, -0.72f, 0f);
            _pipe.localScale = new Vector3(1.1f, 0.03f, 0.03f);

            // The stream between them: its thickness IS the flow, and it vanishes at equality.
            _path.Clear();
            for (int i = 0; i < 12; i++)
            {
                float t = i / 11f;
                _path.Add(new Vector3(Mathf.Lerp(-0.5f, 0.5f, t), -0.72f + Mathf.Sin(t * Mathf.PI) * 0.05f, 0f));
            }
            _stream.Radius = Mathf.Clamp(Mathf.Abs(q) * 0.05f, 0.0008f, 0.03f);
            _stream.Set(_path);
            _stream.Mat.SetFloat("_Pulse", Mathf.Clamp01(Mathf.Abs(q) * 3f));
            // Direction is carried by the packet travel, so a reversed difference visibly reverses.
            _stream.Mat.SetFloat("_Speed", Mathf.Clamp(q * 2.5f, -3f, 3f));
        }
    }

    /// <summary>
    /// RESISTANCE — the fourth power, done with the fingers.
    ///
    /// The single most valuable demonstration in this module, and the reason is arithmetic:
    /// Poiseuille resistance goes as 1/r^4, so squeezing a vessel to HALF its radius cuts flow to a
    /// SIXTEENTH. Nobody believes that from a formula. Everybody believes it after pinching a tube
    /// and watching the flow bar fall off a cliff while their fingers have barely moved.
    ///
    /// The exponent is not tuned for drama. It is four because it is four.
    /// </summary>
    [ConceptDemoFor("resistance")]
    public class ResistanceDemo : ConceptDemo
    {
        CurveView _vessel;
        Transform _flowBar, _squeezeMark;
        Material _flowMat;
        readonly List<Vector3> _path = new List<Vector3>(24);
        float _radius = 1f;               // as a fraction of the relaxed radius

        const float RelaxedRadius = 0.045f;

        public override void Build()
        {
            _vessel = Curve(PrismPalette.Coral, RelaxedRadius, 5f);
            _flowMat = FlatMaterial(PrismPalette.Gold, 0.8f);
            _flowBar = Body(PrismMesh.Icosphere(1), 1f, _flowMat, "flow");
            _squeezeMark = Ball(0.03f, PrismPalette.Lavender, 0.7f, "grip");
        }

        protected override void OnTick(float dt)
        {
            // Distance from the vessel axis is the squeeze: closing your fingers narrows it.
            float target = 1f;
            bool gripping = false;
            if (TryFreeLocal(out var hand))
            {
                float d = new Vector2(hand.y, hand.z).magnitude;
                if (Mathf.Abs(hand.x) < 0.7f && d < 0.5f)
                {
                    gripping = true;
                    target = Mathf.Clamp(Mathf.InverseLerp(0.02f, 0.34f, d), 0.25f, 1f);
                }
            }
            _radius = Mathf.Lerp(_radius, target, dt * 8f);
            _squeezeMark.gameObject.SetActive(gripping);
            if (gripping && TryFreeLocal(out var h2)) _squeezeMark.localPosition = h2;

            // Poiseuille: Q proportional to r^4 for a fixed pressure difference.
            float r4 = _radius * _radius * _radius * _radius;

            // The vessel narrows only in its middle, so the learner sees a local constriction
            // rather than a uniformly thinner tube — resistance is where the narrowing IS.
            _path.Clear();
            for (int i = 0; i < 24; i++)
            {
                float t = i / 23f;
                _path.Add(new Vector3(Mathf.Lerp(-0.9f, 0.9f, t), 0.1f, 0f));
            }
            float pinch = Mathf.Lerp(RelaxedRadius, RelaxedRadius * _radius, 1f);
            _vessel.Radius = pinch;
            _vessel.Set(_path);
            _vessel.Mat.SetFloat("_Speed", Mathf.Lerp(0.1f, 1.8f, r4));
            _vessel.Mat.SetFloat("_Pulse", r4);

            // The bar is flow. Halve the radius and it drops to a sixteenth, in front of them.
            float h = Mathf.Clamp(r4 * 0.85f, 0.004f, 0.85f);
            _flowBar.localScale = new Vector3(0.10f, h, 0.10f);
            _flowBar.localPosition = new Vector3(0.75f, -0.7f + h, 0f);
            _flowMat.SetColor("_Tint", PrismPalette.Spectral(Mathf.Clamp01(1f - r4)));
        }
    }

    /// <summary>
    /// HOMEOSTASIS — the reason you can stand up without fainting.
    ///
    /// A regulated pressure with a real negative-feedback loop and a real DELAY. The free hand
    /// pushes the pressure away from its setpoint; the reflex pushes back, but not instantly, so
    /// the recovery overshoots and rings before it settles.
    ///
    /// The delay is the whole point and is not a stylistic choice: a controller with no lag would
    /// simply hold the value pinned, which teaches nothing, and no real reflex behaves that way.
    /// </summary>
    [ConceptDemoFor("homeostasis")]
    public class HomeostasisDemo : ConceptDemo
    {
        CurveView _trace, _setLine;
        Transform _marker;
        Material _markerMat;
        readonly List<Vector3> _pts = new List<Vector3>(64);
        readonly List<Vector3> _line = new List<Vector3>(2);
        readonly float[] _history = new float[64];
        int _head;
        float _p = 0.5f, _reflex;         // pressure, and the slow reflex signal
        float _accum;

        const float Setpoint = 0.5f;
        const float Gain = 2.6f;
        const float ReflexLag = 0.55f;    // seconds — the delay that makes it ring

        public override void Build()
        {
            _trace = Curve(PrismPalette.Cyan, 0.007f);
            _setLine = Curve(PrismPalette.Warm, 0.003f);
            _markerMat = FlatMaterial(PrismPalette.Gold, 0.9f);
            _marker = Body(PrismMesh.Icosphere(2), 0.05f, _markerMat, "marker");
            for (int i = 0; i < _history.Length; i++) _history[i] = Setpoint;
        }

        protected override void OnTick(float dt)
        {
            // The hand is a disturbance, not a controller: it pushes, the body answers.
            float push = 0f;
            if (TryFreeLocal(out var hand) && Mathf.Abs(hand.y) > 0.05f)
                push = Mathf.Clamp(hand.y, -0.8f, 0.8f) * 1.6f;

            // Reflex chases the error, but through a lag — so its correction always arrives late.
            float error = Setpoint - _p;
            _reflex += (error * Gain - _reflex) * (dt / ReflexLag);
            _p += (push + _reflex) * dt;
            _p = Mathf.Clamp(_p, 0.02f, 0.98f);

            _accum += dt;
            if (_accum > 0.05f)
            {
                _accum = 0f;
                _history[_head] = _p;
                _head = (_head + 1) % _history.Length;
            }

            _pts.Clear();
            for (int i = 0; i < _history.Length; i++)
            {
                int idx = (_head + i) % _history.Length;
                _pts.Add(new Vector3(Mathf.Lerp(-0.9f, 0.9f, i / (float)(_history.Length - 1)),
                                     (_history[idx] - 0.5f) * 1.3f, 0f));
            }
            _trace.Set(_pts);

            _line.Clear();
            _line.Add(new Vector3(-0.9f, 0f, 0f));
            _line.Add(new Vector3(0.9f, 0f, 0f));
            _setLine.Set(_line);

            _marker.localPosition = new Vector3(0.9f, (_p - 0.5f) * 1.3f, 0f);
            // Gold when held near the setpoint, coral when driven away: the state, at a glance.
            _markerMat.SetColor("_Tint",
                Color.Lerp(PrismPalette.Gold, PrismPalette.Coral, Mathf.Clamp01(Mathf.Abs(error) * 4f)));
        }
    }

    /// <summary>
    /// CARDIAC CYCLE — a pump with valves, not a throbbing shape.
    ///
    /// Two phases running on a real clock: fill with the outlet shut, then eject with the inlet
    /// shut. Volume is genuinely integrated from the flows, so the ventricle can only push out what
    /// it actually took in, and raising the rate visibly shortens filling before it shortens
    /// anything else — which is why a very fast heart moves LESS blood per beat, a fact almost
    /// nobody expects.
    /// </summary>
    [ConceptDemoFor("cardiac-cycle")]
    public class CardiacCycleDemo : ConceptDemo
    {
        Transform _chamber, _inValve, _outValve;
        Material _chamberMat, _inMat, _outMat;
        CurveView _trace;
        readonly List<Vector3> _pts = new List<Vector3>(72);
        readonly float[] _history = new float[72];
        int _head;
        float _phase;                     // 0..1 through one beat
        float _volume = 0.5f;
        float _rate = 1.1f;               // beats per second
        float _accum;

        public override void Build()
        {
            _chamberMat = PrismMaterials.New(PrismMaterials.Gel);
            _chamberMat.SetColor("_Tint", PrismPalette.Coral);
            _chamberMat.SetColor("_DeepTint", PrismPalette.Violet);
            _chamberMat.SetFloat("_Density", 1.2f);
            Track(_chamberMat);
            _chamber = Body(PrismMesh.Icosphere(3), 0.30f, _chamberMat, "ventricle");

            _inMat = FlatMaterial(PrismPalette.Cyan, 0.7f);
            _outMat = FlatMaterial(PrismPalette.Gold, 0.7f);
            _inValve = Body(PrismMesh.Icosphere(1), 0.07f, _inMat, "inlet");
            _outValve = Body(PrismMesh.Icosphere(1), 0.07f, _outMat, "outlet");
            _trace = Curve(PrismPalette.Lavender, 0.006f);
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _rate = Mathf.Clamp(0.5f + Mathf.InverseLerp(-0.7f, 0.7f, hand.y) * 2.5f, 0.4f, 3.0f);

            _phase = Mathf.Repeat(_phase + _rate * dt, 1f);

            // Diastole occupies the first 60% of the beat, systole the rest — and because the beat
            // shortens with rate, filling time shrinks first. That is real and is the surprise.
            bool filling = _phase < 0.6f;
            float fillRate = 0.9f, ejectRate = 1.4f;

            if (filling) _volume += fillRate * dt * _rate;
            else _volume -= ejectRate * dt * _rate;
            _volume = Mathf.Clamp(_volume, 0.12f, 1f);

            _chamber.localScale = Vector3.one * (0.22f + _volume * 0.20f);
            _chamberMat.SetFloat("_Density", Mathf.Lerp(0.7f, 1.6f, _volume));

            // A valve is open or shut, never both, and never neither.
            _inValve.localPosition = new Vector3(-0.42f, 0.30f, 0f);
            _outValve.localPosition = new Vector3(0.42f, 0.30f, 0f);
            _inValve.localScale = Vector3.one * (filling ? 0.085f : 0.035f);
            _outValve.localScale = Vector3.one * (filling ? 0.035f : 0.085f);
            _inMat.SetFloat("_Luminance", filling ? 0.95f : 0.15f);
            _outMat.SetFloat("_Luminance", filling ? 0.15f : 0.95f);

            _accum += dt;
            if (_accum > 0.04f)
            {
                _accum = 0f;
                _history[_head] = _volume;
                _head = (_head + 1) % _history.Length;
            }

            _pts.Clear();
            for (int i = 0; i < _history.Length; i++)
            {
                int idx = (_head + i) % _history.Length;
                _pts.Add(new Vector3(Mathf.Lerp(-0.9f, 0.9f, i / (float)(_history.Length - 1)),
                                     -0.55f + _history[idx] * 0.45f, 0f));
            }
            _trace.Set(_pts);
        }
    }
}
