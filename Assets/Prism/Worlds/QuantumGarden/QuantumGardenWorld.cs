using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using Prism.Worlds;
using UnityEngine;

namespace Prism.Worlds.QuantumGarden
{
    /// <summary>Evidence keys. Named constants because the gates and the world must agree exactly.</summary>
    public static class QuantumEvidence
    {
        public const string Detection = "detection";
        public const string SlitToggle = "slit.toggle";
        public const string SeparationChanged = "separation.changed";
        public const string WavelengthChanged = "wavelength.changed";
        public const string PatternInterference = "pattern.interference";
        public const string PatternSingle = "pattern.single";
        public const string PatternInterferencePostField = "pattern.interference.field";
        public const string PatternSinglePostField = "pattern.single.field";
        public const string ChallengeDone = "apply.challenge.done";
        public const string PredictionGood = "explain.prediction.good";
        public const string CreationComplete = "create.done";
    }

    /// <summary>
    /// The Quantum Garden — a two-slit apparatus, run honestly.
    ///
    ///   Wonder     A source, a barrier with two gaps, a screen. Particles arrive one at a time
    ///              and land somewhere definite. Nothing is labelled, nothing is explained. Wait
    ///              long enough and a pattern the learner did not predict builds up out of dots.
    ///   Explore    They can drag either slit, cover one with a bare hand, and stretch the
    ///              wavelength gauge with both hands. No words yet.
    ///   Discover   Only once they have personally grown BOTH the two-slit pattern and the
    ///              one-slit pattern — the straddle — does the amplitude field itself appear:
    ///              the continuous |psi|^2 the dots have been sampling all along.
    ///   Formalize  One quiet label, spatial, brief: superposition, amplitude, the Born rule.
    ///   Apply      A faint target comb of fringes; tune separation and wavelength to match it.
    ///   Explain    Predict the densest band before it is confirmed: place a marker, then watch
    ///              a hundred particles decide whether the marker was right.
    ///   Create     Choose an apparatus of their own and let a fresh pattern grow in.
    ///   Connect    Return to the atrium; the constellation has changed.
    ///
    /// See NOTES.md for exactly what is honest physics and what is a stated simplification.
    /// </summary>
    public class QuantumGardenWorld : PrismWorldBase
    {
        public override string WorldId => "quantum-garden";

        /// <summary>A vacuum that is not empty — the point of the whole world.</summary>
        protected override WorldShellSpec Shell => WorldShellSpec.QuantumVacuum;
        public override string PrimaryConceptId => "quantum-garden";
        public override string DisplayName => "Quantum Garden";
        public override string[] Shaders => new[] { "Prism/QuantumField" };

        public TwoSlitSim Sim { get; private set; }
        public QuantumScreen DetectorScreen { get; private set; }

        // ---- tuning constants -------------------------------------------------------------
        const int WonderDetections = 18;
        const int InterferenceThreshold = 55;
        const int SingleThreshold = 45;
        const int CreateThreshold = 130;

        const float BlockRadius = 0.05f;
        const float SlitGrabRadius = 0.045f;

        const float GaugeHandMin = 0.05f, GaugeHandMax = 0.35f;
        const float GaugeGrabRadius = 0.06f;

        const float EmitInterval = 0.20f;
        const float FlightDuration = 0.22f;
        const float DetectDelay = 0.37f;
        const int FlightPoolSize = 6;
        const int PendingCap = 24;

        const float ApplyTolerance = 0.09f;
        const float ApplyHoldNeeded = 1.2f;
        const int BurstTotal = 100;
        const int BurstPerFrame = 6;

        const float BarrierHeight = 0.09f;
        const float BarrierThickness = 0.012f;
        const float BarrierHalfWidth = TwoSlitSim.SlitXMax + 0.06f;

        const int GhostMaxTicks = 13;

        // ---- geometry -----------------------------------------------------------------------
        Transform _source;
        Transform _wallLeft, _wallMid, _wallRight;
        Transform _markerLeftSlit, _markerRightSlit;
        Material _markerLeftMat, _markerRightMat;

        struct FlightMote { public Transform View; public float T; public bool Active; }
        FlightMote[] _flights;

        struct Pending { public float X, Y, RevealTime; public bool Valid; }
        Pending[] _pending;
        float _emitAcc;

        Transform _gaugeGroup, _gaugeLeft, _gaugeRight, _gaugeTubeView;
        Material _gaugeLeftMat, _gaugeRightMat, _gaugeTubeMat;
        Mesh _gaugeTubeMesh;
        float _gaugeLastSpan = -1f;
        readonly List<Vector3> _gaugePts = new List<Vector3>(2) { Vector3.zero, Vector3.zero };

        Transform _predictionMarker;
        Material _predictionMarkerMat;
        Vector3 _markerLocal;
        bool _wasPinchingForMarker;

        Mesh _ghostMesh;
        Transform _ghostView;
        Material _ghostMat;
        readonly List<Vector3> _ghostVerts = new List<Vector3>(GhostMaxTicks * 4);
        readonly List<int> _ghostTris = new List<int>(GhostMaxTicks * 6);

        PrismLabel _formalLabel;

        // ---- interaction / stage state --------------------------------------------------
        PrismHands.Hand _draggingLeft, _draggingRight;
        int _stableRunCount;

        enum ExplainPhase { Inactive, Placing, Bursting, Done }
        ExplainPhase _explainPhase = ExplainPhase.Inactive;
        int _burstRemaining;

        bool _applyDone;
        float _applyHoldTime;
        float _targetSpacing;

        float _fieldReveal;
        LoopStage _lastKnownStage = (LoopStage)(-1);

        // -----------------------------------------------------------------
        // setup
        // -----------------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            // The apparatus is a lab bench, not a solar system: it wants to sit closer and a
            // little lower than the base default so the slits fall inside comfortable reach.
            EyeToTable = 0.40f;
            Reach = 0.50f;
        }

        protected override void BuildWorld()
        {
            Sim = new TwoSlitSim();
            DetectorScreen = new QuantumScreen();
            DetectorScreen.Build(Anchor);

            BuildSource();
            BuildBarrierAndSlits();
            BuildFlightPool();
            BuildGauge();
            BuildMarker();
            BuildGhost();

            _formalLabel = PrismLabel.Create("FormalLabel", Anchor, Head, 0.016f);

            _pending = new Pending[PendingCap];
        }

        void BuildSource()
        {
            var mat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.55f, 340f);
            _source = Body(PrismMesh.Icosphere(2), 0.014f, mat, "Source");
            _source.localPosition = new Vector3(0f, 0f, TwoSlitSim.SourceZ);
        }

        void BuildBarrierAndSlits()
        {
            var wallMat = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.14f, 0f);
            _wallLeft = MakeWallSegment(wallMat, "WallLeft");
            _wallMid = MakeWallSegment(wallMat, "WallMid");
            _wallRight = MakeWallSegment(wallMat, "WallRight");

            _markerLeftMat = PrismMaterials.CeramicBody(PrismPalette.Cyan, 0.6f, 300f);
            _markerRightMat = PrismMaterials.CeramicBody(PrismPalette.Cyan, 0.6f, 300f);
            _markerLeftSlit = Body(PrismMesh.Icosphere(2), 0.011f, _markerLeftMat, "SlitLeft");
            _markerRightSlit = Body(PrismMesh.Icosphere(2), 0.011f, _markerRightMat, "SlitRight");

            UpdateBarrierGeometry();
        }

        Transform MakeWallSegment(Material mat, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Anchor, false);
            go.AddComponent<MeshFilter>().sharedMesh = QuantumMesh.UnitCube();
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        void BuildFlightPool()
        {
            _flights = new FlightMote[FlightPoolSize];
            var mat = PrismMaterials.CeramicBody(PrismPalette.Cyan, 0.8f, 260f);
            for (int i = 0; i < FlightPoolSize; i++)
            {
                var t = Body(PrismMesh.Icosphere(2), 0.009f, mat, "Flight" + i);
                t.gameObject.SetActive(false);
                _flights[i] = new FlightMote { View = t, Active = false, T = 0f };
            }
        }

        void BuildGauge()
        {
            var go = new GameObject("WavelengthGauge");
            go.transform.SetParent(Anchor, false);
            go.transform.localPosition = new Vector3(0f, 0.09f, TwoSlitSim.SourceZ);
            _gaugeGroup = go.transform;

            _gaugeLeftMat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.6f, 350f);
            _gaugeRightMat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.6f, 350f);
            _gaugeLeft = Body(PrismMesh.Icosphere(2), 0.010f, _gaugeLeftMat, "GaugeLeft");
            _gaugeRight = Body(PrismMesh.Icosphere(2), 0.010f, _gaugeRightMat, "GaugeRight");
            _gaugeLeft.SetParent(_gaugeGroup, false);
            _gaugeRight.SetParent(_gaugeGroup, false);

            _gaugeTubeMesh = new Mesh { name = "GaugeTube" };
            _gaugeTubeMesh.MarkDynamic();
            var tgo = new GameObject("GaugeTube");
            tgo.transform.SetParent(_gaugeGroup, false);
            tgo.AddComponent<MeshFilter>().sharedMesh = _gaugeTubeMesh;
            _gaugeTubeMat = PrismMaterials.ForRelation(Relation.Measures, 0.7f);
            tgo.AddComponent<MeshRenderer>().sharedMaterial = _gaugeTubeMat;
            _gaugeTubeView = tgo.transform;

            UpdateGaugeVisual(true);
        }

        void BuildMarker()
        {
            _predictionMarkerMat = PrismMaterials.CeramicBody(PrismPalette.Coral, 0.7f, 400f);
            _predictionMarker = Body(PrismMesh.Icosphere(2), 0.013f, _predictionMarkerMat, "PredictionMarker");
            _predictionMarker.gameObject.SetActive(false);
        }

        void BuildGhost()
        {
            _ghostMesh = new Mesh { name = "FringeGhost" };
            _ghostMesh.MarkDynamic();
            var go = new GameObject("FringeGhost");
            go.transform.SetParent(Anchor, false);
            go.transform.localPosition = new Vector3(0f, 0f, TwoSlitSim.ScreenZ - 0.006f);
            go.AddComponent<MeshFilter>().sharedMesh = _ghostMesh;

            _ghostMat = PrismMaterials.New(PrismMaterials.Mote);
            _ghostMat.SetColor("_Tint", PrismPalette.Mint);
            _ghostMat.SetColor("_EdgeTint", PrismPalette.Cyan);
            _ghostMat.SetFloat("_Density", 0.9f);
            go.AddComponent<MeshRenderer>().sharedMaterial = _ghostMat;
            _ghostView = go.transform;
            _ghostView.gameObject.SetActive(false);
        }

        // -----------------------------------------------------------------
        // loop
        // -----------------------------------------------------------------

        protected override void ConfigureLoop(LearningLoop loop)
        {
            loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet watched enough particles land to notice a pattern forming",
                e => e.Count(QuantumEvidence.Detection) >= WonderDetections));

            // The straddle: both the two-slit pattern and the one-slit pattern, personally grown,
            // with at least two real open/close toggles behind them.
            loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet grown both the two-slit pattern and the one-slit pattern",
                e => e.Has(QuantumEvidence.PatternInterference)
                  && e.Has(QuantumEvidence.PatternSingle)
                  && e.Count(QuantumEvidence.SlitToggle) >= 2));

            // Not just "has the field been visible for a while" -- has the learner reproduced
            // BOTH patterns again with the field actually on screen, so the dot-by-dot buildup
            // and the continuous envelope are connected by something they did, not by a timer.
            loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet regrown both patterns with the amplitude field visible",
                e => e.Has(QuantumEvidence.PatternInterferencePostField)
                  && e.Has(QuantumEvidence.PatternSinglePostField)));

            loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet changed the wavelength or the separation since the field appeared",
                e => (e.Count(QuantumEvidence.WavelengthChanged) + e.Count(QuantumEvidence.SeparationChanged)) >= 2));

            loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet tuned the apparatus to match the target fringe spacing",
                e => e.Has(QuantumEvidence.ChallengeDone)));

            loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet predicted where the pattern would be densest",
                e => e.Has(QuantumEvidence.PredictionGood)));

            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet grown a pattern of their own choosing",
                e => e.Has(QuantumEvidence.CreationComplete)));
        }

        protected override void OnStageEntered(LoopStage stage)
        {
            base.OnStageEntered(stage);
            _lastKnownStage = stage;
            ApplyStageSetup(stage);
        }

        /// <summary>
        /// One-shot setup for a freshly entered stage. Reachable two ways: the normal live
        /// transition (via OnStageEntered) and, for a RETURNING learner whose saved progress
        /// starts mid-loop, the catch-up poll in Tick — LearningLoop only fires StageEntered on
        /// a transition it performs itself, never for the stage a resumed loop simply starts at.
        /// Without this, a learner resuming at Apply would find a target-less challenge and no
        /// way to satisfy its gate.
        /// </summary>
        void ApplyStageSetup(LoopStage stage)
        {
            switch (stage)
            {
                case LoopStage.Apply:
                    BeginApplyChallenge();
                    break;
                case LoopStage.Explain:
                    if (_ghostView != null) _ghostView.gameObject.SetActive(false);
                    BeginExplain();
                    break;
                case LoopStage.Create:
                    if (_predictionMarker != null) _predictionMarker.gameObject.SetActive(false);
                    _stableRunCount = 0;
                    DetectorScreen.Clear();
                    break;
                case LoopStage.Connect:
                    Knowledge?.Confirm(PrimaryConceptId, 0.4f);
                    Debug.Log("[PRISM] Quantum Garden: Connect reached. The constellation changes.");
                    break;
                case LoopStage.Formalize:
                    ShowFormalLabel();
                    break;
            }
        }

        void ShowFormalLabel()
        {
            _formalLabel.SetText(
                "every open path adds an amplitude\nprobability = amplitude squared (the Born rule)",
                PrismPalette.Spectral(0.55f));
        }

        // -----------------------------------------------------------------
        // constellation
        // -----------------------------------------------------------------

        public override IEnumerable<ConceptSpec> Concepts => BuildConcepts();

        static IEnumerable<ConceptSpec> BuildConcepts()
        {
            yield return new ConceptSpec
            {
                Id = "quantum-garden",
                Title = "Quantum Garden",
                Domain = ConceptDomain.MatterAndEnergy,
                Kind = KnowledgeKind.System,
                Direction = WedgeDir(90f, 0.05f),
                Distance = 3.6f,
                WorldId = "quantum-garden",
                Capability = "Grow an interference pattern one particle at a time, and say why it is there.",
                Formalisation = "A source of definite, localised detections whose density over many " +
                                "trials follows the squared magnitude of a complex amplitude that itself " +
                                "obeys ordinary wave addition.",
                Links = new (string, Relation, float)[]
                {
                    ("superposition", Relation.Composes, 0.85f),
                    ("measurement", Relation.Composes, 0.85f),
                    ("quantisation", Relation.Composes, 0.75f),
                }
            };

            yield return new ConceptSpec
            {
                Id = "superposition",
                Title = "Superposition",
                Domain = ConceptDomain.MatterAndEnergy,
                Kind = KnowledgeKind.Uncertainty,
                Direction = WedgeDir(78f, 0.30f),
                Distance = 4.4f,
                Capability = "Let an amplitude spread over every option still open, and add the options " +
                             "before asking which one happened.",
                Formalisation = "The state of a system that has not been measured is a sum of amplitudes " +
                                "over every possibility consistent with what IS known -- not a single " +
                                "unknown fact waiting to be found out.",
                Links = new (string, Relation, float)[]
                {
                    ("waves", Relation.Analogy, 0.9f),
                    ("measurement", Relation.Constrains, 0.6f),
                }
            };

            yield return new ConceptSpec
            {
                Id = "measurement",
                Title = "Measurement",
                Domain = ConceptDomain.MatterAndEnergy,
                Kind = KnowledgeKind.Equation,
                Direction = WedgeDir(96f, -0.25f),
                Distance = 4.7f,
                Capability = "Predict not where one particle will land, but how densely many of them will.",
                Formalisation = "The Born rule: the probability of a detection in a region is proportional " +
                                "to the squared magnitude of the amplitude there. The amplitude itself is " +
                                "not a probability and can be negative or complex; only its square is.",
                Links = new (string, Relation, float)[]
                {
                    ("probability", Relation.Instantiates, 0.85f),
                    ("superposition", Relation.Measures, 0.7f),
                }
            };

            yield return new ConceptSpec
            {
                Id = "quantisation",
                Title = "Quantisation",
                Domain = ConceptDomain.MatterAndEnergy,
                Kind = KnowledgeKind.Fact,
                Direction = WedgeDir(102f, 0.10f),
                Distance = 5.1f,
                Capability = "Recognise the discreteness underneath a perfectly smooth-looking pattern.",
                Formalisation = "Whatever the wave predicts, each detection arrives whole: one bright " +
                                "point, never a fraction of one, however faint the source.",
                Links = new (string, Relation, float)[]
                {
                    ("measurement", Relation.Constrains, 0.6f),
                }
            };
        }

        /// <summary>
        /// Constellation placement, in the direction wedge assigned to this world (azimuth 72-108
        /// degrees measured clockwise from forward, elevation as a small raw y-offset before
        /// normalisation) -- see NOTES.md for exactly how this maps onto ConceptDefinition.direction.
        /// </summary>
        static Vector3 WedgeDir(float azimuthDegrees, float elevation)
        {
            float az = azimuthDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az), elevation, Mathf.Cos(az));
        }

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        protected override void Tick(float dt)
        {
            CatchUpStage();

            bool frozen = _explainPhase == ExplainPhase.Bursting;

            TickEmission(dt, frozen);
            TickPendingDetections();
            TickFlights(dt);

            if (!frozen)
            {
                TickSlitBlocking();
                TickSlitDragging();
                TickWavelengthGauge();
            }
            UpdateBarrierGeometry();

            TickFieldReveal(dt);
            if (Loop.Stage >= LoopStage.Discover) DetectorScreen.SyncField(Sim);
            DetectorScreen.Apply();

            _formalLabel.Show(Loop.Stage == LoopStage.Formalize);
            if (Loop.Stage == LoopStage.Formalize)
                _formalLabel.PlaceAbove(DetectorScreen.Root.position, TwoSlitSim.ScreenHalfHeight + 0.06f);

            if (Loop.Stage == LoopStage.Apply) TickApplyChallenge(dt);
            if (Loop.Stage == LoopStage.Explain) TickExplain();
        }

        /// <summary>See the comment on ApplyStageSetup: catches up a resumed loop that starts mid-way.</summary>
        void CatchUpStage()
        {
            if (_lastKnownStage == Loop.Stage) return;
            _lastKnownStage = Loop.Stage;
            ApplyStageSetup(Loop.Stage);
        }

        // ---- emission and detection ------------------------------------------------------

        void TickEmission(float dt, bool frozen)
        {
            if (frozen) return;
            _emitAcc += dt;
            int guard = 0;
            while (_emitAcc >= EmitInterval && guard++ < 8)
            {
                _emitAcc -= EmitInterval;
                EmitParticle();
            }
        }

        void EmitParticle()
        {
            float x = Sim.SampleDetectionX();
            float y = Random.Range(-TwoSlitSim.ScreenHalfHeight * 0.9f, TwoSlitSim.ScreenHalfHeight * 0.9f);
            SchedulePending(x, y, Time.time + DetectDelay);
            SpawnFlight();
        }

        void SchedulePending(float x, float y, float revealTime)
        {
            for (int i = 0; i < PendingCap; i++)
            {
                if (_pending[i].Valid) continue;
                _pending[i] = new Pending { X = x, Y = y, RevealTime = revealTime, Valid = true };
                return;
            }
            // The pool is sized well above steady-state occupancy at EmitInterval/DetectDelay; if
            // it is ever exhausted, dropping one visual flourish is harmless and growing the pool
            // at runtime is not.
        }

        void TickPendingDetections()
        {
            float now = Time.time;
            for (int i = 0; i < PendingCap; i++)
            {
                if (!_pending[i].Valid || now < _pending[i].RevealTime) continue;
                _pending[i].Valid = false;
                CommitDetection(_pending[i].X, _pending[i].Y);
            }
        }

        void SpawnFlight()
        {
            for (int i = 0; i < _flights.Length; i++)
            {
                if (_flights[i].Active) continue;
                _flights[i].Active = true;
                _flights[i].T = 0f;
                _flights[i].View.gameObject.SetActive(true);
                return;
            }
            // Every slot busy at this pace should not happen; if it does, the newest particle
            // simply travels without a visible spark, which is cosmetic only.
        }

        void TickFlights(float dt)
        {
            var from = new Vector3(0f, 0f, TwoSlitSim.SourceZ);
            var to = new Vector3(0f, 0f, TwoSlitSim.SlitZ);
            for (int i = 0; i < _flights.Length; i++)
            {
                if (!_flights[i].Active) continue;
                _flights[i].T += dt / FlightDuration;
                if (_flights[i].T >= 1f)
                {
                    _flights[i].Active = false;
                    _flights[i].View.gameObject.SetActive(false);
                    continue;
                }
                // A straight run from the source to the barrier's own axis, never toward either
                // slit in particular -- see NOTES.md on why the flight never depicts a path
                // through the apparatus, only the parts that are genuinely observed.
                _flights[i].View.localPosition = Vector3.Lerp(from, to, _flights[i].T);
            }
        }

        /// <summary>The one place a detection actually counts: recorded, drawn, and checked against every gate that cares.</summary>
        void CommitDetection(float x, float y)
        {
            DetectorScreen.AddDetection(x, y);
            Loop.Evidence.Record(QuantumEvidence.Detection);
            _stableRunCount++;

            bool bothOpen = Sim.LeftOpen && Sim.RightOpen;
            bool oneOpen = Sim.LeftOpen ^ Sim.RightOpen;
            bool fieldVisible = Loop.Stage >= LoopStage.Discover;

            if (bothOpen && _stableRunCount >= InterferenceThreshold)
            {
                Loop.Evidence.Record(QuantumEvidence.PatternInterference);
                if (fieldVisible) Loop.Evidence.Record(QuantumEvidence.PatternInterferencePostField);
            }
            if (oneOpen && _stableRunCount >= SingleThreshold)
            {
                Loop.Evidence.Record(QuantumEvidence.PatternSingle);
                if (fieldVisible) Loop.Evidence.Record(QuantumEvidence.PatternSinglePostField);
            }
            if (Loop.Stage >= LoopStage.Create && _stableRunCount >= CreateThreshold)
                Loop.Evidence.Record(QuantumEvidence.CreationComplete);
        }

        /// <summary>A deliberate change starts a fresh plate -- like loading a new photographic plate for a new configuration.</summary>
        void OnConfigChanged()
        {
            _stableRunCount = 0;
            DetectorScreen.Clear();
        }

        // ---- slits: block and drag --------------------------------------------------------

        void TickSlitBlocking()
        {
            Vector3 leftWorld = Anchor.TransformPoint(new Vector3(Sim.LeftSlitX, 0f, TwoSlitSim.SlitZ));
            Vector3 rightWorld = Anchor.TransformPoint(new Vector3(Sim.RightSlitX, 0f, TwoSlitSim.SlitZ));

            bool blockLeft = IsHandBlocking(leftWorld);
            bool blockRight = IsHandBlocking(rightWorld);

            bool changed = Sim.SetOpen(true, !blockLeft);
            changed |= Sim.SetOpen(false, !blockRight);
            if (changed)
            {
                OnConfigChanged();
                Loop.Evidence.Record(QuantumEvidence.SlitToggle);
            }
        }

        bool IsHandBlocking(Vector3 worldPos)
        {
            if (Hands == null) return false;
            foreach (var h in new[] { Hands.Left, Hands.Right })
            {
                if (h == null || !h.IsTracked) continue;
                if (h.Pinch > 0.5f) continue;   // a pinching hand is grabbing, not covering the gap
                if ((PrismHands.PointOf(h) - worldPos).sqrMagnitude < BlockRadius * BlockRadius) return true;
            }
            return false;
        }

        void TickSlitDragging()
        {
            _draggingLeft = UpdateDrag(_draggingLeft, true);
            _draggingRight = UpdateDrag(_draggingRight, false);
        }

        PrismHands.Hand UpdateDrag(PrismHands.Hand current, bool isLeft)
        {
            if (Hands == null) return null;

            if (current != null)
            {
                if (!current.IsTracked || current.Pinch < 0.5f) return null;
                float localX = Anchor.InverseTransformPoint(PrismHands.PointOf(current)).x;
                bool changed = isLeft ? Sim.SetLeftSlitX(localX) : Sim.SetRightSlitX(localX);
                if (changed)
                {
                    OnConfigChanged();
                    Loop.Evidence.Record(QuantumEvidence.SeparationChanged);
                }
                return current;
            }

            float slitX = isLeft ? Sim.LeftSlitX : Sim.RightSlitX;
            Vector3 worldPos = Anchor.TransformPoint(new Vector3(slitX, 0f, TwoSlitSim.SlitZ));

            foreach (var h in new[] { Hands.Left, Hands.Right })
            {
                if (h == null || !h.IsTracked || h.Pinch < 0.6f) continue;
                if (h == _draggingLeft || h == _draggingRight) continue;   // already busy with the other slit
                if ((PrismHands.PointOf(h) - worldPos).sqrMagnitude > SlitGrabRadius * SlitGrabRadius) continue;
                Hands.Buzz(h, 0.22f, 0.04f);
                return h;
            }
            return null;
        }

        void UpdateBarrierGeometry()
        {
            float lx = Sim.LeftSlitX, rx = Sim.RightSlitX;
            float halfSlit = TwoSlitSim.SlitWidth * 0.5f;

            PlaceWall(_wallLeft, -BarrierHalfWidth, lx - halfSlit);
            PlaceWall(_wallMid, lx + halfSlit, rx - halfSlit);
            PlaceWall(_wallRight, rx + halfSlit, BarrierHalfWidth);

            _markerLeftSlit.localPosition = new Vector3(lx, 0f, TwoSlitSim.SlitZ);
            _markerRightSlit.localPosition = new Vector3(rx, 0f, TwoSlitSim.SlitZ);

            SetSlitMarkerOpen(_markerLeftMat, Sim.LeftOpen);
            SetSlitMarkerOpen(_markerRightMat, Sim.RightOpen);
        }

        static void PlaceWall(Transform t, float xLo, float xHi)
        {
            float width = Mathf.Max(xHi - xLo, 0.0005f);
            float cx = (xLo + xHi) * 0.5f;
            t.localPosition = new Vector3(cx, 0f, TwoSlitSim.SlitZ);
            t.localScale = new Vector3(width, BarrierHeight, BarrierThickness);
        }

        static void SetSlitMarkerOpen(Material mat, bool open)
        {
            mat.SetColor("_Tint", open ? PrismPalette.Cyan : new Color(0.42f, 0.42f, 0.44f));
            mat.SetFloat("_Luminance", open ? 0.6f : 0.12f);
        }

        // ---- wavelength gauge --------------------------------------------------------------

        void TickWavelengthGauge()
        {
            if (Hands == null) return;
            bool leftAt = Hands.Left != null && Hands.Left.IsTracked && Hands.Left.Pinch > 0.6f
                       && (Hands.Left.Position - _gaugeLeft.position).sqrMagnitude < GaugeGrabRadius * GaugeGrabRadius;
            bool rightAt = Hands.Right != null && Hands.Right.IsTracked && Hands.Right.Pinch > 0.6f
                        && (Hands.Right.Position - _gaugeRight.position).sqrMagnitude < GaugeGrabRadius * GaugeGrabRadius;

            if (leftAt && rightAt)
            {
                float handDist = Vector3.Distance(Hands.Left.Position, Hands.Right.Position);
                float t = Mathf.InverseLerp(GaugeHandMin, GaugeHandMax, handDist);
                float lambda = Mathf.Lerp(TwoSlitSim.WavelengthMin, TwoSlitSim.WavelengthMax, t);
                if (Sim.SetWavelength(lambda))
                {
                    OnConfigChanged();
                    Loop.Evidence.Record(QuantumEvidence.WavelengthChanged);
                }
            }
            UpdateGaugeVisual(false);
        }

        void UpdateGaugeVisual(bool force)
        {
            float t = Mathf.InverseLerp(TwoSlitSim.WavelengthMin, TwoSlitSim.WavelengthMax, Sim.Wavelength);
            float span = Mathf.Lerp(GaugeHandMin, GaugeHandMax, t);
            if (!force && Mathf.Abs(span - _gaugeLastSpan) < 0.001f) return;
            _gaugeLastSpan = span;

            _gaugeLeft.localPosition = new Vector3(-span * 0.5f, 0f, 0f);
            _gaugeRight.localPosition = new Vector3(span * 0.5f, 0f, 0f);

            Color c = PrismPalette.Spectral(t);
            _gaugeLeftMat.SetColor("_Tint", c);
            _gaugeRightMat.SetColor("_Tint", c);
            _gaugeTubeMat.SetColor("_Tint", c);

            _gaugePts[0] = _gaugeLeft.localPosition;
            _gaugePts[1] = _gaugeRight.localPosition;
            PrismMesh.Tube(_gaugePts, 0.0025f, 6, _gaugeTubeMesh);
        }

        void TickFieldReveal(float dt)
        {
            float want = Loop.Stage >= LoopStage.Discover ? 1f : 0f;
            if (Mathf.Approximately(_fieldReveal, want)) return;
            _fieldReveal = Mathf.MoveTowards(_fieldReveal, want, dt / 4f);
            DetectorScreen.SetFieldReveal(_fieldReveal);
        }

        // ---- Apply: match the target fringe spacing ---------------------------------------

        void BeginApplyChallenge()
        {
            _applyDone = false;
            _applyHoldTime = 0f;
            _targetSpacing = Random.Range(0.045f, 0.16f);
            BuildGhostTicks(_targetSpacing);
            if (_ghostView != null) _ghostView.gameObject.SetActive(true);
        }

        void BuildGhostTicks(float spacing)
        {
            _ghostVerts.Clear();
            _ghostTris.Clear();
            if (spacing > 0f && !float.IsInfinity(spacing))
            {
                float tickHalfHeight = TwoSlitSim.ScreenHalfHeight * 0.85f;
                const float tickHalfWidth = 0.0025f;

                int maxK = Mathf.Min((GhostMaxTicks - 1) / 2,
                                     Mathf.FloorToInt(TwoSlitSim.ScreenHalfWidth / spacing));
                for (int k = -maxK; k <= maxK; k++)
                {
                    float x = k * spacing;
                    int b = _ghostVerts.Count;
                    _ghostVerts.Add(new Vector3(x - tickHalfWidth, -tickHalfHeight, 0f));
                    _ghostVerts.Add(new Vector3(x + tickHalfWidth, -tickHalfHeight, 0f));
                    _ghostVerts.Add(new Vector3(x - tickHalfWidth, tickHalfHeight, 0f));
                    _ghostVerts.Add(new Vector3(x + tickHalfWidth, tickHalfHeight, 0f));
                    _ghostTris.Add(b); _ghostTris.Add(b + 1); _ghostTris.Add(b + 2);
                    _ghostTris.Add(b + 1); _ghostTris.Add(b + 3); _ghostTris.Add(b + 2);
                }
            }
            _ghostMesh.Clear();
            _ghostMesh.SetVertices(_ghostVerts);
            _ghostMesh.SetTriangles(_ghostTris, 0);
            _ghostMesh.RecalculateBounds();
        }

        void TickApplyChallenge(float dt)
        {
            if (_applyDone) return;
            float spacing = Sim.PredictedFringeSpacing();
            float err = float.IsInfinity(spacing) ? 1f : Mathf.Abs(spacing - _targetSpacing) / _targetSpacing;

            if (err < ApplyTolerance)
            {
                _applyHoldTime += dt;
                if (_applyHoldTime >= ApplyHoldNeeded)
                {
                    _applyDone = true;
                    Loop.Evidence.Record(QuantumEvidence.ChallengeDone);
                    Companion?.Voice?.Consonance(DetectorScreen.Root.position);
                    if (_ghostView != null) _ghostView.gameObject.SetActive(false);
                }
            }
            else _applyHoldTime = 0f;
        }

        // ---- Explain: predict, then run the count ------------------------------------------

        void BeginExplain()
        {
            DetectorScreen.Clear();
            _stableRunCount = 0;
            _explainPhase = ExplainPhase.Placing;
            _wasPinchingForMarker = false;
            _markerLocal = Vector3.zero;
            if (_predictionMarker != null)
            {
                _predictionMarker.gameObject.SetActive(true);
                _predictionMarker.localPosition = new Vector3(0f, 0f, TwoSlitSim.ScreenZ - 0.01f);
            }
        }

        void TickExplain()
        {
            if (Hands == null) return;

            switch (_explainPhase)
            {
                case ExplainPhase.Placing:
                {
                    if (TryScreenPoint(out Vector3 pt))
                    {
                        _markerLocal = pt;
                        _predictionMarker.localPosition = new Vector3(pt.x, pt.y, TwoSlitSim.ScreenZ - 0.01f);
                    }

                    bool pinchingNow = (Hands.Left != null && Hands.Left.IsTracked && Hands.Left.Pinch > 0.6f)
                                    || (Hands.Right != null && Hands.Right.IsTracked && Hands.Right.Pinch > 0.6f);
                    if (_wasPinchingForMarker && !pinchingNow)
                    {
                        _explainPhase = ExplainPhase.Bursting;
                        _burstRemaining = BurstTotal;
                    }
                    _wasPinchingForMarker = pinchingNow;
                    break;
                }

                case ExplainPhase.Bursting:
                {
                    int n = Mathf.Min(BurstPerFrame, _burstRemaining);
                    for (int i = 0; i < n; i++)
                    {
                        float x = Sim.SampleDetectionX();
                        float y = Random.Range(-TwoSlitSim.ScreenHalfHeight * 0.9f, TwoSlitSim.ScreenHalfHeight * 0.9f);
                        CommitDetection(x, y);
                    }
                    _burstRemaining -= n;
                    if (_burstRemaining <= 0) EvaluatePrediction();
                    break;
                }
            }
        }

        /// <summary>Where the learner is pointing on the screen plane, while pinching. Reach-based, since the screen sits beyond comfortable touch.</summary>
        bool TryScreenPoint(out Vector3 local)
        {
            local = default;
            PrismHands.Hand h = null;
            if (Hands.Left != null && Hands.Left.IsTracked && Hands.Left.ReachValid && Hands.Left.Pinch > 0.6f) h = Hands.Left;
            else if (Hands.Right != null && Hands.Right.IsTracked && Hands.Right.ReachValid && Hands.Right.Pinch > 0.6f) h = Hands.Right;
            if (h == null) return false;

            Vector3 originLocal = Anchor.InverseTransformPoint(h.Reach.origin);
            Vector3 dirLocal = Anchor.InverseTransformDirection(h.Reach.direction);
            if (Mathf.Abs(dirLocal.z) < 1e-5f) return false;

            float t = (TwoSlitSim.ScreenZ - originLocal.z) / dirLocal.z;
            if (t < 0f) return false;

            Vector3 hit = originLocal + dirLocal * t;
            local = new Vector3(
                Mathf.Clamp(hit.x, -TwoSlitSim.ScreenHalfWidth, TwoSlitSim.ScreenHalfWidth),
                Mathf.Clamp(hit.y, -TwoSlitSim.ScreenHalfHeight, TwoSlitSim.ScreenHalfHeight),
                0f);
            return true;
        }

        void EvaluatePrediction()
        {
            float atMarker = Sim.Intensity(_markerLocal.x);
            float peak = Sim.MaxIntensity();
            bool good = atMarker >= peak * 0.45f;
            Vector3 worldPos = Anchor.TransformPoint(new Vector3(_markerLocal.x, _markerLocal.y, TwoSlitSim.ScreenZ));

            if (good)
            {
                Loop.Evidence.Record(QuantumEvidence.PredictionGood);
                Companion?.Voice?.Consonance(worldPos);
                _explainPhase = ExplainPhase.Done;
            }
            else
            {
                // Wrong, not failed: recorded as a misconception and left open to try again,
                // exactly as the contract asks.
                Knowledge?.FlagMisconception(PrimaryConceptId, 0.15f);
                Companion?.Voice?.Tension(worldPos);
                DetectorScreen.Clear();
                _stableRunCount = 0;
                _explainPhase = ExplainPhase.Placing;
            }
        }
    }
}
