using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using UnityEngine;

namespace Prism.Worlds.MachineCathedral
{
    /// <summary>
    /// Pocket demonstrations for this world's three supporting concepts. Registered by attribute
    /// (<see cref="ConceptDemoForAttribute"/>) rather than by editing the shared registry, for the
    /// same reason <c>WorldRegistry</c> discovers worlds instead of listing them.
    ///
    /// All three reuse the SAME grammar the full world runs on: <see cref="TradeRig"/> for the two
    /// that involve a real trade (torque, mechanical-advantage), and plain computed booleans for the
    /// one that is purely digital (logic-gates). That is deliberate continuity, not an accident of
    /// convenience — a learner who later enters Machine Cathedral proper should feel like they
    /// already know the shape of the thing from the 17 cm version they held in the atrium.
    /// </summary>

    /// <summary>
    /// TORQUE — a wrench on a stuck bolt.
    ///
    /// Grab anywhere along the bar (no pinch needed, just presence) and the free hand becomes a
    /// point force at that radius. Real torque = force x radius drives a real damped rotation
    /// against a fixed resistance, exactly the way a stiff bolt resists a wrench. Grab close to the
    /// bolt and the bar strains but does not turn, no matter how hard you push against the spring;
    /// grab near the far end and the same effort turns it easily. Nothing states the threshold —
    /// the bar and the torque arc both redden toward gold once the current grip clears it, so the
    /// discovery is watched, not read.
    /// </summary>
    [ConceptDemoFor("torque")]
    public class TorqueDemo : ConceptDemo
    {
        Transform _boltMarker, _barPivot, _grip;
        Material _barMat;
        CurveView _arc;
        readonly List<Vector3> _arcPts = new List<Vector3>(20);

        float _phi, _omega, _r = 0.55f;
        bool _wasWorking;

        const float BarLen = 1.0f;
        const float Margin = 0.12f;
        const float Inertia = 0.09f;
        const float SpringK = 6f;
        const float Fmax = 1.0f;
        const float ResistTorque = 0.42f;

        public override void Build()
        {
            Ball(0.09f, PrismPalette.Warm, 0.5f, "bolt");
            _boltMarker = Ball(0.026f, PrismPalette.Gold, 0.7f, "boltMarker");

            var pivotGo = new GameObject("barPivot");
            pivotGo.transform.SetParent(transform, false);
            _barPivot = pivotGo.transform;

            _barMat = FlatMaterial(Tint, 0.5f);
            var visGo = new GameObject("barVisual");
            visGo.transform.SetParent(_barPivot, false);
            visGo.AddComponent<MeshFilter>().sharedMesh = MachineMesh.Box(new Vector3(BarLen, 0.045f, 0.045f));
            visGo.AddComponent<MeshRenderer>().sharedMaterial = _barMat;
            visGo.transform.localPosition = new Vector3(BarLen * 0.5f, 0f, 0f);

            _grip = Ball(0.05f, PrismPalette.Lavender, 0.6f, "grip");
            _arc = Curve(PrismPalette.Cyan, 0.010f);
        }

        protected override void OnTick(float dt)
        {
            Vector3 barDir = new Vector3(Mathf.Cos(_phi), Mathf.Sin(_phi), 0f);
            Vector3 tangDir = new Vector3(-Mathf.Sin(_phi), Mathf.Cos(_phi), 0f);

            float torqueDrive = 0f;
            if (TryFreeLocal(out var hand))
            {
                _r = Mathf.Clamp(Vector3.Dot(hand, barDir), Margin, BarLen);
                float tangentialOffset = Vector3.Dot(hand - barDir * _r, tangDir);
                float force = Mathf.Clamp(SpringK * tangentialOffset, -Fmax, Fmax);
                torqueDrive = force * _r;
            }

            // Static friction up to a threshold, a smaller kinetic friction once cleared -- a real
            // qualitative feature of a stuck bolt, not a precise friction law. See NOTES.md.
            bool working = Mathf.Abs(torqueDrive) > ResistTorque;
            float net = working ? torqueDrive - Mathf.Sign(torqueDrive) * ResistTorque * 0.5f : 0f;

            _omega += (net / Inertia) * dt;
            _omega *= Mathf.Exp(-1.6f * dt);
            _phi += _omega * dt;

            if (working && !_wasWorking) Voice?.Consonance(transform.position, 0.5f, 1.3f);
            _wasWorking = working;

            _barPivot.localRotation = Quaternion.Euler(0f, 0f, _phi * Mathf.Rad2Deg);

            Vector3 newBarDir = new Vector3(Mathf.Cos(_phi), Mathf.Sin(_phi), 0f);
            _grip.localPosition = newBarDir * _r;
            _boltMarker.localPosition = newBarDir * 0.09f;

            // The torque arc: a curved indicator, distinct from the straight push the hand supplies,
            // because torque is a different kind of quantity from force and deserves its own shape.
            float sweep = Mathf.Clamp01(Mathf.Abs(torqueDrive) / Fmax) * Mathf.PI * 1.5f;
            float sign = torqueDrive >= 0f ? 1f : -1f;
            _arcPts.Clear();
            for (int i = 0; i < 20; i++)
            {
                float t = i / 19f;
                float a = sign * t * sweep;
                _arcPts.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0.06f) * 0.30f);
            }
            _arc.Set(_arcPts);

            _barMat.SetColor("_Tint", working ? PrismPalette.Gold : Tint);
            _arc.Mat.SetColor("_Tint", working ? PrismPalette.Gold : PrismPalette.Cyan);
        }
    }

    /// <summary>
    /// MECHANICAL ADVANTAGE — a beam with two fixed weights and a fulcrum you slide.
    ///
    /// The load (heavy, coral) always outweighs the effort (light, cyan) at a centred fulcrum, so
    /// the beam sinks toward the load. Slide the fulcrum toward the load with the free hand and the
    /// balance point is crossed: the light weight starts winning, and the beam visibly flips to
    /// lift the heavy one. Two bars — work put in by the effort side, work delivered against the
    /// load — are genuine running integrals from the same <see cref="TradeRig"/> the beam's motion
    /// is computed with, not a picture of an equation: they climb together, stay together, and
    /// reset for the next swing when the beam settles. There is no way to make one outrun the other.
    /// </summary>
    [ConceptDemoFor("mechanical-advantage")]
    public class MechanicalAdvantageDemo : ConceptDemo
    {
        readonly TradeRig _rig = new TradeRig();
        Transform _beam, _loadWeight, _effortWeight, _fulcrumMark, _workInBar, _workOutBar;
        Material _beamMat;

        float _tf, _restTimer;

        const float HalfLen = 0.75f;
        const float Margin = 0.14f;
        const float LoadForce = 2.6f;
        const float EffortForce = 1.0f;
        const float ThetaMax = 0.55f;
        const float LoadMass = 1f, EffortMass = 0.4f;
        const float WorkBarScale = 1.1f;

        public override void Build()
        {
            _rig.QMin = -ThetaMax;
            _rig.QMax = ThetaMax;
            _rig.Damping = 0.5f;

            _beamMat = FlatMaterial(Tint, 0.35f);
            var beamGo = new GameObject("beam");
            beamGo.transform.SetParent(transform, false);
            beamGo.AddComponent<MeshFilter>().sharedMesh = MachineMesh.Box(new Vector3(HalfLen * 2f, 0.05f, 0.05f));
            beamGo.AddComponent<MeshRenderer>().sharedMaterial = _beamMat;
            _beam = beamGo.transform;

            _loadWeight = Ball(0.15f, PrismPalette.Coral, 0.75f, "load");
            _effortWeight = Ball(0.10f, PrismPalette.Cyan, 0.75f, "effort");
            _fulcrumMark = Ball(0.045f, PrismPalette.Lavender, 0.6f, "fulcrum");

            _workInBar = Body(PrismMesh.Icosphere(1), 1f, FlatMaterial(PrismPalette.Cyan, 0.7f), "workIn");
            _workOutBar = Body(PrismMesh.Icosphere(1), 1f, FlatMaterial(PrismPalette.Coral, 0.7f), "workOut");
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _tf = Mathf.Clamp(hand.x, -(HalfLen - Margin), HalfLen - Margin);

            float cosT = Mathf.Max(Mathf.Cos(_rig.Q), 0.05f);
            float de = HalfLen + _tf;
            float dl = HalfLen - _tf;

            // Same relation as LeverStation, at pocket scale: Ratio corrects for the arc geometry so
            // LoadForce/Ratio is the true generalised load torque, and the fixed effort weight is
            // fed in directly as a torque (force times its own arm times the same cosine).
            _rig.LoadForce = LoadForce;
            _rig.Ratio = 1f / (dl * cosT);
            _rig.EffMass = EffortMass * de * de + LoadMass * dl * dl;
            _rig.AdvanceConstant(dt, EffortForce * de * cosT);

            // The work bars reset once the beam settles, so each swing gets its own legible tally
            // instead of climbing forever until both bars are pinned at the ceiling.
            if (Mathf.Abs(_rig.QDot) < 0.02f)
            {
                _restTimer += dt;
                if (_restTimer > 0.6f) { _rig.WorkIn = 0f; _rig.WorkOut = 0f; _restTimer = 0f; }
            }
            else _restTimer = 0f;

            Quaternion rot = Quaternion.Euler(0f, 0f, _rig.Q * Mathf.Rad2Deg);
            _beam.localRotation = rot;
            _beam.localPosition = rot * new Vector3(-_tf, 0f, 0f);

            _effortWeight.localPosition = (rot * Vector3.left) * de;
            _loadWeight.localPosition = (rot * Vector3.right) * dl;
            _fulcrumMark.localPosition = new Vector3(_tf, -0.02f, 0f);

            Bar(_workInBar, Mathf.Abs(_rig.WorkIn), -0.16f);
            Bar(_workOutBar, Mathf.Abs(_rig.WorkOut), 0.06f);
        }

        static void Bar(Transform t, float value, float x)
        {
            float hgt = Mathf.Clamp(value * WorkBarScale, 0.015f, 0.85f);
            t.localScale = new Vector3(0.09f, hgt, 0.09f);
            t.localPosition = new Vector3(x, -0.85f + hgt, 0.55f);
        }
    }

    /// <summary>
    /// LOGIC GATES — two mechanical switches, a topology toggle, and a lamp.
    ///
    /// Reach toward switch A or switch B and pinch to throw it; a third, lavender toggle flips the
    /// wiring itself between series and parallel, redrawing the wires to match. The lamp's state is
    /// never asserted: it is <c>series ? (A &amp;&amp; B) : (A || B)</c>, computed fresh every
    /// frame from which switches are actually closed. A learner who wires two closed switches in
    /// series, watches the lamp light, opens one, watches it go dark, then flips to parallel and
    /// finds the SAME two switches now only need one closed between them, has built an AND and an
    /// OR with their own hand and seen the difference be the wiring, not the switches.
    /// </summary>
    [ConceptDemoFor("logic-gates")]
    public class LogicGatesDemo : ConceptDemo
    {
        class ToggleState
        {
            public Transform Pivot;
            public Material ArmMat;
            public Vector3 Origin;
            public Color BaseColour;
            public bool ColourByClosed = true;
            public bool Closed;
            public float Angle;
        }

        ToggleState _switchA, _switchB, _topology;
        Transform _lamp;
        Material _lampMat;
        CurveView _wireA, _wireB;
        bool _wasPinching, _seriesWiring = true, _wasLampOn;

        const float ClosedAngleDeg = 58f;
        const float ArmLength = 0.32f;
        const float ToggleRadius = 0.30f;

        public override void Build()
        {
            _switchA = MakeToggle(new Vector3(-0.55f, 0.05f, 0f), PrismPalette.Cyan);
            _switchB = MakeToggle(new Vector3(-0.05f, 0.05f, 0f), PrismPalette.Mint);
            _topology = MakeToggle(new Vector3(-0.30f, -0.55f, 0.14f), PrismPalette.Lavender);
            _topology.ColourByClosed = false;

            _lampMat = FlatMaterial(PrismPalette.Violet, 0.25f);
            var lampGo = new GameObject("lamp");
            lampGo.transform.SetParent(transform, false);
            lampGo.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            lampGo.AddComponent<MeshRenderer>().sharedMaterial = _lampMat;
            lampGo.transform.localPosition = new Vector3(0.55f, 0.35f, 0.10f);
            lampGo.transform.localScale = Vector3.one * 0.16f;
            _lamp = lampGo.transform;

            _wireA = Curve(PrismPalette.Coral, 0.010f, 3f);
            _wireB = Curve(PrismPalette.Coral, 0.010f, 3f);
        }

        ToggleState MakeToggle(Vector3 origin, Color colour)
        {
            var t = new ToggleState { Origin = origin, BaseColour = colour };

            var pivotGo = new GameObject("togglePivot");
            pivotGo.transform.SetParent(transform, false);
            pivotGo.transform.localPosition = origin;
            t.Pivot = pivotGo.transform;

            t.ArmMat = FlatMaterial(colour, 0.55f);
            var armGo = new GameObject("toggleArm");
            armGo.transform.SetParent(t.Pivot, false);
            armGo.AddComponent<MeshFilter>().sharedMesh = MachineMesh.Box(new Vector3(ArmLength, 0.05f, 0.05f));
            armGo.AddComponent<MeshRenderer>().sharedMaterial = t.ArmMat;
            armGo.transform.localPosition = new Vector3(ArmLength * 0.5f, 0f, 0f);

            return t;
        }

        protected override void OnTick(float dt)
        {
            bool hasHand = TryFreeLocal(out var h);
            bool pinching = FreePinch > 0.6f;
            bool pinchDown = pinching && !_wasPinching;
            _wasPinching = pinching;

            if (hasHand && pinchDown)
            {
                if ((h - _switchA.Origin).sqrMagnitude < ToggleRadius * ToggleRadius) _switchA.Closed = !_switchA.Closed;
                else if ((h - _switchB.Origin).sqrMagnitude < ToggleRadius * ToggleRadius) _switchB.Closed = !_switchB.Closed;
                else if ((h - _topology.Origin).sqrMagnitude < ToggleRadius * ToggleRadius) _seriesWiring = !_seriesWiring;
            }

            UpdateToggle(_switchA, dt);
            UpdateToggle(_switchB, dt);
            UpdateToggle(_topology, dt);

            bool on = _seriesWiring ? (_switchA.Closed && _switchB.Closed) : (_switchA.Closed || _switchB.Closed);
            _lampMat.SetColor("_Tint", on ? PrismPalette.Gold : PrismPalette.Violet);
            _lampMat.SetFloat("_Luminance", on ? 0.9f : 0.2f);
            if (on && !_wasLampOn) Voice?.Consonance(transform.position, 0.6f, 1.1f);
            _wasLampOn = on;

            Vector3 aTip = ArmTip(_switchA);
            Vector3 bTip = ArmTip(_switchB);
            Vector3 lampPos = _lamp.localPosition;

            if (_seriesWiring) { Segment(_wireA, aTip, bTip); Segment(_wireB, bTip, lampPos); }
            else { Segment(_wireA, aTip, lampPos); Segment(_wireB, bTip, lampPos); }

            float aLive = _seriesWiring ? (on ? 1f : 0.10f) : (_switchA.Closed ? 1f : 0.10f);
            float bLive = _seriesWiring ? (on ? 1f : 0.10f) : (_switchB.Closed ? 1f : 0.10f);
            _wireA.Mat.SetFloat("_Pulse", aLive);
            _wireB.Mat.SetFloat("_Pulse", bLive);
        }

        static Vector3 ArmTip(ToggleState t)
        {
            Quaternion rot = Quaternion.Euler(0f, 0f, t.Angle);
            return t.Origin + rot * new Vector3(ArmLength, 0f, 0f);
        }

        static void UpdateToggle(ToggleState t, float dt)
        {
            float target = t.Closed ? ClosedAngleDeg : 0f;
            t.Angle = Mathf.MoveTowards(t.Angle, target, dt * 260f);
            t.Pivot.localRotation = Quaternion.Euler(0f, 0f, t.Angle);
            if (t.ColourByClosed) t.ArmMat.SetColor("_Tint", t.Closed ? PrismPalette.Gold : t.BaseColour);
        }
    }
}
