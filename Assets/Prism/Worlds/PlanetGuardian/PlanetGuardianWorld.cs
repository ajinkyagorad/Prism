using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using Prism.Worlds;
using UnityEngine;

namespace Prism.Worlds.PlanetGuardian
{
    /// <summary>Evidence keys. Named constants because the gates and the world must agree exactly.</summary>
    public static class PlanetGuardianEvidence
    {
        public const string LeverTouch          = "lever.touch";
        public const string LeverRelease        = "lever.release";
        public const string GhgTouch            = "lever.ghg.touch";
        public const string AlbedoTouch         = "lever.albedo.touch";
        public const string SunTouch            = "lever.sun.touch";
        public const string WarmExcursion       = "excursion.warm";
        public const string ColdExcursion       = "excursion.cold";
        public const string RunawayFelt         = "runaway.felt";
        public const string Rebalanced          = "rebalanced";
        public const string TargetHeld          = "apply.target-held";
        public const string PredictionGood      = "explain.prediction-good";
        public const string DisturbanceTriggered= "create.disturbance-triggered";
        public const string ConstructionStable  = "create.construction-stable";
    }

    /// <summary>
    /// The Planet Guardian world: a hand-held planet whose temperature is set by a real energy
    /// balance, complete with thermal inertia and a genuine ice-albedo tipping point.
    ///
    /// COLOUR LAW (the only place this world uses PrismPalette.Spectral): the atmosphere shell's
    /// hue encodes the CURRENT ENERGY IMBALANCE (absorbed minus outgoing), not temperature —
    /// cyan is losing energy, the ramp's warm end is gaining it, the middle is balance. This is
    /// the more interesting reading because imbalance is what predicts where the temperature is
    /// HEADED, which is invisible if only temperature itself is shown. Temperature is instead
    /// read honestly and separately, through the ice caps' latitude — a direct geometric
    /// consequence of the same freezing-point physics that drives the simulation, not a
    /// decoration. The in/out flow tubes carry fixed identity colours (gold = sunlight in,
    /// cyan = radiated heat out) with packet rate as magnitude, so the two flows the learner is
    /// meant to watch fail to match are never colour-ambiguous with each other or with the
    /// imbalance reading.
    ///
    ///   Wonder     A small planet turning, modest ice at its poles, nothing labelled. Three
    ///              unlabelled beads on unlabelled rails sit within reach. Touching one leaves Wonder.
    ///   Explore    Three controls — greenhouse gas, surface reflectivity, the sun itself — each
    ///              a bead dragged along a short rail. The planet answers, but late: oceans (a
    ///              compressed but real heat capacity) mean the temperature keeps drifting for
    ///              tens of seconds after a hand lets go. Push it far enough in either direction
    ///              and the ice line itself takes over, continuing the change alone.
    ///   Discover   Only once the learner has personally driven the planet both warm and cold
    ///              past the point where it kept moving without them: the energy budget becomes
    ///              literal, as two flows — sunlight in, heat out — the learner can watch fail
    ///              to match.
    ///   Formalize  A numeric readout (temperature, imbalance) and a thermometer rail appear.
    ///              Names arrive now, not before: albedo, forcing, equilibrium, feedback.
    ///   Apply      A target temperature is marked on the rail. Reaching it is easy; holding it
    ///              is not, because the planet is still moving when it looks like it has arrived.
    ///   Explain    Change something, then place a marker on the rail for where it will settle,
    ///              before it gets there. Wrong guesses are recorded, not punished.
    ///   Create     Design a planet — three lever settings — that survives a volcanic-style
    ///              shock and returns to where it was, rather than tipping into the other branch.
    ///   Connect    Return to the atrium; the constellation has changed.
    /// </summary>
    public class PlanetGuardianWorld : PrismWorldBase
    {
        [Header("Layout")]
        public float PlanetRadius = 0.055f;

        public PlanetGuardianSim Sim { get; private set; }
        public bool IsSettled { get; private set; }

        const float GrabRadius = 0.05f;
        const float IceCapUnitRadius = 1.02f;          // slightly > 1 so the shell reads on top
        const float ImbalanceColourScale = 60f;        // W/m^2 that saturates the imbalance ramp
        const float SettleDTdtEpsilon = 0.02f;         // K/s
        const float SettleHoldSeconds = 3f;

        // Verified against the actual stepper (see NOTES.md): a single lever pushed to its
        // extreme and released reliably crosses both thresholds within a minute of idle drift,
        // with comfortable margin — these are not tuned to the knife edge of what is reachable.
        const float WarmExcursionT = 291f;
        const float ColdExcursionT = 256f;
        const float RunawayDeltaT = 2.5f;
        const float RunawayIdleDelay = 1f;
        const float RunawayWatchWindow = 4f;
        const float RebalanceImbalance = 2f;
        const float RebalanceLeaveImbalance = 8f;
        const float RebalanceMinDeltaFromDefault = 3f;

        // ---- geometry ----
        Transform _planet;
        Material _planetMat;
        Transform _iceNorth, _iceSouth;
        Material _iceMat;
        Transform _atmosphere;
        Material _atmosphereMat;
        Transform _haze;
        Material _hazeMat;
        Transform _sun;
        Material _sunMat;

        Transform _inFlowView, _outFlowView;
        Material _inFlowMat, _outFlowMat;

        Transform _thermRail, _thermCurrent, _thermTarget, _thermPredict;
        Material _thermCurrentMat, _thermTargetMat, _thermPredictMat;
        Vector3 _thermBase, _thermTop;
        const float ThermTMin = 150f;
        const float ThermTMax = 340f;

        Transform _meteor;
        Material _meteorMat;
        bool _meteorPendingTrigger;

        PrismLabel _readout;
        float _lastShownT = float.NaN;
        float _lastShownImbalance = float.NaN;

        PlanetGuardianChallenge _challenge;

        /// <summary>A hand-draggable bead on a short rail: the whole interface for one control.</summary>
        class Lever
        {
            public Transform Bead;
            public Vector3 LocalBase, LocalTop;
            public float Value01 = 0.5f;
            public PrismHands.Hand Dragging;
            public string TouchKey;
        }
        Lever _leverGhg, _leverAlbedo, _leverSun;

        // ---- runaway / rebalance bookkeeping ----
        bool _wasAboveWarm, _wasBelowCold;
        float _idleTimer;
        bool _runawaySnapshotTaken;
        float _runawayBaselineT;
        bool _runawayWatching;
        bool _runawayFiredThisEpisode;
        bool _rebalancedArmed = true;
        float _settledTimer;

        // ---- predict-bead drag (Explain) ----
        PrismHands.Hand _predictDragHand;
        bool _predictPlacedPending;
        float _predictPendingValue;

        // -----------------------------------------------------------------
        // identity
        // -----------------------------------------------------------------

        public override string WorldId => "planet-guardian";

        /// <summary>Just outside the air, where an energy balance is something you can see.</summary>
        protected override WorldShellSpec Shell => WorldShellSpec.Stratosphere;
        public override string PrimaryConceptId => "planet-guardian";
        public override string DisplayName => "Planet Guardian";

        public Vector3 PlanetPosition => _planet != null ? _planet.position : Anchor.position;

        protected override void Awake()
        {
            base.Awake();
            // A globe held close, not a table seen from a distance — the three controls need to
            // be reachable with either hand without the learner stepping forward.
            Reach = 0.42f;
            EyeToTable = 0.36f;
        }

        // -----------------------------------------------------------------
        // concepts
        // -----------------------------------------------------------------

        public override IEnumerable<ConceptSpec> Concepts
        {
            get
            {
                yield return new ConceptSpec
                {
                    Id = "planet-guardian",
                    Title = "Planet Guardian",
                    Domain = ConceptDomain.EarthAndCiv,
                    Kind = KnowledgeKind.System,
                    Direction = WedgeDir(162f, 0.05f),
                    Distance = 3.6f,
                    WorldId = "planet-guardian",
                    Capability = "Hold a planet's temperature where you want it, against its own inertia.",
                    Formalisation = "A planet's temperature is set by a balance: energy absorbed " +
                        "from its star against energy radiated to space. The balance has memory, " +
                        "and near certain thresholds it stops being reversible.",
                    Links = new (string, Relation, float)[]
                    {
                        ("energy-balance",  Relation.Composes,   0.90f),
                        ("albedo",          Relation.Composes,   0.80f),
                        ("tipping-points",  Relation.Composes,   0.80f),
                        ("equilibrium",     Relation.Constrains, 0.75f),
                        ("feedback",        Relation.Analogy,    0.90f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "energy-balance",
                    Title = "Energy Balance",
                    Domain = ConceptDomain.MatterAndEnergy,
                    Kind = KnowledgeKind.Equation,
                    Direction = WedgeDir(148f, 0.35f),
                    Distance = 4.3f,
                    Capability = "Read a system's fate from two flows rather than one snapshot.",
                    Formalisation = "C dT/dt = absorbed - outgoing. Absorbed is (1-albedo)*S/4; " +
                        "outgoing is epsilon*sigma*T^4. A planet rests only where the two are equal.",
                    Links = new (string, Relation, float)[]
                    {
                        ("energy-conservation", Relation.Instantiates, 0.85f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "albedo",
                    Title = "Albedo",
                    Domain = ConceptDomain.EarthAndCiv,
                    Kind = KnowledgeKind.Fact,
                    Direction = WedgeDir(176f, -0.30f),
                    Distance = 4.0f,
                    Capability = "Know how much of what arrives is simply thrown back.",
                    Formalisation = "The fraction of incoming light a surface reflects rather than " +
                        "absorbs. Ice and fresh snow reflect most of it; open ocean absorbs most of it.",
                    Links = System.Array.Empty<(string, Relation, float)>()
                };

                yield return new ConceptSpec
                {
                    Id = "equilibrium",
                    Title = "Equilibrium",
                    Domain = ConceptDomain.EarthAndCiv,
                    Kind = KnowledgeKind.Theory,
                    Direction = WedgeDir(155f, -0.10f),
                    Distance = 5.0f,
                    Capability = "Recognise a state that holds itself in place, and tell it apart " +
                        "from one that only looks like it does.",
                    Formalisation = "A rest point where inputs equal outputs. Not every equilibrium " +
                        "is stable, and a system can sit near an unstable one for a long time before " +
                        "revealing which kind it was.",
                    Links = System.Array.Empty<(string, Relation, float)>()
                };

                yield return new ConceptSpec
                {
                    Id = "tipping-points",
                    Title = "Tipping Points",
                    Domain = ConceptDomain.EarthAndCiv,
                    Kind = KnowledgeKind.Theory,
                    Direction = WedgeDir(170f, 0.45f),
                    Distance = 4.7f,
                    Capability = "Tell how much margin a system has left before a change stops being reversible.",
                    Formalisation = "Where a feedback loop's own slope overtakes a system's tendency " +
                        "to settle, a small push produces a disproportionate, self-sustaining change.",
                    Links = System.Array.Empty<(string, Relation, float)>()
                };
            }
        }

        /// <summary>
        /// Matches PrismEnvironment's sun-direction convention exactly: azimuth in degrees,
        /// 0 = forward (+Z), sweeping toward +X as it increases; elevation in radians, angle
        /// above the horizontal plane.
        /// </summary>
        static Vector3 WedgeDir(float azimuthDeg, float elevationRad)
        {
            float az = azimuthDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(elevationRad) * Mathf.Sin(az),
                                Mathf.Sin(elevationRad),
                                Mathf.Cos(elevationRad) * Mathf.Cos(az));
        }

        // -----------------------------------------------------------------
        // construction
        // -----------------------------------------------------------------

        protected override void BuildWorld()
        {
            Sim = new PlanetGuardianSim();

            BuildPlanet();
            BuildIceCaps();
            BuildAtmosphere();
            BuildHaze();
            BuildSun();
            BuildFlows();
            BuildLevers();
            BuildThermometer();
            BuildMeteor();

            _readout = PrismLabel.Create("ClimateReadout", Anchor, Head, 0.015f);

            _challenge = gameObject.AddComponent<PlanetGuardianChallenge>();
            _challenge.World = this;
        }

        void BuildPlanet()
        {
            var tint = Color.Lerp(PrismPalette.Mint, PrismPalette.Coral, 0.30f);
            _planetMat = PrismMaterials.CeramicBody(tint, 0.28f);
            _planet = Body(PrismMesh.Icosphere(3), PlanetRadius, _planetMat, "Planet");
        }

        void BuildIceCaps()
        {
            var tint = Color.Lerp(PrismPalette.Warm, PrismPalette.Cyan, 0.18f);
            _iceMat = PrismMaterials.CeramicBody(tint, 0.55f);
            var mesh = PrismMesh.Icosphere(2);

            _iceNorth = MakeIceCap(mesh, "IceNorth");
            _iceSouth = MakeIceCap(mesh, "IceSouth");
        }

        Transform MakeIceCap(Mesh mesh, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_planet, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _iceMat;
            go.transform.localScale = Vector3.one * IceCapUnitRadius;
            return go.transform;
        }

        void BuildAtmosphere()
        {
            _atmosphereMat = PrismMaterials.New(PrismMaterials.Gel);
            _atmosphereMat.SetColor("_DeepTint", PrismPalette.Violet);
            _atmosphereMat.SetFloat("_Density", 0.9f);
            _atmosphereMat.SetFloat("_NoiseFreq", 5f);
            _atmosphereMat.SetFloat("_FlowSpeed", 0.12f);
            _atmosphereMat.SetVector("_FlowAxis", new Vector4(0f, 1f, 0f, 0f));
            _atmosphere = Body(PrismMesh.Icosphere(3), PlanetRadius * 1.22f, _atmosphereMat, "Atmosphere");
        }

        void BuildHaze()
        {
            _hazeMat = PrismMaterials.New(PrismMaterials.Volumetric);
            _hazeMat.SetColor("_Tint", PrismPalette.Coral);
            _hazeMat.SetColor("_EdgeTint", PrismPalette.Gold);
            _hazeMat.SetFloat("_NoiseFreq", 2.6f);
            _hazeMat.SetFloat("_Churn", 0.08f);
            _hazeMat.SetFloat("_Softness", 1.1f);
            _haze = Body(PrismMesh.Icosphere(2), PlanetRadius * 1.42f, _hazeMat, "GhgHaze");
        }

        void BuildSun()
        {
            _sunMat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.9f);
            _sun = Body(PrismMesh.Icosphere(2), 0.018f, _sunMat, "Sun");
            _sun.localPosition = new Vector3(0.20f, 0.08f, -0.06f);
        }

        void BuildFlows()
        {
            var inGo = new GameObject("InFlow");
            inGo.transform.SetParent(Anchor, false);
            var inMesh = new Mesh { name = "InFlowMesh" };
            var inPts = new List<Vector3> { _sun.localPosition, Vector3.zero };
            PrismMesh.Tube(inPts, 0.006f, 6, inMesh);
            inGo.AddComponent<MeshFilter>().sharedMesh = inMesh;
            _inFlowMat = PrismMaterials.New(PrismMaterials.Flow);
            _inFlowMat.SetColor("_Tint", PrismPalette.Gold);
            _inFlowMat.SetFloat("_CoreGain", 0.5f);
            _inFlowMat.SetFloat("_Strength", 0.85f);
            inGo.AddComponent<MeshRenderer>().sharedMaterial = _inFlowMat;
            _inFlowView = inGo.transform;
            _inFlowView.gameObject.SetActive(false);

            var outGo = new GameObject("OutFlow");
            outGo.transform.SetParent(Anchor, false);
            var outMesh = new Mesh { name = "OutFlowMesh" };
            var outPts = new List<Vector3> { Vector3.zero, new Vector3(0f, PlanetRadius * 4.2f, -0.03f) };
            PrismMesh.Tube(outPts, 0.006f, 6, outMesh);
            outGo.AddComponent<MeshFilter>().sharedMesh = outMesh;
            _outFlowMat = PrismMaterials.New(PrismMaterials.Flow);
            _outFlowMat.SetColor("_Tint", PrismPalette.Cyan);
            _outFlowMat.SetFloat("_CoreGain", 0.5f);
            _outFlowMat.SetFloat("_Strength", 0.85f);
            outGo.AddComponent<MeshRenderer>().sharedMaterial = _outFlowMat;
            _outFlowView = outGo.transform;
            _outFlowView.gameObject.SetActive(false);
        }

        void BuildLevers()
        {
            _leverGhg = MakeLever("LeverGhg", -34f, PrismPalette.Coral, PlanetGuardianEvidence.GhgTouch);
            _leverAlbedo = MakeLever("LeverAlbedo", 0f, PrismPalette.Mint, PlanetGuardianEvidence.AlbedoTouch);
            _leverSun = MakeLever("LeverSun", 34f, PrismPalette.Gold, PlanetGuardianEvidence.SunTouch);

            _leverGhg.Value01 = Mathf.InverseLerp(PlanetGuardianSim.GhgPpmMin, PlanetGuardianSim.GhgPpmMax,
                                                  PlanetGuardianSim.Co2Ref);
            _leverAlbedo.Value01 = Mathf.InverseLerp(PlanetGuardianSim.AlbedoManualMin, PlanetGuardianSim.AlbedoManualMax,
                                                     PlanetGuardianSim.AlbedoDefault);
            _leverSun.Value01 = Mathf.InverseLerp(PlanetGuardianSim.SunMultMin, PlanetGuardianSim.SunMultMax, 1f);
        }

        Lever MakeLever(string name, float fanDeg, Color tint, string touchKey)
        {
            float rad = fanDeg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, -Mathf.Cos(rad));
            Vector3 basePos = dir * 0.115f + new Vector3(0f, -0.045f, 0f);
            Vector3 topPos = basePos + Vector3.up * 0.075f;

            var railGo = new GameObject(name + "Rail");
            railGo.transform.SetParent(Anchor, false);
            var railMesh = new Mesh { name = name + "RailMesh" };
            var railPts = new List<Vector3> { basePos, topPos };
            PrismMesh.Tube(railPts, 0.0028f, 6, railMesh);
            railGo.AddComponent<MeshFilter>().sharedMesh = railMesh;
            railGo.AddComponent<MeshRenderer>().sharedMaterial = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.10f);

            var beadMat = PrismMaterials.CeramicBody(tint, 0.6f);
            var bead = Body(PrismMesh.Icosphere(2), 0.014f, beadMat, name + "Bead");
            bead.localPosition = basePos;

            return new Lever { Bead = bead, LocalBase = basePos, LocalTop = topPos, TouchKey = touchKey };
        }

        void BuildThermometer()
        {
            _thermBase = new Vector3(-0.19f, -0.03f, -0.02f);
            _thermTop = _thermBase + Vector3.up * 0.16f;

            var railGo = new GameObject("ThermometerRail");
            railGo.transform.SetParent(Anchor, false);
            var railMesh = new Mesh { name = "ThermRailMesh" };
            var pts = new List<Vector3> { _thermBase, _thermTop };
            PrismMesh.Tube(pts, 0.0026f, 6, railMesh);
            railGo.AddComponent<MeshFilter>().sharedMesh = railMesh;
            railGo.AddComponent<MeshRenderer>().sharedMaterial = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.12f);
            _thermRail = railGo.transform;
            _thermRail.gameObject.SetActive(false);

            _thermCurrentMat = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.7f);
            _thermCurrent = Body(PrismMesh.Icosphere(2), 0.011f, _thermCurrentMat, "ThermCurrent");
            _thermCurrent.gameObject.SetActive(false);

            _thermTargetMat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.65f);
            _thermTarget = Body(PrismMesh.Icosphere(2), 0.013f, _thermTargetMat, "ThermTarget");
            _thermTarget.gameObject.SetActive(false);

            _thermPredictMat = PrismMaterials.CeramicBody(PrismPalette.Violet, 0.65f);
            _thermPredict = Body(PrismMesh.Icosphere(2), 0.013f, _thermPredictMat, "ThermPredict");
            _thermPredict.gameObject.SetActive(false);
        }

        void BuildMeteor()
        {
            _meteorMat = PrismMaterials.New(PrismMaterials.Seed);
            _meteorMat.SetColor("_Tint", PrismPalette.Violet);
            _meteorMat.SetFloat("_Growth", 1f);
            _meteorMat.SetFloat("_Density", 1.2f);
            _meteor = Body(PrismMesh.Icosphere(2), 0.016f, _meteorMat, "Disturbance");
            _meteor.localPosition = new Vector3(0f, 0.05f, -0.16f);
            _meteor.gameObject.SetActive(false);
        }

        // -----------------------------------------------------------------
        // the loop
        // -----------------------------------------------------------------

        protected override void ConfigureLoop(LearningLoop loop)
        {
            loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet touched anything",
                e => e.Count(PlanetGuardianEvidence.LeverTouch) >= 1));

            // The gate that matters most: BOTH a warm and a cold excursion, AND at least one
            // episode of the planet visibly continuing to change after every hand let go. A
            // learner who has personally felt the ice line take over is ready to be shown why.
            loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet driven the planet both warmer and colder, and felt it keep moving without them",
                e => e.Has(PlanetGuardianEvidence.WarmExcursion)
                  && e.Has(PlanetGuardianEvidence.ColdExcursion)
                  && e.Has(PlanetGuardianEvidence.RunawayFelt)
                  && e.Count(PlanetGuardianEvidence.LeverRelease) >= 4));

            loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet deliberately found a new, different balance point",
                e => e.Has(PlanetGuardianEvidence.Rebalanced)));

            loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet explored all three controls with the numbers visible",
                e => e.Has(PlanetGuardianEvidence.GhgTouch)
                  && e.Has(PlanetGuardianEvidence.AlbedoTouch)
                  && e.Has(PlanetGuardianEvidence.SunTouch)
                  && e.Count(PlanetGuardianEvidence.LeverRelease) >= 10));

            loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet held the planet on target against its own drift",
                e => e.Has(PlanetGuardianEvidence.TargetHeld)));

            loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet predicted where the temperature would settle",
                e => e.Has(PlanetGuardianEvidence.PredictionGood)));

            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet built a planet that survives a shock",
                e => e.Has(PlanetGuardianEvidence.ConstructionStable)));
        }

        protected override void OnStageEntered(LoopStage stage)
        {
            base.OnStageEntered(stage);   // keeps the companion notified, same as every world

            switch (stage)
            {
                case LoopStage.Discover:
                    // The energy budget becomes literal: two flows the learner can watch fail
                    // to match, rather than a single number that merely drifts.
                    _inFlowView.gameObject.SetActive(true);
                    _outFlowView.gameObject.SetActive(true);
                    break;

                case LoopStage.Formalize:
                    _thermRail.gameObject.SetActive(true);
                    _thermCurrent.gameObject.SetActive(true);
                    break;

                case LoopStage.Apply:
                    _challenge.BeginTargetChallenge();
                    break;

                case LoopStage.Explain:
                    _challenge.BeginPrediction();
                    break;

                case LoopStage.Create:
                    _meteor.gameObject.SetActive(true);
                    break;

                case LoopStage.Connect:
                    Knowledge?.Confirm(PrimaryConceptId, 0.4f);
                    break;
            }
        }

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        protected override void Tick(float dt)
        {
            UpdateLevers();
            UpdateMeteor();
            UpdateThermPredictDrag();

            _challenge?.Evaluate(dt);

            Sim.GhgPpm = Mathf.Lerp(PlanetGuardianSim.GhgPpmMin, PlanetGuardianSim.GhgPpmMax, _leverGhg.Value01);
            Sim.AlbedoManual = Mathf.Lerp(PlanetGuardianSim.AlbedoManualMin, PlanetGuardianSim.AlbedoManualMax,
                                          _leverAlbedo.Value01);
            Sim.SunMultiplier = Mathf.Lerp(PlanetGuardianSim.SunMultMin, PlanetGuardianSim.SunMultMax,
                                           _leverSun.Value01);
            Sim.DisturbanceForcing = _challenge != null ? _challenge.DisturbanceForcing : 0f;
            Sim.Advance(dt);

            UpdateSettle(dt);
            SyncPlanetVisuals(dt);
            SyncFlows();
            SyncSun();
            SyncThermometerCurrent();
            SyncReadout();

            UpdateExcursionEvidence();
            UpdateRunawayEvidence(dt);
            UpdateRebalanceEvidence();
        }

        // ---- levers -------------------------------------------------------

        void UpdateLevers()
        {
            UpdateLever(_leverGhg);
            UpdateLever(_leverAlbedo);
            UpdateLever(_leverSun);
        }

        void UpdateLever(Lever lever)
        {
            if (Hands == null)
            {
                lever.Bead.localPosition = Vector3.Lerp(lever.LocalBase, lever.LocalTop, lever.Value01);
                return;
            }

            if (lever.Dragging == null)
            {
                TryStartDrag(lever, Hands.Left);
                if (lever.Dragging == null) TryStartDrag(lever, Hands.Right);
            }
            else
            {
                var h = lever.Dragging;
                if (!h.IsTracked || !(h.Pinch > 0.5f || h.Grip > 0.5f))
                {
                    EndDrag(lever);
                }
                else
                {
                    Vector3 local = Anchor.InverseTransformPoint(PrismHands.PointOf(h));
                    lever.Value01 = ProjectOntoRail(local, lever.LocalBase, lever.LocalTop, lever.Value01);
                }
            }

            lever.Bead.localPosition = Vector3.Lerp(lever.LocalBase, lever.LocalTop, lever.Value01);
        }

        static float ProjectOntoRail(Vector3 local, Vector3 railBase, Vector3 railTop, float fallback)
        {
            Vector3 axis = railTop - railBase;
            float len2 = axis.sqrMagnitude;
            if (len2 < 1e-8f) return fallback;
            float t = Vector3.Dot(local - railBase, axis) / len2;
            return Mathf.Clamp01(t);
        }

        /// <summary>True if this hand is already dragging a different lever this frame — the
        /// three beads sit close enough together that a hand near the boundary between two
        /// could otherwise satisfy both grab checks and drag them in lockstep.</summary>
        bool IsHandBusyOnAnotherLever(PrismHands.Hand h, Lever except)
        {
            if (h == null) return false;
            if (_leverGhg != except && _leverGhg.Dragging == h) return true;
            if (_leverAlbedo != except && _leverAlbedo.Dragging == h) return true;
            if (_leverSun != except && _leverSun.Dragging == h) return true;
            return false;
        }

        void TryStartDrag(Lever lever, PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked) return;
            if (!(h.Pinch > 0.55f || h.Grip > 0.55f)) return;
            if (IsHandBusyOnAnotherLever(h, lever)) return;

            Vector3 point = PrismHands.PointOf(h);
            if ((point - lever.Bead.position).sqrMagnitude > GrabRadius * GrabRadius) return;

            lever.Dragging = h;
            Loop.Evidence.Record(PlanetGuardianEvidence.LeverTouch);
            Loop.Evidence.Record(lever.TouchKey);
            Hands.Buzz(h, 0.22f, 0.04f);
        }

        void EndDrag(Lever lever)
        {
            var h = lever.Dragging;
            lever.Dragging = null;
            Loop.Evidence.Record(PlanetGuardianEvidence.LeverRelease);
            if (h != null) Hands.Clunk(h, 0.3f);
        }

        // ---- meteor (Create) -----------------------------------------------

        void UpdateMeteor()
        {
            if (!_meteor.gameObject.activeSelf) return;

            float bob = Mathf.Sin(Time.time * 1.3f) * 0.006f;
            _meteor.localPosition = new Vector3(0f, 0.05f + bob, -0.16f);

            var h = NearestHand(_meteor.position, GrabRadius);
            if (h != null && h.PinchDown)
            {
                _meteorPendingTrigger = true;
                if (Hands != null) Hands.Buzz(h, 0.4f, 0.08f);
            }
        }

        public bool TryConsumeMeteorTrigger()
        {
            if (!_meteorPendingTrigger) return false;
            _meteorPendingTrigger = false;
            return true;
        }

        // ---- thermometer (Formalize onward) --------------------------------

        Vector3 ThermPoint(float t)
        {
            float u = Mathf.InverseLerp(ThermTMin, ThermTMax, t);
            return Vector3.Lerp(_thermBase, _thermTop, Mathf.Clamp01(u));
        }

        float ThermValueFromLocal(Vector3 local)
        {
            float u = ProjectOntoRail(local, _thermBase, _thermTop, 0.5f);
            return Mathf.Lerp(ThermTMin, ThermTMax, u);
        }

        void SyncThermometerCurrent()
        {
            if (!_thermCurrent.gameObject.activeSelf) return;
            _thermCurrent.localPosition = ThermPoint(Sim.Temperature);
        }

        void UpdateThermPredictDrag()
        {
            if (!_thermPredict.gameObject.activeSelf || Hands == null) return;

            if (_predictDragHand == null)
            {
                var h = NearestHand(_thermPredict.position, GrabRadius);
                if (h != null && (h.Pinch > 0.55f || h.Grip > 0.55f))
                    _predictDragHand = h;
            }
            else
            {
                var h = _predictDragHand;
                if (!h.IsTracked || !(h.Pinch > 0.5f || h.Grip > 0.5f))
                {
                    _predictDragHand = null;
                    _predictPlacedPending = true;
                    _predictPendingValue = ThermValueFromLocal(Anchor.InverseTransformPoint(_thermPredict.position));
                    Hands.Clunk(h, 0.25f);
                }
                else
                {
                    Vector3 local = Anchor.InverseTransformPoint(PrismHands.PointOf(h));
                    float t = ThermValueFromLocal(local);
                    _thermPredict.localPosition = ThermPoint(t);
                }
            }
        }

        public bool TryConsumePredictDrag(out float predictedT)
        {
            if (_predictPlacedPending)
            {
                predictedT = _predictPendingValue;
                _predictPlacedPending = false;
                return true;
            }
            predictedT = 0f;
            return false;
        }

        public void ShowThermTarget(float targetT)
        {
            _thermTarget.gameObject.SetActive(true);
            _thermTarget.localPosition = ThermPoint(targetT);
        }

        public void HideThermTarget() => _thermTarget.gameObject.SetActive(false);

        public void ShowThermPredict(bool on)
        {
            _thermPredict.gameObject.SetActive(on);
            if (on) _thermPredict.localPosition = ThermPoint(Sim.Temperature);
        }

        // ---- visual sync -----------------------------------------------------

        void SyncPlanetVisuals(float dt)
        {
            _planet.Rotate(Vector3.up, 6f * dt, Space.Self);

            float ice = Sim.IceFraction;
            float offset = Mathf.Lerp(1f + IceCapUnitRadius, 0f, ice);
            _iceNorth.localPosition = Vector3.up * offset;
            _iceSouth.localPosition = Vector3.down * offset;

            float imbalance = Sim.Imbalance;
            float u = 0.5f + 0.5f * Mathf.Clamp(imbalance / ImbalanceColourScale, -1f, 1f);
            _atmosphereMat.SetColor("_Tint", PrismPalette.Spectral(u));
            float mag = Mathf.Clamp01(Mathf.Abs(imbalance) / ImbalanceColourScale);
            _atmosphereMat.SetFloat("_Density", Mathf.Lerp(0.6f, 1.7f, mag));

            _hazeMat.SetFloat("_Density", Mathf.Lerp(0.15f, 1.9f, _leverGhg.Value01));
            _planetMat.SetFloat("_Luminance", Mathf.Lerp(0.14f, 0.50f, _leverAlbedo.Value01));
        }

        void SyncFlows()
        {
            if (!_inFlowView.gameObject.activeSelf) return;

            float inRate = Mathf.InverseLerp(60f, 420f, Sim.Absorbed);
            _inFlowMat.SetFloat("_Speed", Mathf.Lerp(0.3f, 2.2f, inRate));
            _inFlowMat.SetFloat("_Packets", Mathf.Lerp(2f, 9f, inRate));

            float outRate = Mathf.InverseLerp(60f, 420f, Sim.Outgoing);
            _outFlowMat.SetFloat("_Speed", Mathf.Lerp(0.3f, 2.2f, outRate));
            _outFlowMat.SetFloat("_Packets", Mathf.Lerp(2f, 9f, outRate));
        }

        void SyncSun()
        {
            float s01 = _leverSun.Value01;
            _sunMat.SetFloat("_Luminance", Mathf.Lerp(0.5f, 1.0f, s01));
            _sun.localScale = Vector3.one * Mathf.Lerp(0.014f, 0.024f, s01);
        }

        void SyncReadout()
        {
            if (_readout == null) return;
            if (Loop.Stage < LoopStage.Formalize) { _readout.Show(false); return; }

            float t = Sim.Temperature;
            float imb = Sim.Imbalance;
            float roundT = Mathf.Round(t * 2f) * 0.5f;
            float roundImb = Mathf.Round(imb * 2f) * 0.5f;

            if (!Mathf.Approximately(roundT, _lastShownT) || !Mathf.Approximately(roundImb, _lastShownImbalance))
            {
                _lastShownT = roundT;
                _lastShownImbalance = roundImb;
                string sign = roundImb >= 0f ? "+" : "";
                string body = roundT.ToString("0.0") + " K\n" + sign + roundImb.ToString("0.0") + " W/m^2";
                float u = 0.5f + 0.5f * Mathf.Clamp(imb / ImbalanceColourScale, -1f, 1f);
                _readout.SetText(body, PrismPalette.Spectral(u));
            }

            _readout.PlaceAbove(_planet.position, PlanetRadius * 1.6f);
            _readout.Show(true);
        }

        void UpdateSettle(float dt)
        {
            if (Mathf.Abs(Sim.DTdt) < SettleDTdtEpsilon) _settledTimer += dt;
            else _settledTimer = 0f;
            IsSettled = _settledTimer >= SettleHoldSeconds;
        }

        // ---- evidence from the physics itself ---------------------------------

        void UpdateExcursionEvidence()
        {
            float t = Sim.Temperature;
            bool aboveWarm = t >= WarmExcursionT;
            bool belowCold = t <= ColdExcursionT;
            if (aboveWarm && !_wasAboveWarm) Loop.Evidence.Record(PlanetGuardianEvidence.WarmExcursion);
            if (belowCold && !_wasBelowCold) Loop.Evidence.Record(PlanetGuardianEvidence.ColdExcursion);
            _wasAboveWarm = aboveWarm;
            _wasBelowCold = belowCold;
        }

        /// <summary>
        /// Watches for the signature of a genuine tipping point: the planet still changing by a
        /// meaningful amount well after every hand has let go of every control. This is measured
        /// directly from the running simulation, not asserted — a learner who only ever nudges
        /// the system gently will simply never trigger it.
        /// </summary>
        void UpdateRunawayEvidence(float dt)
        {
            bool anyDragging = _leverGhg.Dragging != null || _leverAlbedo.Dragging != null
                             || _leverSun.Dragging != null;

            if (anyDragging)
            {
                _idleTimer = 0f;
                _runawaySnapshotTaken = false;
                _runawayWatching = false;
                _runawayFiredThisEpisode = false;
                return;
            }

            _idleTimer += dt;

            if (!_runawaySnapshotTaken && _idleTimer >= RunawayIdleDelay)
            {
                _runawaySnapshotTaken = true;
                _runawayBaselineT = Sim.Temperature;
                _runawayWatching = true;
            }
            else if (_runawayWatching && !_runawayFiredThisEpisode
                     && _idleTimer >= RunawayIdleDelay + RunawayWatchWindow)
            {
                if (Mathf.Abs(Sim.Temperature - _runawayBaselineT) > RunawayDeltaT)
                {
                    Loop.Evidence.Record(PlanetGuardianEvidence.RunawayFelt);
                    _runawayFiredThisEpisode = true;
                }
            }
        }

        void UpdateRebalanceEvidence()
        {
            float imbalance = Sim.Imbalance;
            float dFromDefault = Mathf.Abs(Sim.Temperature - PlanetGuardianSim.TRef);

            if (Mathf.Abs(imbalance) > RebalanceLeaveImbalance) _rebalancedArmed = true;

            if (_rebalancedArmed && Mathf.Abs(imbalance) < RebalanceImbalance
                && dFromDefault > RebalanceMinDeltaFromDefault)
            {
                Loop.Evidence.Record(PlanetGuardianEvidence.Rebalanced);
                _rebalancedArmed = false;
                Companion?.Voice?.Consonance(PlanetPosition, 0.5f);
            }
        }
    }
}
