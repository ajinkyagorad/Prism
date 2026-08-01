using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using Prism.Worlds;
using UnityEngine;

namespace Prism.Worlds.BodyExpedition
{
    /// <summary>Evidence keys. Named constants because the gates and the world must agree exactly.</summary>
    public static class BodyExpeditionEvidence
    {
        public const string AnyTouch          = "touch";
        public const string VesselAdjust      = "vessel.adjust";
        public const string PressureRise      = "pressure.rise";
        public const string PressureCollapse  = "pressure.collapse";
        public const string HeartRateUsed     = "heart.used";
        public const string TiltUsed          = "tilt.used";
        public const string PatientRestored   = "patient.restored";
        public const string PredictionGood    = "prediction.good";
        public const string TreeBalanced      = "tree.balanced";
    }

    /// <summary>
    /// Body Expedition: the circulatory system as a pressure-and-flow control system the learner
    /// takes hold of, not a diagram of parts to name.
    ///
    ///   Wonder     A small vessel tree, pulsing. Flow already visibly moves through it, coloured
    ///              by speed. Nothing is labelled and nothing explains itself.
    ///   Explore    Narrow or widen any vessel with a two-hand squeeze; grip the heart to speed or
    ///              slow it; drag the posture bead to tilt the body from lying to standing. Pressure
    ///              answers everywhere at once, not just where the hand is — that non-locality is
    ///              the lesson, and the gate waits until the learner has personally produced both a
    ///              pressure rise and a pressure collapse.
    ///   Discover   Pressure appears as a real field: a bead at every junction, sized and lit by the
    ///              local pressure there. Resistance was always visible — it is the vessel's own
    ///              width — and flow was always a moving, speed-coloured current.
    ///   Formalize  Systole and diastole are named against the beat the learner has already felt.
    ///              Near the heart, two plain bars show the pressure gap and the resulting flow
    ///              growing and shrinking together — the relationship shown spatially, not written
    ///              as an equation.
    ///   Apply      A vessel has gone pathological; find it and restore healthy pressure.
    ///   Explain    Predict which territory will lose flow when an unseen vessel narrows, mark it,
    ///              then watch.
    ///   Create     Balance flow across all four tissue beds by hand.
    ///   Connect    Return to the atrium; the constellation has changed.
    ///
    /// See NOTES.md for the physics model, the colour encoding, and what is honest vs approximate.
    /// </summary>
    public class BodyExpeditionWorld : PrismWorldBase
    {
        public override string WorldId => "body-expedition";

        /// <summary>Inside a body. Dark, warm, and the walls move.</summary>
        protected override WorldShellSpec Shell => WorldShellSpec.Interior;
        public override string PrimaryConceptId => "body-expedition";
        public override string DisplayName => "Body Expedition";

        public override string[] Shaders =>
            new[] { PrismMaterials.Ceramic, PrismMaterials.Flow, PrismMaterials.Gel };

        public override IEnumerable<ConceptSpec> Concepts => BuildConceptSpecs();

        public CirculationSim Sim { get; private set; }
        public Transform WorldAnchor => Anchor;

        /// <summary>
        /// Fixed Anchor-local layout, indexed by BodyNode. Deliberately decoupled from the physics
        /// Vessel.Length used for resistance — like OrbitalWorld's display planet radius versus the
        /// mu that implies a real Earth-scale orbit, the tabletop geometry here is chosen for reach
        /// and legibility, and the resistance numbers are chosen separately for honesty; see
        /// NOTES.md.
        /// </summary>
        public static readonly Vector3[] NodeLocalPos =
        {
            new Vector3( 0.00f,  0.02f,  0.02f),   // Aorta
            new Vector3( 0.00f,  0.09f,  0.02f),   // Upper
            new Vector3( 0.00f, -0.11f,  0.02f),   // Lower
            new Vector3(-0.08f,  0.14f,  0.01f),   // Arm
            new Vector3( 0.05f,  0.17f,  0.01f),   // Head
            new Vector3(-0.07f, -0.16f,  0.01f),   // LegL
            new Vector3( 0.07f, -0.16f,  0.01f),   // LegR
            new Vector3( 0.11f, -0.02f, -0.03f),   // Venous
        };
        static readonly Vector3 HeartLocalPos = new Vector3(0f, -0.03f, 0.00f);

        // Colour encoding, stated once: vessel HUE is flow SPEED (m/s), cyan slow through violet
        // fast, exactly the convention OrbitalWorld uses for its trails. Pressure is a SEPARATE,
        // orthogonal channel carried by the junction beads' size and brightness, never by hue, so
        // the two fields can never be read as the same thing.
        const float SpeedLo = 0.015f;
        const float SpeedHi = 1.80f;
        const float PacketSpeedScale = 0.7f;

        // Vessel radii in CirculationSim are already display-scale (see its header comment); this
        // is a further UNIFORM cosmetic boost so the thinnest peripheral vessels stay graspable.
        // Uniform means every relative comparison the learner can make — this vessel vs that one,
        // this vessel narrowed vs itself widened — is unaffected.
        const float DisplayRadiusBoost = 1.8f;

        const float VesselGrabRadius = 0.035f;

        const float TiltDialX = -0.15f, TiltDialZ = 0.02f;
        const float TiltTrackBottomY = -0.02f, TiltTrackTopY = 0.07f;

        const float BarMaxHeight = 0.05f;
        const float BarThickness = 0.0028f;

        class VesselView
        {
            public Vessel Edge;
            public Transform T;
            public Material Mat;
            public Vector3 LocalFrom, LocalTo;
            public float VisualLength;
        }

        class NodeOrb
        {
            public int Id;
            public Transform T;
            public Material Mat;
            public float BaseRadius;
        }

        Mesh _unitCylinder;
        VesselView[] _vessels;
        VesselView _adjustingVessel;
        float _adjustGrabHandDist, _adjustGrabStartMult;

        NodeOrb[] _orbs;
        float _pressureReveal;

        Transform _heart;
        Material _heartMat;
        float _heartHeldFor;
        PrismHands.Hand _heartGripHand;

        Transform _tiltDial;
        Transform _tiltIndicator;

        PrismLabel _readout;
        Transform _pressureBar, _flowBar;
        float _formalizeReveal;

        CirculationChallenge _challenge;

        // -----------------------------------------------------------------
        // construction
        // -----------------------------------------------------------------

        protected override void BuildWorld()
        {
            Sim = new CirculationSim();

            BuildVessels();
            BuildOrbs();
            BuildHeart();
            BuildTiltControls();
            BuildFormalizeReadout();

            _challenge = gameObject.AddComponent<CirculationChallenge>();
            _challenge.World = this;
        }

        static Mesh BuildUnitCylinderMesh()
        {
            var pts = new List<Vector3> { Vector3.zero, Vector3.forward };
            return PrismMesh.Tube(pts, 1f, 8);
        }

        void BuildVessels()
        {
            _unitCylinder = BuildUnitCylinderMesh();
            _vessels = new VesselView[CirculationSim.EdgeCount];

            for (int i = 0; i < CirculationSim.EdgeCount; i++)
            {
                var e = Sim.Edges[i];
                var vv = new VesselView
                {
                    Edge = e,
                    LocalFrom = NodeLocalPos[e.From],
                    LocalTo = NodeLocalPos[e.To]
                };
                vv.VisualLength = Vector3.Distance(vv.LocalFrom, vv.LocalTo);

                var go = new GameObject("Vessel_" + i);
                go.transform.SetParent(Anchor, false);
                go.AddComponent<MeshFilter>().sharedMesh = _unitCylinder;

                var mat = PrismMaterials.New(PrismMaterials.Flow);
                mat.SetFloat("_Strength", 0.85f);
                mat.SetFloat("_CoreGain", 0.5f);
                mat.SetFloat("_Packets", Mathf.Clamp(vv.VisualLength / 0.014f, 1.5f, 7f));
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                vv.Mat = mat;

                vv.T = go.transform;
                vv.T.localPosition = vv.LocalFrom;
                vv.T.localRotation = SafeLookRotation(vv.LocalTo - vv.LocalFrom);

                _vessels[i] = vv;
            }
        }

        void BuildOrbs()
        {
            _orbs = new NodeOrb[CirculationSim.NodeCount];
            for (int n = 0; n < CirculationSim.NodeCount; n++)
            {
                Material mat;
                float baseRadius;

                if (n == (int)BodyNode.Venous)
                {
                    // The venous reservoir is a diffuse pool, not a point, so it gets the
                    // translucent internal-current material rather than a crisp bead.
                    mat = PrismMaterials.New(PrismMaterials.Gel);
                    mat.SetColor("_Tint", PrismPalette.Gold);
                    mat.SetColor("_DeepTint", PrismPalette.Coral);
                    mat.SetFloat("_Density", 0.9f);
                    mat.SetFloat("_FlowSpeed", 0.12f);
                    baseRadius = 0.020f;
                }
                else
                {
                    mat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.3f);
                    baseRadius = 0.012f;
                }

                var t = Body(PrismMesh.Icosphere(1), 0.0005f, mat, "Pressure_" + (BodyNode)n);
                t.localPosition = NodeLocalPos[n];
                _orbs[n] = new NodeOrb { Id = n, T = t, Mat = mat, BaseRadius = baseRadius };
            }
        }

        void BuildHeart()
        {
            _heartMat = PrismMaterials.CeramicBody(PrismPalette.Coral, 0.4f, 260f);
            _heart = Body(PrismMesh.Icosphere(2), 0.022f, _heartMat, "Heart");
            _heart.localPosition = HeartLocalPos;
        }

        void BuildTiltControls()
        {
            var dialMat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.4f);
            _tiltDial = Body(PrismMesh.Icosphere(1), 0.012f, dialMat, "TiltDial");
            _tiltDial.localPosition = new Vector3(TiltDialX, TiltTrackBottomY, TiltDialZ);

            var indMat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.35f);
            var go = new GameObject("TiltIndicator");
            go.transform.SetParent(Anchor, false);
            go.AddComponent<MeshFilter>().sharedMesh = _unitCylinder;
            go.AddComponent<MeshRenderer>().sharedMaterial = indMat;
            _tiltIndicator = go.transform;
            _tiltIndicator.localPosition = new Vector3(TiltDialX + 0.05f, TiltTrackBottomY + 0.02f, TiltDialZ);
            _tiltIndicator.localScale = new Vector3(0.0025f, 0.0025f, 0.045f);
        }

        void BuildFormalizeReadout()
        {
            _readout = PrismLabel.Create("Readout", Anchor, Head, 0.013f);

            var pressureMat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.5f);
            var go1 = new GameObject("PressureBar");
            go1.transform.SetParent(Anchor, false);
            go1.AddComponent<MeshFilter>().sharedMesh = _unitCylinder;
            go1.AddComponent<MeshRenderer>().sharedMaterial = pressureMat;
            _pressureBar = go1.transform;
            _pressureBar.localPosition = NodeLocalPos[(int)BodyNode.Aorta] + new Vector3(0.028f, -0.01f, 0.02f);
            _pressureBar.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            _pressureBar.localScale = new Vector3(BarThickness, BarThickness, 0.00001f);

            var flowMat = PrismMaterials.CeramicBody(PrismPalette.Cyan, 0.5f);
            var go2 = new GameObject("FlowBar");
            go2.transform.SetParent(Anchor, false);
            go2.AddComponent<MeshFilter>().sharedMesh = _unitCylinder;
            go2.AddComponent<MeshRenderer>().sharedMaterial = flowMat;
            _flowBar = go2.transform;
            _flowBar.localPosition = NodeLocalPos[(int)BodyNode.Aorta] + new Vector3(0.045f, -0.01f, 0.02f);
            _flowBar.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            _flowBar.localScale = new Vector3(BarThickness, BarThickness, 0.00001f);
        }

        // -----------------------------------------------------------------
        // the loop
        // -----------------------------------------------------------------

        protected override void ConfigureLoop(LearningLoop loop)
        {
            loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet touched anything",
                e => e.Has(BodyExpeditionEvidence.AnyTouch)));

            // The gate that matters most, straight from the brief: the learner must have PRODUCED
            // both a pressure rise and a pressure collapse before either is explained, so that when
            // the field appears at Discover they have already felt both directions with their own
            // hands.
            loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet made both a pressure rise and a pressure collapse",
                e => e.Count(BodyExpeditionEvidence.VesselAdjust) >= 3
                  && e.Has(BodyExpeditionEvidence.PressureRise)
                  && e.Has(BodyExpeditionEvidence.PressureCollapse)));

            loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet tried both the heartbeat and the tilt",
                e => e.Has(BodyExpeditionEvidence.HeartRateUsed) && e.Has(BodyExpeditionEvidence.TiltUsed)));

            loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet practiced enough with the vessels",
                e => e.Count(BodyExpeditionEvidence.VesselAdjust) >= 8));

            loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet restored the patient's pressure",
                e => e.Has(BodyExpeditionEvidence.PatientRestored)));

            loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet predicted where flow would drop",
                e => e.Has(BodyExpeditionEvidence.PredictionGood)));

            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet built a tree that delivers evenly",
                e => e.Has(BodyExpeditionEvidence.TreeBalanced)));
        }

        protected override void OnStageEntered(LoopStage stage)
        {
            switch (stage)
            {
                case LoopStage.Apply:
                    _challenge.BeginPatient();
                    break;
                case LoopStage.Explain:
                    _challenge.BeginPrediction();
                    break;
                case LoopStage.Create:
                    _challenge.BeginBalance();
                    break;
                case LoopStage.Connect:
                    Knowledge?.Confirm(PrimaryConceptId, 0.4f);
                    Debug.Log("[PRISM] Body Expedition: Connect reached. The constellation changes.");
                    break;
            }
            base.OnStageEntered(stage);
        }

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        protected override void Tick(float dt)
        {
            HandleVesselGrab(dt);
            HandleHeartGrip(dt);
            HandleTiltDial(dt);

            Sim.Advance(dt);

            if (Sim.JustBeat && _heartGripHand != null) Hands.Buzz(_heartGripHand, 0.3f, 0.05f);

            SyncVesselViews();
            SyncPressureOrbs(dt);
            SyncHeartView();
            SyncTiltIndicator();
            SyncFormalizeReadout(dt);

            RecordPressureEvidence();

            _challenge?.Evaluate(dt);
        }

        // ---- interaction --------------------------------------------------

        /// <summary>
        /// Narrow or widen a vessel by pinching it with both hands and moving them together or
        /// apart — a two-hand stretch/squeeze, the same gesture grammar as resizing something in
        /// the air. Grabbing is by proximity to the vessel's CENTRELINE (closest point on the
        /// segment), not its rendered surface, so a thin peripheral vessel is exactly as easy to
        /// catch as the aorta.
        /// </summary>
        void HandleVesselGrab(float dt)
        {
            if (Hands == null) return;
            var L = Hands.Left;
            var R = Hands.Right;
            bool bothTracked = L != null && R != null && L.IsTracked && R.IsTracked;
            bool bothPinching = bothTracked && L.Pinch > 0.6f && R.Pinch > 0.6f;

            if (_adjustingVessel == null)
            {
                if (!bothPinching) return;

                Vector3 lLocal = Anchor.InverseTransformPoint(L.Position);
                Vector3 rLocal = Anchor.InverseTransformPoint(R.Position);

                VesselView best = null;
                float bestScore = float.MaxValue;
                for (int i = 0; i < _vessels.Length; i++)
                {
                    var v = _vessels[i];
                    Vector3 cl = ClosestPointOnSegment(lLocal, v.LocalFrom, v.LocalTo);
                    Vector3 cr = ClosestPointOnSegment(rLocal, v.LocalFrom, v.LocalTo);
                    float dl = Vector3.Distance(cl, lLocal);
                    float dr = Vector3.Distance(cr, rLocal);
                    if (dl > VesselGrabRadius || dr > VesselGrabRadius) continue;
                    float score = dl + dr;
                    if (score < bestScore) { bestScore = score; best = v; }
                }

                if (best != null)
                {
                    _adjustingVessel = best;
                    _adjustGrabHandDist = Mathf.Max(0.01f, Vector3.Distance(L.Position, R.Position));
                    _adjustGrabStartMult = best.Edge.RadiusMult;
                    Hands.Buzz(L, 0.2f, 0.05f);
                    Hands.Buzz(R, 0.2f, 0.05f);
                    Loop.Evidence.Record(BodyExpeditionEvidence.AnyTouch);
                }
                return;
            }

            if (!bothPinching)
            {
                _adjustingVessel = null;
                Loop.Evidence.Record(BodyExpeditionEvidence.VesselAdjust);
                Hands.Clunk(L, 0.3f);
                Hands.Clunk(R, 0.3f);
                return;
            }

            float dist = Mathf.Max(0.01f, Vector3.Distance(L.Position, R.Position));
            float ratio = dist / _adjustGrabHandDist;
            _adjustingVessel.Edge.RadiusMult = Mathf.Clamp(_adjustGrabStartMult * ratio,
                CirculationSim.MinRadiusMult, CirculationSim.MaxRadiusMult);
        }

        /// <summary>Grip the heart to speed it up — the harder the squeeze, the faster it beats.</summary>
        void HandleHeartGrip(float dt)
        {
            Vector3 heartWorld = Anchor.TransformPoint(HeartLocalPos);
            var h = NearestHand(heartWorld, 0.06f);
            bool gripping = h != null && h.IsTracked && h.Grip > 0.5f;
            _heartGripHand = gripping ? h : null;

            float targetMult = gripping ? Mathf.Lerp(0.6f, 1.9f, h.Grip) : 1f;
            Sim.HeartRateMult = Mathf.MoveTowards(Sim.HeartRateMult, targetMult, dt * 1.5f);

            if (gripping)
            {
                if (_heartHeldFor < 0.0001f) Loop.Evidence.Record(BodyExpeditionEvidence.AnyTouch);
                _heartHeldFor += dt;
                if (_heartHeldFor > 0.3f) Loop.Evidence.Record(BodyExpeditionEvidence.HeartRateUsed);
            }
            else _heartHeldFor = 0f;
        }

        /// <summary>Drag the small bead upward to stand the body up; downward lays it flat again.</summary>
        void HandleTiltDial(float dt)
        {
            Vector3 beadLocal = new Vector3(TiltDialX, Mathf.Lerp(TiltTrackBottomY, TiltTrackTopY, Sim.TiltFraction), TiltDialZ);
            Vector3 beadWorld = Anchor.TransformPoint(beadLocal);
            var h = NearestHand(beadWorld, 0.05f);
            bool gripping = h != null && h.IsTracked && h.Pinch > 0.6f;

            if (gripping)
            {
                Vector3 local = Anchor.InverseTransformPoint(PrismHands.PointOf(h));
                float t = Mathf.InverseLerp(TiltTrackBottomY, TiltTrackTopY, local.y);
                Sim.TiltFraction = Mathf.Clamp01(t);
                Loop.Evidence.Record(BodyExpeditionEvidence.AnyTouch);
                if (Sim.TiltFraction > 0.15f) Loop.Evidence.Record(BodyExpeditionEvidence.TiltUsed);
            }

            _tiltDial.localPosition = new Vector3(TiltDialX, Mathf.Lerp(TiltTrackBottomY, TiltTrackTopY, Sim.TiltFraction), TiltDialZ);
        }

        // ---- visual sync ----------------------------------------------------

        float EffectiveRadiusMult(Vessel e)
        {
            float m = e.RadiusMult;
            if (e.IsPeripheral) m *= Mathf.Pow(Mathf.Max(Sim.TprReflexMult, 0.05f), -0.25f);
            return Mathf.Clamp(m, 0.15f, 3.2f);
        }

        void SyncVesselViews()
        {
            bool systole = Sim.InSystole;
            for (int i = 0; i < _vessels.Length; i++)
            {
                var vv = _vessels[i];
                float mult = EffectiveRadiusMult(vv.Edge);
                float radius = vv.Edge.BaseRadius * mult * DisplayRadiusBoost;
                vv.T.localScale = new Vector3(radius, radius, vv.VisualLength);

                float flow = Sim.Flow(vv.Edge);                          // mL/s, signed, From->To positive
                float area = Mathf.Max(Mathf.PI * radius * radius, 1e-8f);
                float speed = flow * 1e-6f / area;                       // m/s, signed
                float speedAbs = Mathf.Abs(speed);

                vv.Mat.SetColor("_Tint", PrismPalette.Spectral(Mathf.InverseLerp(SpeedLo, SpeedHi, speedAbs)));
                vv.Mat.SetFloat("_Speed", Mathf.Clamp(speed * PacketSpeedScale, -2.8f, 2.8f));
                vv.Mat.SetFloat("_Pulse", systole ? 1f : 0f);
            }
        }

        void SyncPressureOrbs(float dt)
        {
            float target = (Loop.Stage >= LoopStage.Discover) ? 1f : 0f;
            _pressureReveal = Mathf.MoveTowards(_pressureReveal, target, dt / 4f);

            for (int n = 0; n < _orbs.Length; n++)
            {
                var o = _orbs[n];
                float p = Sim.PressureAt(n);
                float norm = Mathf.Clamp(p / CirculationSim.RestingMap, 0.15f, 2.8f);
                float s = o.BaseRadius * Mathf.Sqrt(norm) * _pressureReveal;
                o.T.localScale = Vector3.one * Mathf.Max(s, 0.00001f);

                float glow = Mathf.Clamp01(norm / 1.7f);
                if (o.Mat.HasProperty("_Luminance")) o.Mat.SetFloat("_Luminance", Mathf.Lerp(0.12f, 0.85f, glow));
                else if (o.Mat.HasProperty("_Density")) o.Mat.SetFloat("_Density", Mathf.Lerp(0.5f, 1.6f, glow));
            }
        }

        void SyncHeartView()
        {
            float pulse = Sim.InSystole ? Mathf.Sin(Mathf.Clamp01(Sim.BeatPhase01 / 0.32f) * Mathf.PI) : 0f;
            _heart.localScale = Vector3.one * (0.022f * (1f + pulse * 0.14f));
            _heartMat.SetFloat("_Luminance", Mathf.Lerp(0.30f, 0.55f, pulse));
        }

        void SyncTiltIndicator()
        {
            _tiltIndicator.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(90f, 0f, Sim.TiltFraction));
        }

        void SyncFormalizeReadout(float dt)
        {
            float target = (Loop.Stage >= LoopStage.Formalize) ? 1f : 0f;
            _formalizeReveal = Mathf.MoveTowards(_formalizeReveal, target, dt / 3f);

            float totalFlow = Sim.Flow(Sim.Edges[CirculationSim.EArmVenous]) + Sim.Flow(Sim.Edges[CirculationSim.EHeadVenous])
                             + Sim.Flow(Sim.Edges[CirculationSim.ELegLVenous]) + Sim.Flow(Sim.Edges[CirculationSim.ELegRVenous]);
            float gap = Mathf.Abs(Sim.TotalHead((int)BodyNode.Aorta) - Sim.TotalHead((int)BodyNode.Venous));

            float gapH = Mathf.Clamp01(gap / 6f) * BarMaxHeight * _formalizeReveal;
            float flowH = Mathf.Clamp01(Mathf.Abs(totalFlow) / 40f) * BarMaxHeight * _formalizeReveal;
            _pressureBar.localScale = new Vector3(BarThickness, BarThickness, Mathf.Max(gapH, 0.00001f));
            _flowBar.localScale = new Vector3(BarThickness, BarThickness, Mathf.Max(flowH, 0.00001f));

            Vector3 aortaWorld = Anchor.TransformPoint(NodeLocalPos[(int)BodyNode.Aorta]);
            bool near = _formalizeReveal > 0.5f && HandNear(aortaWorld, 0.45f);
            _readout.Show(near);
            if (near)
            {
                string phase = Sim.InSystole ? "systole" : "diastole";
                string body = phase + "\nresistance and pressure gap set the flow\n" + totalFlow.ToString("0.0") + " mL/s";
                _readout.SetText(body, PrismPalette.Coral);
                _readout.PlaceAbove(aortaWorld, 0.07f);
            }
        }

        void RecordPressureEvidence()
        {
            float map = Sim.MeanArterialPressureFiltered;
            if (map > CirculationSim.PressureRiseThreshold) Loop.Evidence.Record(BodyExpeditionEvidence.PressureRise);
            if (map < CirculationSim.PressureCollapseThreshold) Loop.Evidence.Record(BodyExpeditionEvidence.PressureCollapse);
        }

        bool HandNear(Vector3 worldPoint, float range)
        {
            if (Hands == null) return false;
            if (Hands.Left != null && Hands.Left.IsTracked && Vector3.Distance(Hands.Left.Position, worldPoint) < range) return true;
            if (Hands.Right != null && Hands.Right.IsTracked && Vector3.Distance(Hands.Right.Position, worldPoint) < range) return true;
            return false;
        }

        // ---- small static helpers -------------------------------------------

        static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-10f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return a + ab * t;
        }

        static Quaternion SafeLookRotation(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-10f) return Quaternion.identity;
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(dir.normalized, up)) > 0.999f) up = Vector3.forward;
            return Quaternion.LookRotation(dir.normalized, up);
        }

        static Vector3 Az(float azimuthDeg, float y) =>
            new Vector3(Mathf.Sin(azimuthDeg * Mathf.Deg2Rad), y, Mathf.Cos(azimuthDeg * Mathf.Deg2Rad));

        // -----------------------------------------------------------------
        // concepts
        // -----------------------------------------------------------------

        static ConceptSpec[] BuildConceptSpecs()
        {
            return new[]
            {
                new ConceptSpec
                {
                    Id = "body-expedition", Title = "Body Expedition",
                    Domain = ConceptDomain.BodyAndMind, Kind = KnowledgeKind.System,
                    Direction = Az(48f, 0.05f), Distance = 3.6f,
                    WorldId = "body-expedition",
                    Capability = "Read a pressure and a flow, and say which vessel to blame.",
                    Formalisation = "The circulation is a closed loop of compliant chambers and " +
                                    "resistive vessels driven by a pulsatile pump; pressure and " +
                                    "flow at every point are coupled through the whole network at once.",
                    Links = new (string, Relation, float)[]
                    {
                        ("pressure-and-flow", Relation.Composes, 0.9f),
                        ("resistance", Relation.Composes, 0.85f),
                        ("homeostasis", Relation.Composes, 0.8f),
                        ("cardiac-cycle", Relation.Composes, 0.8f),
                    }
                },
                new ConceptSpec
                {
                    Id = "pressure-and-flow", Title = "Pressure and Flow",
                    Domain = ConceptDomain.BodyAndMind, Kind = KnowledgeKind.Theory,
                    Direction = Az(40f, -0.10f), Distance = 4.0f,
                    Capability = "Predict which way a fluid moves from what its pressure is doing at both ends.",
                    Formalisation = "Flow moves from high total pressure toward low. In a connected " +
                                    "network, changing one vessel changes the pressure everywhere " +
                                    "else on the same loop.",
                    Links = new (string, Relation, float)[]
                    {
                        ("resistance", Relation.Constrains, 0.85f),
                    }
                },
                new ConceptSpec
                {
                    Id = "resistance", Title = "Vascular Resistance",
                    Domain = ConceptDomain.BodyAndMind, Kind = KnowledgeKind.Equation,
                    Direction = Az(56f, 0.15f), Distance = 4.3f,
                    Capability = "Know that halving a tube's radius is not a small change.",
                    Formalisation = "Flow equals the pressure difference divided by resistance, and " +
                                    "resistance rises as the fourth power of 1/radius: half the " +
                                    "radius, sixteen times the resistance.",
                    Links = new (string, Relation, float)[]
                    {
                        ("inverse-square", Relation.Analogy, 0.35f),
                    }
                },
                new ConceptSpec
                {
                    Id = "homeostasis", Title = "Homeostasis",
                    Domain = ConceptDomain.BodyAndMind, Kind = KnowledgeKind.Theory,
                    Direction = Az(64f, -0.05f), Distance = 4.8f,
                    Capability = "Recognise a body pushing back against a change you just made to it.",
                    Formalisation = "A sensed error drives a correction sized to that error: the " +
                                    "baroreflex watches pressure and leans on resistance and heart " +
                                    "rate to bring it back toward its setpoint.",
                    Links = new (string, Relation, float)[]
                    {
                        ("feedback", Relation.Analogy, 0.85f),
                        ("pressure-and-flow", Relation.Constrains, 0.7f),
                    }
                },
                new ConceptSpec
                {
                    Id = "cardiac-cycle", Title = "The Cardiac Cycle",
                    Domain = ConceptDomain.BodyAndMind, Kind = KnowledgeKind.Process,
                    Direction = Az(70f, 0.20f), Distance = 5.2f,
                    Capability = "Feel the difference between the heart pushing and the heart filling.",
                    Formalisation = "Systole ejects, diastole fills; the beat repeats with a period " +
                                    "set by heart rate — the same shape as any other periodic motion.",
                    Links = new (string, Relation, float)[]
                    {
                        ("periodic-motion", Relation.Analogy, 0.85f),
                        ("pressure-and-flow", Relation.Causes, 0.75f),
                    }
                },
            };
        }
    }
}
