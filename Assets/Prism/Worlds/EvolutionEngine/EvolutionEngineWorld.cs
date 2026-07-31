using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using Prism.Worlds;
using Prism.Worlds.Orbital;
using UnityEngine;

namespace Prism.Worlds.EvolutionEngine
{
    /// <summary>Evidence keys. Named constants because the gates and the world must agree exactly.</summary>
    public static class EvolutionEvidence
    {
        public const string Birth = "birth";
        public const string Death = "death";

        // Explore -> Discover: the learner has driven the population's mean trait both up and
        // down by holding the temperature at an extreme long enough for births and deaths to
        // actually move it, in both directions, at different times.
        public const string ShiftWarm = "shift.warm";
        public const string ShiftCold = "shift.cold";

        // Discover -> Formalize: caused at least one more confirmed shift with the histogram
        // already on screen, so the causal link was watched, not just inferred once.
        public const string ShiftWhileVisible = "shift.visible";

        // Formalize -> Apply: introduced the predator at least once, a second and qualitatively
        // different kind of filter than temperature.
        public const string PredatorUsed = "predator.used";

        // Apply -> Explain: held the population inside a target band for the full duration.
        public const string ApplyDone = "apply.done";

        // Explain -> Create: correctly predicted which way the mean would move, then verified it.
        public const string PredictionGood = "prediction.good";

        // Create -> Connect: two separated, sustained peaks in the living distribution.
        public const string CreateBimodal = "create.bimodal";
    }

    /// <summary>
    /// The Evolution Engine world: change the world and watch life answer.
    ///
    /// The choreography, and — as with the orbital world — what the learner is deliberately NOT
    /// shown until they have earned it:
    ///
    ///   Wonder     A population of small organisms, varying visibly in size, wandering a shallow
    ///              terrarium. They are born; they die. Nothing is labelled, nothing is explained,
    ///              and nothing responds to a hand placed on an organism — there is no grab target
    ///              on them at all, from the first second. Only the WORLD around them can be
    ///              touched.
    ///   Explore    A temperature control, a predator, a resource and a token that speeds up time
    ///              are all reachable. The learner discovers, by trying things, that moving these
    ///              changes what the population looks like later — never immediately, never by a
    ///              direct edit, only through who gets born and who does not.
    ///   Discover   Once the learner has held the temperature at one extreme long enough to watch
    ///              the population answer, and then at the other extreme and watched it answer
    ///              again, the histogram appears: a real, counted distribution of the living
    ///              population's trait, in the same colours the organisms already wear.
    ///   Formalize  Only now: population size and mean trait as a small readout, and the concepts
    ///              variation, heritability, selection and fitness enter the constellation.
    ///   Apply      Hold the population inside a target band, by environment alone.
    ///   Explain    Predict where the mean will settle, mark it, change something, and watch.
    ///   Create     Build an environment that produces two separated, stable forms at once.
    ///   Connect    Return to the atrium; the constellation has changed.
    ///
    /// SIMULATION HONESTY. <see cref="EvolutionSim"/> is the only place a trait value or a
    /// population mean is ever set. This file reads it, translates hand and object positions into
    /// the three environment numbers the simulation accepts (temperature, predator, resource), and
    /// records evidence from what the simulation reports back. It never writes a trait value and it
    /// never writes MeanTrait. If that ever needs to change, the simulation has stopped being real.
    /// </summary>
    public class EvolutionEngineWorld : PrismWorldBase
    {
        public const string OrganismShaderName = "Prism/EvolutionEngineOrganism";

        public override string WorldId => "evolution-engine";

        /// <summary>Deep time: a warm dim basin with geology in the walls.</summary>
        protected override WorldShellSpec Shell => WorldShellSpec.DeepTime;
        public override string PrimaryConceptId => "evolution-engine";
        public override string DisplayName => "Evolution Engine";
        public override string[] Shaders => new[] { OrganismShaderName };

        /// <summary>Exposes the protected base Anchor to sibling components (EvolutionChallenge)
        /// that are not subclasses of PrismWorldBase and so cannot see a protected member.</summary>
        public Transform WorldAnchor => Anchor;

        public EvolutionSim Sim { get; private set; }
        public EvolutionHistogram Histogram { get; private set; }

        // ---- layout, local to Anchor. Kept well inside the ~0.75 m arm's-reach envelope Orbital
        // already establishes as safe, on the same reasoning: everything here sits at Reach plus,
        // at most, about a quarter of a metre. ----
        static readonly Vector3 DialRodCentre     = new Vector3(-0.20f, 0.05f, 0.06f);
        const float DialHalfLength                = 0.04f;
        static readonly Vector3 PredatorRestLocal = new Vector3(0.21f, 0.045f, 0.02f);
        static readonly Vector3 ResourceRestLocal = new Vector3(0.0f, 0.045f, -0.21f);
        static readonly Vector3 TimeStoneRestLocal = new Vector3(-0.19f, 0.05f, -0.13f);
        static readonly Vector3 HistogramLocalCentre = new Vector3(0f, 0f, 0.20f);
        const float GrabRadius = 0.05f;

        Transform _predator;
        Material _predatorMat;
        bool _predatorHeld;
        PrismHands.Hand _predatorHand;

        Transform _resource;
        Material _resourceMat;
        bool _resourceHeld;
        PrismHands.Hand _resourceHand;

        Transform _dialBead;
        Material _dialMat;

        Transform _timeStone;
        Material _timeMat;
        float _timeScale = 1f;

        EvolutionOrganismField _organismField;
        EvolutionChallenge _challenge;
        PrismLabel _label;

        long _lastBirths, _lastDeaths;

        float _hotDwell, _coldDwell;
        float _meanUAtHotStart, _meanUAtColdStart;
        bool _warmShiftFired, _coldShiftFired;
        const float ShiftUThreshold = 0.09f;
        const float DwellRequired = 10f;

        // -----------------------------------------------------------------
        // construction
        // -----------------------------------------------------------------

        protected override void BuildWorld()
        {
            // Everything here sits close and slightly low, like a terrarium on a table rather
            // than a planet at the end of an outstretched arm.
            Reach = 0.50f;
            EyeToTable = 0.46f;

            Sim = new EvolutionSim();

            BuildTerrain();
            BuildOrganisms();
            BuildTemperatureDial();
            BuildPredator();
            BuildResource();
            BuildTimeStone();

            Histogram = new EvolutionHistogram(Anchor, HistogramLocalCentre);
            Histogram.Show(false);

            _label = PrismLabel.Create("EvolutionLabel", Anchor, Head, 0.015f);

            _challenge = gameObject.AddComponent<EvolutionChallenge>();
            _challenge.World = this;
        }

        void BuildTerrain()
        {
            var mat = PrismMaterials.CeramicBody(Color.Lerp(PrismPalette.Warm, PrismPalette.Mint, 0.14f), 0.20f, 0f);
            var go = new GameObject("Terrarium");
            go.transform.SetParent(Anchor, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Disc(96, 18);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localPosition = EvolutionSim.TerrainCentre;
            go.transform.localScale = new Vector3(EvolutionSim.TerrainRadius, 1f, EvolutionSim.TerrainRadius);
        }

        void BuildOrganisms()
        {
            var go = new GameObject("Organisms");
            go.transform.SetParent(Anchor, false);
            _organismField = go.AddComponent<EvolutionOrganismField>();   // RequireComponent adds Filter+Renderer

            var mat = PrismMaterials.New(OrganismShaderName);
            mat.SetFloat("_Density", 1.15f);
            mat.SetFloat("_Softness", 0.6f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            _organismField.SetCamera(Head != null ? Head : OrbitalWorld.FindHeadCamera());
        }

        /// <summary>
        /// A small heat/cold token sliding on a rod. Its own colour previews which trait value is
        /// CURRENTLY favoured reproductively at this temperature — the same spectral encoding the
        /// organisms and the histogram use, not a second colour meaning to learn.
        /// </summary>
        void BuildTemperatureDial()
        {
            var rodPts = new List<Vector3>
            {
                DialRodCentre + new Vector3(-DialHalfLength, 0f, 0f),
                DialRodCentre + new Vector3( DialHalfLength, 0f, 0f)
            };
            var rodGo = new GameObject("DialRod");
            rodGo.transform.SetParent(Anchor, false);
            rodGo.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Tube(rodPts, 0.0014f, 6);
            rodGo.AddComponent<MeshRenderer>().sharedMaterial = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.16f);

            var mat = PrismMaterials.New(PrismMaterials.Seed);
            mat.SetFloat("_Growth", 1f);
            mat.SetFloat("_Density", 1.1f);
            _dialMat = mat;
            _dialBead = Body(PrismMesh.Icosphere(2), 0.016f, mat, "TemperatureBead");
            RefreshDialVisual();
        }

        /// <summary>Coral: a fixed identity colour for an agent, not a trait reading — distinct on
        /// purpose from the spectral trait encoding everything else in this world uses.</summary>
        void BuildPredator()
        {
            var mat = PrismMaterials.New(PrismMaterials.Seed);
            mat.SetColor("_Tint", PrismPalette.Coral);
            mat.SetFloat("_Growth", 1f);
            mat.SetFloat("_Density", 1.25f);
            mat.SetFloat("_FilmNm", 240f);
            _predatorMat = mat;
            _predator = Body(PrismMesh.Icosphere(2), 0.026f, mat, "Predator");
            _predator.localPosition = PredatorRestLocal;
        }

        /// <summary>Living gel currents; density brightens as it is placed somewhere more effective.</summary>
        void BuildResource()
        {
            var mat = PrismMaterials.New(PrismMaterials.Gel);
            mat.SetColor("_Tint", PrismPalette.Mint);
            mat.SetColor("_DeepTint", PrismPalette.Gold);
            mat.SetFloat("_NoiseFreq", 5f);
            mat.SetFloat("_FlowSpeed", 0.22f);
            _resourceMat = mat;
            _resource = Body(PrismMesh.Icosphere(2), 0.02f, mat, "Resource");
            _resource.localPosition = ResourceRestLocal;
        }

        void BuildTimeStone()
        {
            var mat = PrismMaterials.New(PrismMaterials.Volumetric);
            mat.SetColor("_Tint", PrismPalette.Violet);
            mat.SetColor("_EdgeTint", PrismPalette.Cyan);
            mat.SetFloat("_Density", 0.75f);
            _timeMat = mat;
            _timeStone = Body(PrismMesh.Icosphere(2), 0.015f, mat, "TimeStone");
            _timeStone.localPosition = TimeStoneRestLocal;
        }

        // -----------------------------------------------------------------
        // the loop
        // -----------------------------------------------------------------

        protected override void ConfigureLoop(LearningLoop loop)
        {
            loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet watched a birth and a death happen",
                e => e.Count(EvolutionEvidence.Birth) >= 2 && e.Count(EvolutionEvidence.Death) >= 1));

            // The gate that matters most, mirroring the orbital world's Explore -> Discover gate:
            // wait for the learner to have produced BOTH directions of change themselves. Only
            // once they have personally pushed the population one way and then the other are they
            // ready to be shown a chart of what they did.
            loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet driven the population both larger and smaller",
                e => e.Has(EvolutionEvidence.ShiftWarm) && e.Has(EvolutionEvidence.ShiftCold)));

            loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet watched the histogram answer another change",
                e => e.Has(EvolutionEvidence.ShiftWhileVisible)));

            loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet introduced a predator",
                e => e.Has(EvolutionEvidence.PredatorUsed)));

            loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet held the population at the target",
                e => e.Has(EvolutionEvidence.ApplyDone)));

            loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet correctly predicted which way the population would move",
                e => e.Has(EvolutionEvidence.PredictionGood)));

            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet produced two stable forms at once",
                e => e.Has(EvolutionEvidence.CreateBimodal)));
        }

        protected override void OnStageEntered(LoopStage stage)
        {
            switch (stage)
            {
                case LoopStage.Apply:
                    _challenge.BeginTargetChallenge();
                    break;
                case LoopStage.Explain:
                    _challenge.BeginPrediction();
                    break;
                case LoopStage.Connect:
                    Knowledge?.Confirm(PrimaryConceptId, 0.4f);
                    Debug.Log("[PRISM] Evolution Engine: Connect reached. The constellation changes.");
                    break;
            }
            base.OnStageEntered(stage);
        }

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        protected override void Tick(float dt)
        {
            HandleDial();
            HandlePredator();
            HandleResource();
            HandleTimeStone(dt);
            RefreshDialVisual();

            Sim.Advance(dt);

            TrackBirthsAndDeaths();
            TrackShiftEvidence(dt);

            _organismField.Rebuild(Sim.Pop);

            if (Loop.Stage >= LoopStage.Discover)
            {
                if (!Histogram.Visible) Histogram.Show(true);
                Histogram.UpdateBars(Sim, dt);
            }

            UpdateLabel();
            _challenge.Evaluate(dt);
        }

        // ---- environment controls -----------------------------------------

        void HandleDial()
        {
            if (Hands == null) return;
            Vector3 beadWorld = Anchor.TransformPoint(_dialBead.localPosition);

            foreach (var h in new[] { Hands.Left, Hands.Right })
            {
                if (h == null || !h.IsTracked || h.Pinch < 0.55f) continue;
                Vector3 point = PrismHands.PointOf(h);
                if ((point - beadWorld).sqrMagnitude > GrabRadius * GrabRadius) continue;

                float localX = Anchor.InverseTransformPoint(point).x;
                float t = Mathf.InverseLerp(DialRodCentre.x - DialHalfLength, DialRodCentre.x + DialHalfLength, localX);
                Sim.Temperature = Mathf.Clamp01(t);
                break;
            }
        }

        void RefreshDialVisual()
        {
            float x = Mathf.Lerp(-DialHalfLength, DialHalfLength, Sim.Temperature);
            _dialBead.localPosition = DialRodCentre + new Vector3(x, 0f, 0f);
            float favouredU = EvolutionSim.TraitToU01(Sim.OptimalTraitAt(Sim.Temperature));
            _dialMat.SetColor("_Tint", PrismPalette.Spectral(favouredU));
        }

        /// <summary>
        /// The predator hunts whenever it is not currently held AND sits inside the terrarium —
        /// a plain spatial rule, so "introducing a predator" is exactly the gesture it sounds
        /// like: pick it up, carry it in, set it down.
        /// </summary>
        void HandlePredator()
        {
            if (Hands != null)
            {
                if (_predatorHeld)
                {
                    if (_predatorHand != null && _predatorHand.IsTracked && _predatorHand.IsGrasping)
                        _predator.position = PrismHands.PointOf(_predatorHand);
                    else
                        _predatorHeld = false;
                }
                else
                {
                    foreach (var h in new[] { Hands.Left, Hands.Right })
                    {
                        if (h == null || !h.IsTracked || !h.IsGrasping) continue;
                        if ((PrismHands.PointOf(h) - _predator.position).sqrMagnitude > GrabRadius * GrabRadius) continue;
                        _predatorHeld = true;
                        _predatorHand = h;
                        Hands.Buzz(h, 0.25f, 0.04f);
                        break;
                    }
                }
            }

            Vector3 local = Anchor.InverseTransformPoint(_predator.position);
            Vector3 flat = local - EvolutionSim.TerrainCentre; flat.y = 0f;
            bool active = !_predatorHeld && flat.magnitude <= EvolutionSim.TerrainRadius;

            if (active && !Sim.PredatorActive) Loop.Evidence.Record(EvolutionEvidence.PredatorUsed);

            Sim.PredatorActive = active;
            Sim.PredatorLocalPos = new Vector3(local.x, 0f, local.z);
        }

        /// <summary>The resource's effect is continuous with its position, not with a placed/held
        /// state, so the learner sees carrying capacity respond while they are still moving it.</summary>
        void HandleResource()
        {
            if (Hands != null)
            {
                if (_resourceHeld)
                {
                    if (_resourceHand != null && _resourceHand.IsTracked && _resourceHand.IsGrasping)
                        _resource.position = PrismHands.PointOf(_resourceHand);
                    else
                        _resourceHeld = false;
                }
                else
                {
                    foreach (var h in new[] { Hands.Left, Hands.Right })
                    {
                        if (h == null || !h.IsTracked || !h.IsGrasping) continue;
                        if ((PrismHands.PointOf(h) - _resource.position).sqrMagnitude > GrabRadius * GrabRadius) continue;
                        _resourceHeld = true;
                        _resourceHand = h;
                        Hands.Buzz(h, 0.25f, 0.04f);
                        break;
                    }
                }
            }

            Vector3 local = Anchor.InverseTransformPoint(_resource.position);
            Vector3 flat = local - EvolutionSim.TerrainCentre; flat.y = 0f;
            float richness = 1f - Mathf.Clamp01(flat.magnitude / EvolutionSim.TerrainRadius);
            Sim.ResourceRichness01 = richness;
            _resourceMat.SetFloat("_Density", Mathf.Lerp(0.7f, 1.6f, richness));
        }

        /// <summary>Held, it speeds simulated time up to 6x; released, it drifts back to its rest
        /// spot and simulated time eases back to normal. It never changes an outcome, only how
        /// long the learner waits to see one.</summary>
        void HandleTimeStone(float dt)
        {
            bool grasped = false;
            if (Hands != null)
            {
                foreach (var h in new[] { Hands.Left, Hands.Right })
                {
                    if (h == null || !h.IsTracked || !h.IsGrasping) continue;
                    if ((PrismHands.PointOf(h) - _timeStone.position).sqrMagnitude > GrabRadius * GrabRadius) continue;
                    grasped = true;
                    _timeStone.position = PrismHands.PointOf(h);
                    break;
                }
            }

            if (!grasped)
                _timeStone.localPosition = Vector3.MoveTowards(_timeStone.localPosition, TimeStoneRestLocal, dt * 0.35f);

            _timeScale = Mathf.MoveTowards(_timeScale, grasped ? 6f : 1f, dt * (grasped ? 10f : 4f));
            Sim.TimeScale = _timeScale;
            _timeMat.SetFloat("_Density", Mathf.Lerp(0.65f, 1.5f, Mathf.InverseLerp(1f, 6f, _timeScale)));
        }

        // ---- evidence from the simulation, never the reverse ---------------

        void TrackBirthsAndDeaths()
        {
            long births = Sim.TotalBirths - _lastBirths;
            long deaths = Sim.TotalDeaths - _lastDeaths;
            if (births > 0) Loop.Evidence.Record(EvolutionEvidence.Birth, (int)births);
            if (deaths > 0) Loop.Evidence.Record(EvolutionEvidence.Death, (int)deaths);
            _lastBirths = Sim.TotalBirths;
            _lastDeaths = Sim.TotalDeaths;
        }

        /// <summary>
        /// The Explore -> Discover gate, made concrete: dwell at a temperature extreme long
        /// enough for the living population to actually answer, in each direction, and only then
        /// record it. This is read from EvolutionSim's own reported mean — nothing here decides
        /// the direction in advance, it only watches for the sign the simulation already produced.
        /// </summary>
        void TrackShiftEvidence(float dt)
        {
            float meanU = EvolutionSim.TraitToU01(Sim.MeanTrait);

            if (Sim.Temperature > 0.70f)
            {
                if (_hotDwell <= 0f) { _meanUAtHotStart = meanU; _warmShiftFired = false; }
                _hotDwell += dt;
                if (!_warmShiftFired && _hotDwell > DwellRequired && meanU < _meanUAtHotStart - ShiftUThreshold)
                {
                    _warmShiftFired = true;
                    Loop.Evidence.Record(EvolutionEvidence.ShiftWarm);
                    if (Loop.Stage >= LoopStage.Discover) Loop.Evidence.Record(EvolutionEvidence.ShiftWhileVisible);
                }
            }
            else _hotDwell = 0f;

            if (Sim.Temperature < 0.30f)
            {
                if (_coldDwell <= 0f) { _meanUAtColdStart = meanU; _coldShiftFired = false; }
                _coldDwell += dt;
                if (!_coldShiftFired && _coldDwell > DwellRequired && meanU > _meanUAtColdStart + ShiftUThreshold)
                {
                    _coldShiftFired = true;
                    Loop.Evidence.Record(EvolutionEvidence.ShiftCold);
                    if (Loop.Stage >= LoopStage.Discover) Loop.Evidence.Record(EvolutionEvidence.ShiftWhileVisible);
                }
            }
            else _coldDwell = 0f;
        }

        /// <summary>
        /// The one piece of text in this world, and it does not appear until Formalize — and even
        /// then, only while the learner's head is near the terrarium, so it fades when attention
        /// moves on rather than sitting on screen at rest.
        /// </summary>
        void UpdateLabel()
        {
            bool eligible = Loop.Stage >= LoopStage.Formalize;
            Vector3 centreWorld = Anchor.TransformPoint(EvolutionSim.TerrainCentre);
            bool near = eligible && Head != null && (Head.transform.position - centreWorld).sqrMagnitude < 0.85f * 0.85f;

            _label.Show(near);
            if (!near) return;

            string body = $"population {Sim.AliveCount}\nmean size {Sim.MeanTrait:0.00}";
            _label.SetText(body, PrismPalette.Spectral(EvolutionSim.TraitToU01(Sim.MeanTrait)));
            _label.PlaceAbove(centreWorld, 0.15f);
        }

        // -----------------------------------------------------------------
        // concepts
        // -----------------------------------------------------------------

        public override IEnumerable<ConceptSpec> Concepts => BuildConcepts();

        /// <summary>Azimuth measured clockwise from +Z, elevation as the resulting unit vector's
        /// own Y — matches the wedge the world was assigned: azimuth 216-252 deg, elevation within
        /// +/-0.5. Distances are set per-concept, within 3.2-5.5 m.</summary>
        static Vector3 Dir(float azimuthDeg, float elevationDeg)
        {
            float az = azimuthDeg * Mathf.Deg2Rad;
            float el = elevationDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        }

        static ConceptSpec[] BuildConcepts() => new[]
        {
            new ConceptSpec
            {
                Id = "evolution-engine", Title = "Evolution Engine",
                Domain = ConceptDomain.LifeAndEvolution, Kind = KnowledgeKind.System,
                Direction = Dir(234f, 0f), Distance = 3.6f,
                WorldId = "evolution-engine",
                Capability = "Shape a population's traits using only the environment; never touch an organism.",
                Formalisation = "A population's heritable trait distribution shifts across generations because " +
                                "individuals with different trait values do not survive and reproduce at equal " +
                                "rates in a given environment. The organisms do not change; which of them leave " +
                                "offspring does.",
                Links = new[]
                {
                    ("variation", Relation.Composes, 0.85f),
                    ("selection", Relation.Composes, 0.85f),
                    ("heritability", Relation.Composes, 0.80f),
                    ("feedback", Relation.Instantiates, 0.55f),
                }
            },
            new ConceptSpec
            {
                Id = "variation", Title = "Variation",
                Domain = ConceptDomain.LifeAndEvolution, Kind = KnowledgeKind.Fact,
                Direction = Dir(220f, 15f), Distance = 3.4f,
                Capability = "Recognise that individuals differ before anything has acted on them.",
                Formalisation = "Individuals in a population already differ in a heritable trait, before any " +
                                "environment has favoured one value over another. Selection has nothing to act " +
                                "on without this.",
                Links = new[]
                {
                    ("heritability", Relation.Prerequisite, 0.70f),
                }
            },
            new ConceptSpec
            {
                Id = "heritability", Title = "Heritability",
                Domain = ConceptDomain.LifeAndEvolution, Kind = KnowledgeKind.Fact,
                Direction = Dir(248f, -12f), Distance = 4.4f,
                Capability = "Tell how closely offspring trait values track a parent's, and how much scatters.",
                Formalisation = "The tendency of offspring to resemble a parent in a trait: an offspring's " +
                                "value scatters around the parent's by a small random amount, never copied " +
                                "exactly.",
                Links = new[]
                {
                    ("selection", Relation.Constrains, 0.75f),
                }
            },
            new ConceptSpec
            {
                Id = "selection", Title = "Selection",
                Domain = ConceptDomain.LifeAndEvolution, Kind = KnowledgeKind.Process,
                Direction = Dir(228f, 9f), Distance = 4.0f,
                Capability = "Predict which way a trait distribution moves once you know what the " +
                             "environment rewards with offspring.",
                Formalisation = "Whichever trait values leave more surviving offspring in the current " +
                                "environment become more common in the next generation. A filter on variation " +
                                "that already exists, not a force that improves an individual.",
                Links = new[]
                {
                    ("probability", Relation.Composes, 0.75f),
                }
            },
            new ConceptSpec
            {
                Id = "fitness", Title = "Fitness",
                Domain = ConceptDomain.LifeAndEvolution, Kind = KnowledgeKind.Fact,
                Direction = Dir(244f, -19f), Distance = 5.1f,
                Capability = "Read a fitness number as an offspring count in this environment, never as a " +
                             "merit score.",
                Formalisation = "The expected number of offspring a trait value leaves in a specific " +
                                "environment. A property of the trait-environment pairing, not of the " +
                                "organism alone, and it changes the moment the environment does.",
                Links = new[]
                {
                    ("selection", Relation.Measures, 0.80f),
                }
            },
        };
    }
}
