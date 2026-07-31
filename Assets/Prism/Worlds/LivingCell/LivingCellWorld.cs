using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.LivingCell
{
    /// <summary>Evidence keys. Named constants because the gates and the world must agree exactly.</summary>
    public static class LivingCellEvidence
    {
        public const string Interacted    = "interacted";
        public const string Blocked       = "blocked";
        public const string Fed           = "fed";
        public const string Pinched       = "pinched";
        public const string Shortage      = "shortage";
        public const string Surplus       = "surplus";
        public const string Recovered     = "recovered";
        public const string SpikeSurvived = "spike.survived";
        public const string PredictionGood = "prediction.correct";
        public const string CycleSurvived  = "cycle.survived";
    }

    /// <summary>
    /// The Living Cell world: a cell as a chemical economy under constant demand, not a diagram.
    ///
    ///   Wonder     A translucent cell, quietly working. A channel glows faintly on its surface; a
    ///              few glucose motes drift nearby. Nothing is labelled and nothing explains itself.
    ///   Explore    Block the channel with a hand and the glow dims; the cytoplasm cools toward cyan.
    ///              Carry a glucose mote through the membrane and it warms toward gold. Pinch the
    ///              membrane itself and everything leaks for a few seconds. The learner is free to
    ///              do all three for as long as they like.
    ///   Discover   Once they have personally driven the cell's ATP charge to BOTH a shortage and a
    ///              surplus — the same straddle-the-boundary gate as the orbital world's bound and
    ///              unbound paths — the invisible becomes visible: five beads along the membrane
    ///              light up as the real oxygen gradient, twelve tokens appear as a countable ATP
    ///              currency, and three currents start flowing to show flux in motion.
    ///   Formalize  Only now: four labels, spatially, beside the thing each one names.
    ///   Apply      A demand spike arrives. Keep the charge above 10% using the controls already learned.
    ///   Explain    Place a marker predicting the charge after a specific, fully-blocked interval —
    ///              then watch.
    ///   Create     A tuning dial appears: the learner's own catabolism rate. Survive one full,
    ///              unattended demand cycle with it.
    ///   Connect    Return to the atrium; the constellation has changed.
    ///
    /// -----------------------------------------------------------------------------------------
    /// COLOUR LAW (two, both stated, neither decorative)
    /// -----------------------------------------------------------------------------------------
    ///   LAW 1 — CONCENTRATION -> SPECTRAL RAMP. Applied only to genuine continuous fields: the
    ///   cytoplasm's tint is PrismPalette.Spectral(ATP fraction), and each of the five oxygen beads
    ///   is PrismPalette.Spectral(local concentration / reference). A starving region and a
    ///   saturated one are visibly different colours before any number appears — exactly the
    ///   candidate encoding named in the brief.
    ///
    ///   LAW 2 — FIXED IDENTITY for discrete, non-field objects, where mapping a single small bead
    ///   to "the current concentration" would be unreadable noise: glucose (motes, the channel port,
    ///   the glucose flux tube, and ATP tokens when charged) is always Gold — the same hue for
    ///   "usable chemical energy" wherever it appears, including the ATP it becomes. Spent energy
    ///   (ADP, the empty side of a token) is Violet. Oxygen's flux tube is Cyan. Only brightness,
    ///   scale and animation state change on these objects — never their hue.
    /// </summary>
    public class LivingCellWorld : PrismWorldBase
    {
        public const string ConceptId = "living-cell";

        public override string WorldId => ConceptId;

        /// <summary>You are not looking at a cell, you are inside one: crowded, warm and wet.</summary>
        protected override WorldShellSpec Shell => WorldShellSpec.Cytoplasm;
        public override string PrimaryConceptId => ConceptId;

        public CellSim Sim { get; private set; }

        /// <summary>Exposes the protected Anchor to CellChallenge, which builds its own small geometry.</summary>
        public Transform WorldAnchor => Anchor;

        /// <summary>True while a scripted (not learner-caused) block is in effect — see CellChallenge.</summary>
        public bool ChannelForcedClosed;

        public const int AtpTokenCount = 12;
        public int AtpLitCount { get; private set; }

        // -----------------------------------------------------------------
        // geometry constants
        // -----------------------------------------------------------------
        const float MembraneRadius = 0.10f;
        const float CytoplasmRadius = 0.055f;
        const int OxygenBeadCount = CellSim.ShellCount;
        const float OxygenReference = 8.0f;

        const int MoteCount = 4;
        const float MoteGrabRadius = 0.040f;
        const float MoteRespawnSeconds = 1.5f;
        const float FeedAmount = 14f;

        const float BlockRadiusInner = 0.030f;
        const float BlockRadiusOuter = 0.075f;
        const float PinchSurfaceTolerance = 0.035f;

        const float ShortageThreshold = 0.22f;
        const float SurplusThreshold = 0.82f;
        const float HealthyLow = 0.35f, HealthyHigh = 0.75f;

        const float GlucoseFluxReference = 26f;
        const float OxygenFluxReference = 5f;
        const float WorkFluxReference = 6f;

        // -----------------------------------------------------------------
        // scene state
        // -----------------------------------------------------------------
        Transform _membrane, _cytoplasm, _channelPort;
        Material _membraneMat, _cytoplasmMat, _channelMat;
        Vector3 _portLocalPos;

        class Bead { public Transform View; public Material Mat; public float Radius; }
        Bead[] _beads;

        class Token { public Transform View; public Material Mat; public float Radius; }
        Token[] _tokens;
        Vector3 _atpArcA, _atpArcB;
        static readonly Color AtpSpentColour = new Color(0.30f, 0.27f, 0.36f);

        class Mote
        {
            public Transform View; public Material Mat;
            public bool Held; public PrismHands.Hand HeldBy;
            public Vector3 CradleLocal; public float RespawnTimer;
        }
        Mote[] _motes;

        Transform _glucoseTube, _oxygenTube, _workTube;
        Material _glucoseTubeMat, _oxygenTubeMat, _workTubeMat;

        Transform _sliderRail, _sliderBead;
        Vector3 _sliderA, _sliderB;
        float _sliderU = 0.30f;
        bool _sliderHeld;
        PrismHands.Hand _sliderHand;

        PrismLabel _labelMembrane, _labelDiffusion, _labelEnzyme, _labelMetabolism;

        CellChallenge _challenge;

        float _reveal;                 // 0 before Discover, eases to 1 after
        bool _wasLow, _wasHigh, _everDisturbed;
        float _recoveredFor;
        float _blockHeld;
        bool _blockedThisHold;

        // -----------------------------------------------------------------
        // concepts
        // -----------------------------------------------------------------

        public override IEnumerable<ConceptSpec> Concepts => BuildConcepts();

        static ConceptSpec[] BuildConcepts()
        {
            return new[]
            {
                new ConceptSpec
                {
                    Id = "living-cell", Title = "Living Cell",
                    Domain = ConceptDomain.LifeAndEvolution, Kind = KnowledgeKind.System,
                    Direction = new Vector3(0.14f, 0.05f, 1f), Distance = 3.6f,
                    WorldId = "living-cell",
                    Capability = "Keep a cell's economy balanced through a shortage, a surplus, " +
                                 "and a sudden spike in demand.",
                    Formalisation = "An open system: matter and energy flow through it continuously, " +
                                     "and it is the FLOW that balances, not any single snapshot of what is inside.",
                    Links = new (string, Relation, float)[]
                    {
                        ("membrane-transport", Relation.Composes, 0.80f),
                        ("diffusion",          Relation.Composes, 0.75f),
                        ("enzyme-catalysis",   Relation.Composes, 0.75f),
                        ("metabolism",         Relation.Composes, 0.85f),
                        ("energy-conservation", Relation.Constrains, 0.80f),
                    }
                },
                new ConceptSpec
                {
                    Id = "diffusion", Title = "Diffusion",
                    Domain = ConceptDomain.MatterAndEnergy, Kind = KnowledgeKind.Process,
                    Direction = new Vector3(0.58f, -0.20f, 1f), Distance = 3.9f,
                    Capability = "Predict which way a gradient will push, and how fast, before it happens.",
                    Formalisation = "Fick's first law: J = -D * dC/dx. Net flow runs down the " +
                                     "concentration gradient at a rate set by the diffusion constant D.",
                    Links = new (string, Relation, float)[]
                    {
                        ("probability", Relation.Analogy, 0.55f),
                    }
                },
                new ConceptSpec
                {
                    Id = "enzyme-catalysis", Title = "Enzyme Catalysis",
                    Domain = ConceptDomain.LifeAndEvolution, Kind = KnowledgeKind.Process,
                    Direction = new Vector3(0.40f, 0.35f, 1f), Distance = 4.6f,
                    Capability = "See why a favourable reaction still needs a nudge to start, and how " +
                                 "a catalyst gives it one without being consumed.",
                    Formalisation = "Michaelis-Menten kinetics: v = Vmax[S] / (Km+[S]). Rate saturates " +
                                     "because the enzyme is a finite, reusable resource, not a reactant.",
                    Links = System.Array.Empty<(string, Relation, float)>()
                },
                new ConceptSpec
                {
                    Id = "membrane-transport", Title = "Membrane Transport",
                    Domain = ConceptDomain.LifeAndEvolution, Kind = KnowledgeKind.Equation,
                    Direction = new Vector3(0.07f, -0.40f, 1f), Distance = 4.2f,
                    Capability = "Know, before you block it, what a channel actually controls.",
                    Formalisation = "Flux across a membrane is gated by permeability: a wall does not " +
                                     "stop a gradient, it multiplies it by a number between zero and one.",
                    Links = new (string, Relation, float)[]
                    {
                        ("diffusion", Relation.Constrains, 0.85f),
                    }
                },
                new ConceptSpec
                {
                    Id = "metabolism", Title = "Metabolism",
                    Domain = ConceptDomain.LifeAndEvolution, Kind = KnowledgeKind.Theory,
                    Direction = new Vector3(0.65f, 0.15f, 1f), Distance = 5.1f,
                    Capability = "Trace one currency, ATP, through every pathway that spends or replenishes it.",
                    Formalisation = "A network of coupled reactions at steady state: production must " +
                                     "track consumption, or a pool empties or floods.",
                    Links = new (string, Relation, float)[]
                    {
                        ("enzyme-catalysis", Relation.Composes, 0.70f),
                        ("feedback",         Relation.Analogy,  0.65f),
                    }
                },
            };
        }

        // -----------------------------------------------------------------
        // build
        // -----------------------------------------------------------------

        protected override void BuildWorld()
        {
            Sim = new CellSim();

            BuildMembraneAndCytoplasm();
            // The anatomy the sim was always driving but never showing. See CellInterior.
            _interior = new CellInterior(Anchor, MembraneRadius);
            BuildChannelPort();
            BuildGlucoseMotes();
            BuildOxygenBeads();
            BuildAtpTokens();
            BuildFluxTubes();
            BuildSlider();
            BuildLabels();

            _challenge = gameObject.AddComponent<CellChallenge>();
            _challenge.World = this;
        }

        CellInterior _interior;

        void BuildMembraneAndCytoplasm()
        {
            _membraneMat = PrismMaterials.New(PrismMaterials.Gel);
            _membraneMat.SetColor("_Tint", PrismPalette.Cyan);
            _membraneMat.SetColor("_DeepTint", PrismPalette.Violet);
            // Thinned from 1.4 once there was anatomy behind it worth seeing. 0.85 was still far
            // too much: against a dark backdrop Prism_Compose returns alpha ~= density, so the
            // membrane came out 95% opaque and swallowed the interior whole. A membrane you cannot
            // see through is not a membrane, it is a shell.
            _membraneMat.SetFloat("_Density", 0.28f);
            _membraneMat.SetFloat("_NoiseFreq", 3.0f);
            _membraneMat.SetFloat("_FlowSpeed", 0.10f);
            _membraneMat.SetVector("_FlowAxis", new Vector4(0, 1, 0, 0));
            _membraneMat.renderQueue = 3100;      // after the interior; see CellInterior.InteriorQueue
            _membrane = Body(PrismMesh.Icosphere(3), MembraneRadius, _membraneMat, "Membrane");

            _cytoplasmMat = PrismMaterials.New(PrismMaterials.Gel);
            _cytoplasmMat.SetColor("_Tint", PrismPalette.Mint);
            _cytoplasmMat.SetColor("_DeepTint", PrismPalette.Lavender);
            _cytoplasmMat.SetFloat("_Density", 1.8f);
            _cytoplasmMat.SetFloat("_NoiseFreq", 5.5f);
            _cytoplasmMat.SetFloat("_FlowSpeed", 0.4f);
            _cytoplasmMat.SetVector("_FlowAxis", new Vector4(0, 1, 0.3f, 0));
            _cytoplasm = Body(PrismMesh.Icosphere(3), CytoplasmRadius, _cytoplasmMat, "Cytoplasm");
        }

        void BuildChannelPort()
        {
            Vector3 portDir = new Vector3(0.35f, 0.05f, 0.93f).normalized;
            _portLocalPos = portDir * (MembraneRadius + 0.012f);

            _channelMat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.30f, 260f);
            _channelPort = Body(PrismMesh.Icosphere(1), 0.018f, _channelMat, "GlucoseChannel");
            _channelPort.localPosition = _portLocalPos;
        }

        void BuildGlucoseMotes()
        {
            Vector3[] dirs =
            {
                new Vector3(0.30f, 0.15f, 0.90f).normalized,
                new Vector3(0.45f, 0.00f, 0.85f).normalized,
                new Vector3(0.25f, -0.10f, 0.92f).normalized,
                new Vector3(0.48f, 0.12f, 0.83f).normalized,
            };

            _motes = new Mote[MoteCount];
            for (int i = 0; i < MoteCount; i++)
            {
                var mat = PrismMaterials.New(PrismMaterials.Seed);
                mat.SetColor("_Tint", PrismPalette.Gold);
                mat.SetFloat("_Growth", 1f);
                mat.SetFloat("_Density", 1.1f);
                mat.SetFloat("_FilmNm", 320f + i * 40f);

                var m = new Mote
                {
                    Mat = mat,
                    CradleLocal = dirs[i % dirs.Length] * (MembraneRadius * 1.6f),
                };
                m.View = Body(PrismMesh.Icosphere(1), 0.014f, mat, $"GlucoseMote{i}");
                m.View.localPosition = m.CradleLocal;
                _motes[i] = m;
            }
        }

        void BuildOxygenBeads()
        {
            Vector3 beadDir = new Vector3(-0.45f, 0.10f, 0.85f).normalized;
            _beads = new Bead[OxygenBeadCount];
            for (int i = 0; i < OxygenBeadCount; i++)
            {
                float t = OxygenBeadCount <= 1 ? 0f : (float)i / (OxygenBeadCount - 1);
                float radius = Mathf.Lerp(MembraneRadius * 1.40f, MembraneRadius * 0.65f, t);

                var mat = PrismMaterials.CeramicBody(PrismPalette.Cyan, 0.35f);
                var b = new Bead { Mat = mat, Radius = 0.011f };
                b.View = Body(PrismMesh.Icosphere(1), b.Radius, mat, $"OxygenBead{i}");
                b.View.localPosition = beadDir * radius;
                _beads[i] = b;
            }
        }

        void BuildAtpTokens()
        {
            _atpArcA = new Vector3(-0.065f, 0.155f, 0f);
            _atpArcB = new Vector3(0.065f, 0.155f, 0f);

            _tokens = new Token[AtpTokenCount];
            for (int i = 0; i < AtpTokenCount; i++)
            {
                float u = AtpTokenCount <= 1 ? 0f : (float)i / (AtpTokenCount - 1);
                var mat = PrismMaterials.CeramicBody(AtpSpentColour, 0.30f);
                var tk = new Token { Mat = mat, Radius = 0.0048f };
                tk.View = Body(PrismMesh.Icosphere(1), tk.Radius, mat, $"AtpToken{i}");
                tk.View.localPosition = Vector3.Lerp(_atpArcA, _atpArcB, u);
                _tokens[i] = tk;
            }
        }

        void BuildFluxTubes()
        {
            Vector3 portDir = _portLocalPos.normalized;
            _glucoseTubeMat = PrismMaterials.New(PrismMaterials.Flow);
            _glucoseTubeMat.SetColor("_Tint", PrismPalette.Gold);
            _glucoseTubeMat.SetFloat("_CoreGain", 0.5f);
            _glucoseTube = BuildStaticTube(
                portDir * (MembraneRadius + 0.010f), portDir * (MembraneRadius * 0.25f),
                0.003f, _glucoseTubeMat, "GlucoseFlux");

            Vector3 beadDir = new Vector3(-0.45f, 0.10f, 0.85f).normalized;
            _oxygenTubeMat = PrismMaterials.New(PrismMaterials.Flow);
            _oxygenTubeMat.SetColor("_Tint", PrismPalette.Cyan);
            _oxygenTubeMat.SetFloat("_CoreGain", 0.5f);
            _oxygenTube = BuildStaticTube(
                beadDir * (MembraneRadius * 1.42f), beadDir * (MembraneRadius * 0.25f),
                0.003f, _oxygenTubeMat, "OxygenFlux");

            _workTubeMat = PrismMaterials.New(PrismMaterials.Flow);
            _workTubeMat.SetColor("_Tint", PrismPalette.Coral);
            _workTubeMat.SetFloat("_CoreGain", 0.5f);
            _workTube = BuildStaticTube(
                Vector3.zero, new Vector3(0f, 0.14f, 0f),
                0.003f, _workTubeMat, "WorkFlux");
        }

        Transform BuildStaticTube(Vector3 aLocal, Vector3 bLocal, float radius, Material mat, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Anchor, false);
            var pts = new List<Vector3> { aLocal, bLocal };
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Tube(pts, radius, 6);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        void BuildSlider()
        {
            _sliderA = new Vector3(-0.05f, -0.155f, 0f);
            _sliderB = new Vector3(0.05f, -0.155f, 0f);

            var railMat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.18f);
            _sliderRail = BuildStaticTube(_sliderA, _sliderB, 0.0022f, railMat, "TuningRail");

            var beadMat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.45f, 280f);
            _sliderBead = Body(PrismMesh.Icosphere(1), 0.012f, beadMat, "TuningBead");
            _sliderBead.localPosition = Vector3.Lerp(_sliderA, _sliderB, _sliderU);

            _sliderRail.gameObject.SetActive(false);
            _sliderBead.gameObject.SetActive(false);
        }

        void BuildLabels()
        {
            _labelMembrane = PrismLabel.Create("LabelMembraneTransport", Anchor, Head, 0.014f);
            _labelMembrane.SetText("membrane transport\npermeability gates the gradient", PrismPalette.Gold);

            _labelDiffusion = PrismLabel.Create("LabelDiffusion", Anchor, Head, 0.014f);
            _labelDiffusion.SetText("diffusion\nJ = -D dC/dx", PrismPalette.Cyan);

            _labelEnzyme = PrismLabel.Create("LabelEnzyme", Anchor, Head, 0.014f);
            _labelEnzyme.SetText("enzyme catalysis\nv = Vmax[S] / (Km+[S])", PrismPalette.Mint);

            _labelMetabolism = PrismLabel.Create("LabelMetabolism", Anchor, Head, 0.014f);
            _labelMetabolism.SetText("metabolism\nATP out, ADP back, never both at once", PrismPalette.Lavender);
        }

        // -----------------------------------------------------------------
        // loop
        // -----------------------------------------------------------------

        protected override void ConfigureLoop(LearningLoop loop)
        {
            loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet touched the cell",
                e => e.Has(LivingCellEvidence.Interacted)));

            // The gate that matters most, mirroring the orbital world's bound/unbound straddle: the
            // learner must have personally driven the SAME quantity — the cell's ATP charge — to
            // both a shortage and a surplus before the mechanism behind it is revealed.
            loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet caused both a shortage and a surplus",
                e => e.Has(LivingCellEvidence.Shortage) && e.Has(LivingCellEvidence.Surplus)
                  && e.Count(LivingCellEvidence.Interacted) >= 3));

            loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet brought the cell back to a healthy balance",
                e => e.Has(LivingCellEvidence.Recovered)));

            loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet used both controls again, now that they are named",
                e => e.Count(LivingCellEvidence.Blocked) >= 2 && e.Count(LivingCellEvidence.Fed) >= 2));

            loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet survived a demand spike",
                e => e.Has(LivingCellEvidence.SpikeSurvived)));

            loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet predicted the charge correctly",
                e => e.Has(LivingCellEvidence.PredictionGood)));

            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet tuned a cell that survives a full cycle",
                e => e.Has(LivingCellEvidence.CycleSurvived)));
        }

        protected override void OnStageEntered(LoopStage stage)
        {
            switch (stage)
            {
                case LoopStage.Apply:
                    _challenge.BeginSpike();
                    break;
                case LoopStage.Explain:
                    _challenge.BeginPrediction();
                    break;
                case LoopStage.Create:
                    _sliderRail.gameObject.SetActive(true);
                    _sliderBead.gameObject.SetActive(true);
                    _challenge.BeginCycle();
                    break;
                case LoopStage.Connect:
                    Knowledge?.Confirm(PrimaryConceptId, 0.4f);
                    break;
            }
            base.OnStageEntered(stage);
        }

        // -----------------------------------------------------------------
        // per-frame
        // -----------------------------------------------------------------

        protected override void Tick(float dt)
        {
            ServiceChannelBlock(dt);
            ServicePinch();
            ServiceMotes(dt);
            if (Loop.Stage >= LoopStage.Create) ServiceSlider();

            Sim.Advance(dt);

            _interior?.Tick(dt, Head, Sim.LastCatabolismRate, Sim.AtpFraction,
                            // Full brightness at half the enzyme's ceiling: the cell spends almost
                            // all of its life well below Vmax, so scaling to Vmax itself would keep
                            // the mitochondria dim through every situation the learner creates.
                            Sim.VmaxCatabolism * 0.5f);

            TrackShortageSurplus(dt);

            _reveal = Mathf.MoveTowards(_reveal, Loop.Stage >= LoopStage.Discover ? 1f : 0f, dt / 3f);

            SyncVisuals(dt);

            _challenge?.Evaluate(dt);
        }

        public Vector3 AtpArcLocalPoint(float u) => Vector3.Lerp(_atpArcA, _atpArcB, Mathf.Clamp01(u));

        /// <summary>Distance from one hand to a point, or 'best' unchanged if the hand cannot answer. No allocation.</summary>
        static float NearestOf(PrismHands.Hand h, Vector3 point, float best)
        {
            if (h == null || !h.IsTracked) return best;
            float d = Vector3.Distance(PrismHands.PointOf(h), point);
            return d < best ? d : best;
        }

        void ServiceChannelBlock(float dt)
        {
            float nearest = float.MaxValue;
            if (Hands != null)
            {
                Vector3 portWorld = Anchor.TransformPoint(_portLocalPos);
                nearest = NearestOf(Hands.Left, portWorld, nearest);
                nearest = NearestOf(Hands.Right, portWorld, nearest);
            }

            float proximityTarget = Mathf.Clamp01(Mathf.InverseLerp(BlockRadiusInner, BlockRadiusOuter, nearest));
            float target = ChannelForcedClosed ? 0f : proximityTarget;
            Sim.ChannelOpen = Mathf.MoveTowards(Sim.ChannelOpen, target, dt * 2.5f);

            bool blockedByHand = !ChannelForcedClosed && Sim.ChannelOpen < 0.25f;
            _blockHeld = blockedByHand ? _blockHeld + dt : 0f;
            if (_blockHeld > 0.8f && !_blockedThisHold)
            {
                _blockedThisHold = true;
                Loop.Evidence.Record(LivingCellEvidence.Blocked);
                Loop.Evidence.Record(LivingCellEvidence.Interacted);
            }
            if (!blockedByHand) _blockedThisHold = false;
        }

        void ServicePinch()
        {
            if (Hands == null) return;
            TryPinchWith(Hands.Left);
            TryPinchWith(Hands.Right);
        }

        void TryPinchWith(PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked || !h.PinchDown) return;
            float d = Vector3.Distance(h.Position, Anchor.position);
            if (Mathf.Abs(d - MembraneRadius) > PinchSurfaceTolerance) return;

            Sim.Puncture(1f);
            Loop.Evidence.Record(LivingCellEvidence.Pinched);
            Loop.Evidence.Record(LivingCellEvidence.Interacted);
            Hands.Clunk(h, 0.5f);
        }

        void ServiceMotes(float dt)
        {
            if (Hands == null) return;

            foreach (var m in _motes)
            {
                if (m.RespawnTimer > 0f)
                {
                    m.RespawnTimer -= dt;
                    if (m.RespawnTimer <= 0f)
                    {
                        m.View.gameObject.SetActive(true);
                        m.View.localPosition = m.CradleLocal;
                    }
                    continue;
                }

                if (m.Held)
                {
                    if (m.HeldBy == null || !m.HeldBy.IsTracked || !m.HeldBy.IsGrasping)
                    {
                        float distToCentre = Vector3.Distance(m.View.position, Anchor.position);
                        if (distToCentre < MembraneRadius * 1.05f)
                        {
                            Sim.DepositGlucose(FeedAmount);
                            Loop.Evidence.Record(LivingCellEvidence.Fed);
                            Loop.Evidence.Record(LivingCellEvidence.Interacted);
                            if (m.HeldBy != null) Hands.Clunk(m.HeldBy, 0.4f);
                            Companion?.Voice?.Settle(m.View.position, 0.3f);
                            m.View.gameObject.SetActive(false);
                            m.RespawnTimer = MoteRespawnSeconds;
                        }
                        m.Held = false;
                        m.HeldBy = null;
                        continue;
                    }

                    m.View.position = PrismHands.PointOf(m.HeldBy);
                    continue;
                }

                Vector3 home = Anchor.TransformPoint(m.CradleLocal);
                float bob = Mathf.Sin(Time.time * 0.9f + m.CradleLocal.x * 13f) * 0.004f;
                m.View.position = Vector3.Lerp(m.View.position, home + Vector3.up * bob, dt * 5f);

                var grabber = NearestHand(m.View.position, MoteGrabRadius);
                if (grabber != null && grabber.IsGrasping)
                {
                    m.Held = true;
                    m.HeldBy = grabber;
                    Hands.Buzz(grabber, 0.2f, 0.04f);
                }
            }
        }

        void ServiceSlider()
        {
            Vector3 aw = Anchor.TransformPoint(_sliderA);
            Vector3 bw = Anchor.TransformPoint(_sliderB);

            if (_sliderHeld)
            {
                if (_sliderHand == null || !_sliderHand.IsTracked || !_sliderHand.IsGrasping)
                {
                    _sliderHeld = false;
                    _sliderHand = null;
                }
                else
                {
                    Vector3 p = PrismHands.PointOf(_sliderHand);
                    Vector3 ab = bw - aw;
                    float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
                    _sliderU = Mathf.Clamp01(Vector3.Dot(p - aw, ab) / len2);
                }
            }
            else
            {
                Vector3 beadPos = Vector3.Lerp(aw, bw, _sliderU);
                var grabber = NearestHand(beadPos, 0.03f);
                if (grabber != null && grabber.IsGrasping)
                {
                    _sliderHeld = true;
                    _sliderHand = grabber;
                }
            }

            Sim.CatabolismTuning = Mathf.Lerp(0.4f, 2.2f, _sliderU);
            if (_sliderBead != null) _sliderBead.position = Vector3.Lerp(aw, bw, _sliderU);
        }

        void TrackShortageSurplus(float dt)
        {
            float x = Sim.AtpFraction;
            bool low = x < ShortageThreshold;
            bool high = x > SurplusThreshold;

            if (low && !_wasLow) { Loop.Evidence.Record(LivingCellEvidence.Shortage); _everDisturbed = true; }
            if (high && !_wasHigh) { Loop.Evidence.Record(LivingCellEvidence.Surplus); _everDisturbed = true; }
            _wasLow = low;
            _wasHigh = high;

            bool healthy = x > HealthyLow && x < HealthyHigh;
            _recoveredFor = (_everDisturbed && healthy) ? _recoveredFor + dt : 0f;
            if (_recoveredFor > 2f && !Loop.Evidence.Has(LivingCellEvidence.Recovered))
                Loop.Evidence.Record(LivingCellEvidence.Recovered);
        }

        void SyncVisuals(float dt)
        {
            float x = Sim.AtpFraction;
            _cytoplasmMat.SetColor("_Tint", PrismPalette.Spectral(x));

            float leakGlow = Sim.Leak;
            _membraneMat.SetFloat("_Density", Mathf.Lerp(1.4f, 2.4f, leakGlow));

            float open = Sim.ChannelOpen;
            _channelMat.SetFloat("_Luminance", Mathf.Lerp(0.08f, 0.55f, open));
            if (_channelPort != null)
                _channelPort.localScale = Vector3.one * (0.018f * Mathf.Lerp(0.8f, 1f, open));

            for (int i = 0; i < _beads.Length; i++)
            {
                float c = Sim.OxygenShellC(i);
                float t = Mathf.Clamp01(c / OxygenReference);
                _beads[i].Mat.SetColor("_Tint", PrismPalette.Spectral(t));
                _beads[i].View.localScale = Vector3.one * (_beads[i].Radius * _reveal);
            }

            AtpLitCount = Mathf.RoundToInt(x * (AtpTokenCount - 1));
            float litF = x * AtpTokenCount;
            for (int i = 0; i < _tokens.Length; i++)
            {
                float b = Mathf.Clamp01(litF - i);
                _tokens[i].Mat.SetColor("_Tint", Color.Lerp(AtpSpentColour, PrismPalette.Gold, b));
                _tokens[i].View.localScale = Vector3.one * (_tokens[i].Radius * _reveal);
            }

            UpdateFluxTube(_glucoseTubeMat, Sim.LastGlucoseFlux, GlucoseFluxReference);
            UpdateFluxTube(_oxygenTubeMat, Sim.LastOxygenInnerFlux, OxygenFluxReference);
            UpdateFluxTube(_workTubeMat, Sim.LastWorkRate, WorkFluxReference);

            // Text is almost none and never at rest: labels get one brief introduction the moment
            // Formalize is reached, then only reappear on approach, exactly like the rest of PRISM.
            // Permanently-on labels from Formalize onward would be exactly the "text at rest" the
            // contract warns against.
            if (Loop.Stage >= LoopStage.Formalize && _formalizeIntroFor < 0f) _formalizeIntroFor = 0f;
            if (_formalizeIntroFor >= 0f) _formalizeIntroFor += dt;
            bool stageReached = Loop.Stage >= LoopStage.Formalize;
            bool introWindow = _formalizeIntroFor >= 0f && _formalizeIntroFor < FormalizeIntroSeconds;

            ShowLabelNear(_labelMembrane, Anchor.TransformPoint(_portLocalPos), 0.028f, stageReached, introWindow);
            ShowLabelNear(_labelDiffusion, _beads[OxygenBeadCount / 2].View.position, 0.022f, stageReached, introWindow);
            ShowLabelNear(_labelEnzyme, _cytoplasm.position, CytoplasmRadius + 0.03f, stageReached, introWindow);
            ShowLabelNear(_labelMetabolism, Anchor.TransformPoint((_atpArcA + _atpArcB) * 0.5f), 0.022f, stageReached, introWindow);
        }

        const float FormalizeIntroSeconds = 6f;
        const float LabelApproachRadius = 0.15f;
        float _formalizeIntroFor = -1f;

        void ShowLabelNear(PrismLabel label, Vector3 worldPoint, float offset, bool stageReached, bool introWindow)
        {
            bool show = stageReached && (introWindow || HandNear(worldPoint, LabelApproachRadius));
            label.Show(show);
            if (show) label.PlaceAbove(worldPoint, offset);
        }

        bool HandNear(Vector3 worldPoint, float radius)
        {
            if (Hands == null) return false;
            if (Hands.Left != null && Hands.Left.IsTracked
                && Vector3.Distance(Hands.Left.Position, worldPoint) < radius) return true;
            if (Hands.Right != null && Hands.Right.IsTracked
                && Vector3.Distance(Hands.Right.Position, worldPoint) < radius) return true;
            return false;
        }

        void UpdateFluxTube(Material mat, float rate, float reference)
        {
            float mag = Mathf.Clamp01(Mathf.Abs(rate) / Mathf.Max(reference, 1e-4f)) * _reveal;
            mat.SetFloat("_Packets", Mathf.Lerp(1f, 8f, mag));
            mat.SetFloat("_Speed", Mathf.Lerp(0.1f, 1.6f, mag));
            mat.SetFloat("_Strength", Mathf.Lerp(0f, 0.9f, mag));
        }
    }
}
