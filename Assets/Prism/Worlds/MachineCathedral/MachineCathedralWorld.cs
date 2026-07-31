using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using Prism.Worlds;
using UnityEngine;

namespace Prism.Worlds.MachineCathedral
{
    /// <summary>
    /// Machine Cathedral: climb the ladder from a lever to a logic gate and find it was one idea
    /// the whole way.
    ///
    /// Three stations sit side by side, each a completely different-looking machine, each reducible
    /// to exactly the same one-line trade (see <see cref="TradeRig"/>): move the fulcrum on a beam,
    /// swap a gear, change how many strands a rope runs through, and in every case the SAME product
    /// — effort times its distance, against load times its distance — refuses to change. A learner
    /// who plays with all three before being told anything has already found the lesson; Formalize
    /// only supplies the name.
    ///
    ///   Wonder     Turn the crank. Something heavy rises at the far end. Nothing is labelled, and
    ///              two other machines are sitting right there, equally unexplained.
    ///   Explore    The fulcrum slides, the driven gear swaps, the rope re-routes through more
    ///              pegs — all by hand, on all three machines, forever.
    ///   Discover   Gated on having personally made both a force-multiplying and a
    ///              distance-multiplying configuration, on any machine, with real motion under it —
    ///              the straddle, same evidentiary shape as the orbital world's bound/unbound gate.
    ///              Two glowing lengths appear: work put in, work delivered. They stay together.
    ///   Formalize  Mechanical advantage, torque and the work balance get their names, spatially,
    ///              beside whichever machine is in the learner's hands.
    ///   Apply      A load too heavy for the current gear, within a fixed force budget. Only a
    ///              bigger driven gear reaches the mark.
    ///   Explain    Predict a gear train's output speed by setting a bead on a track BEFORE turning
    ///              the crank, then turn it and find out.
    ///   Create     A pulley load with a specification: reach the marked height, within the same
    ///              force budget, by choosing enough supporting strands.
    ///   Connect    The ladder upward: two hand-thrown switches, a relay thrown by one of them
    ///              instead of a hand, and a lamp that is an honest AND or OR gate depending on how
    ///              the switches are wired. The same TradeRig runs all of it.
    /// </summary>
    public class MachineCathedralWorld : PrismWorldBase
    {
        public override string WorldId => "machine-cathedral";

        /// <summary>A workshop at night. Lamplight, stone and iron.</summary>
        protected override WorldShellSpec Shell => WorldShellSpec.Workshop;
        public override string PrimaryConceptId => "machine-cathedral";
        public override string DisplayName => "Machine Cathedral";

        public LeverStation Lever { get; private set; }
        public GearStation Gear { get; private set; }
        public PulleyStation Pulley { get; private set; }

        LogicBench _logic;
        MachineChallenges _challenges;
        bool _logicShown;

        // ---- the Discover reveal: work in and work out, as two lengths that always match ----
        Transform _workInBar, _workOutBar;
        Material _workInMat, _workOutMat;
        static readonly Vector3 WorkBarBase = new Vector3(0f, 0.15f, -0.06f);
        const float WorkBarScale = 0.55f;      // metres of bar per joule-equivalent of work
        const float WorkBarMaxHeight = 0.15f;

        // ---- the Formalize reveal: the mechanical advantage gets a name, beside whichever
        // machine is currently in the learner's hands. The one piece of text in this world,
        // exactly as sparingly used as the orbital world's single ConicLabel. ----
        PrismLabel _maLabel;

        // -----------------------------------------------------------------

        protected override void BuildWorld()
        {
            // A workbench close enough that three machines side by side all stay in reach.
            Reach = 0.48f;
            EyeToTable = 0.46f;

            Lever = new LeverStation(this);
            Lever.Build(Anchor, new Vector3(-0.21f, 0f, -0.03f));
            Lever.SetLoadMass(0.15f);

            Gear = new GearStation(this);
            Gear.Build(Anchor, new Vector3(0f, 0f, 0.02f));
            Gear.SetLoadMass(0.10f);

            Pulley = new PulleyStation(this);
            Pulley.Build(Anchor, new Vector3(0.22f, 0f, -0.02f));
            Pulley.SetLoadMass(0.10f);

            _logic = new LogicBench(this);
            _logic.Build(Anchor, new Vector3(0f, 0.20f, 0.03f));
            _logic.Show(false);

            _challenges = gameObject.AddComponent<MachineChallenges>();
            _challenges.World = this;

            BuildWorkBars();
            _maLabel = PrismLabel.Create("MechanicalAdvantageLabel", Anchor, Head, 0.014f);
        }

        /// <summary>
        /// Discover's reveal, promised by the design brief in exactly these words: "work in and
        /// work out, as two lengths that always match." Two thin bars, pinned at a shared base,
        /// grow from the live TradeRig of whichever station the learner most recently drove. They
        /// are hidden before Discover and appear the instant it is entered, at which point the
        /// learner has already produced the evidence that makes the reveal legible: they have felt
        /// both a light push and a heavy one, and now they can see why those were the same fact.
        /// </summary>
        void BuildWorkBars()
        {
            var unitBar = MachineMesh.Box(Vector3.one);

            _workInMat = PrismMaterials.CeramicBody(PrismPalette.Cyan, 0.5f);
            var inGo = new GameObject("WorkInBar");
            inGo.transform.SetParent(Anchor, false);
            inGo.AddComponent<MeshFilter>().sharedMesh = unitBar;
            inGo.AddComponent<MeshRenderer>().sharedMaterial = _workInMat;
            _workInBar = inGo.transform;

            _workOutMat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.5f);
            var outGo = new GameObject("WorkOutBar");
            outGo.transform.SetParent(Anchor, false);
            outGo.AddComponent<MeshFilter>().sharedMesh = unitBar;
            outGo.AddComponent<MeshRenderer>().sharedMaterial = _workOutMat;
            _workOutBar = outGo.transform;

            _workInBar.gameObject.SetActive(false);
            _workOutBar.gameObject.SetActive(false);
        }

        /// <summary>Whichever station's handle is moving fastest right now — the one the learner is
        /// actually driving. Defaults to the gear station when everything is at rest.</summary>
        TradeRig ActiveRig()
        {
            var best = Gear.Rig;
            float bestSpeed = Mathf.Abs(best.QDot);
            if (Mathf.Abs(Lever.Rig.QDot) > bestSpeed) { best = Lever.Rig; bestSpeed = Mathf.Abs(best.QDot); }
            if (Mathf.Abs(Pulley.Rig.QDot) > bestSpeed) { best = Pulley.Rig; }
            return best;
        }

        void UpdateWorkBars()
        {
            bool show = Loop.Stage >= LoopStage.Discover;
            if (_workInBar.gameObject.activeSelf != show)
            {
                _workInBar.gameObject.SetActive(show);
                _workOutBar.gameObject.SetActive(show);
            }
            if (!show) return;

            var rig = ActiveRig();
            float hIn = Mathf.Clamp(Mathf.Abs(rig.WorkIn) * WorkBarScale, 0.002f, WorkBarMaxHeight);
            float hOut = Mathf.Clamp(Mathf.Abs(rig.WorkOut) * WorkBarScale, 0.002f, WorkBarMaxHeight);

            PoseBar(_workInBar, WorkBarBase + Vector3.left * 0.018f, hIn);
            PoseBar(_workOutBar, WorkBarBase + Vector3.right * 0.018f, hOut);
        }

        static void PoseBar(Transform bar, Vector3 basePos, float height)
        {
            bar.localScale = new Vector3(0.008f, height, 0.008f);
            bar.localPosition = basePos + Vector3.up * (height * 0.5f);
        }

        /// <summary>
        /// Formalize's naming moment, spatial rather than a panel: the ratio is printed beside
        /// whichever machine is actually moving, plain language first ("mechanical advantage"
        /// before "MA"), the same way the orbital world names a conic beside the moon still
        /// tracing it.
        /// </summary>
        void UpdateFormalizeLabel()
        {
            if (Loop.Stage < LoopStage.Formalize) { _maLabel.Show(false); return; }

            float speedLever = Mathf.Abs(Lever.Rig.QDot);
            float speedGear = Mathf.Abs(Gear.Rig.QDot);
            float speedPulley = Mathf.Abs(Pulley.Rig.QDot);

            float ratio;
            Vector3 pos;
            if (speedGear >= speedLever && speedGear >= speedPulley)
            {
                ratio = Gear.DisplayRatio; pos = Gear.CrankHandlePosition;
            }
            else if (speedLever >= speedPulley)
            {
                ratio = Lever.DisplayRatio; pos = Lever.HandlePosition;
            }
            else
            {
                ratio = Pulley.DisplayRatio; pos = Pulley.HandlePosition;
            }

            string body = "mechanical advantage " + ratio.ToString("0.00") +
                          "\neffort x distance = load x distance";
            _maLabel.SetText(body, PrismPalette.Spectral(Mathf.Clamp01(ratio / 4f)));
            _maLabel.PlaceAbove(pos, 0.045f);
            _maLabel.Show(true);
        }

        // -----------------------------------------------------------------
        // the loop
        // -----------------------------------------------------------------

        protected override void ConfigureLoop(LearningLoop loop)
        {
            loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet turned, pushed or pulled anything",
                e => e.Count(MachineEvidence.Turn) >= 1));

            // The gate that matters most, in the same shape as the orbital world's bound/unbound
            // gate: wait for evidence that the learner has personally straddled the boundary, on
            // either side, before showing them there is a boundary at all.
            loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet made both a force-multiplying and a distance-multiplying trade",
                e => e.Count(MachineEvidence.Turn) >= 6
                  && e.Has(MachineEvidence.ForceMultiplying)
                  && e.Has(MachineEvidence.DistanceMultiplying)));

            loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet tried the lever, the gears and the pulley",
                e => e.Has(MachineEvidence.StationLever)
                  && e.Has(MachineEvidence.StationGear)
                  && e.Has(MachineEvidence.StationPulley)));

            loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet watched the mechanical advantage answer their own hand",
                e => e.Count(MachineEvidence.FormalizeObserve) >= 3));

            loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet reached the marked height inside the force budget",
                e => e.Has(MachineEvidence.ApplySuccess)));

            loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet predicted a gear train's speed correctly",
                e => e.Has(MachineEvidence.ExplainCorrect)));

            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet assembled a pulley system to the specification",
                e => e.Has(MachineEvidence.CreateSuccess)));

            // Deliberately added, where the orbital reference leaves Create ungated: the contract
            // this world was built against asks for all eight stages reachable by evidence, and
            // "connect this to everything else" has a literal, buildable answer here (switching
            // becomes logic), so it is worth gating on rather than leaving as a label nobody reaches.
            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet built both an AND and an OR out of the same two switches",
                e => e.Has(MachineEvidence.LogicAndSeen) && e.Has(MachineEvidence.LogicOrSeen)));
        }

        protected override void OnStageEntered(LoopStage stage)
        {
            base.OnStageEntered(stage);
            switch (stage)
            {
                case LoopStage.Apply:
                    _challenges.BeginApply();
                    break;
                case LoopStage.Explain:
                    _challenges.BeginExplain();
                    break;
                case LoopStage.Create:
                    _challenges.BeginCreate();
                    break;
                case LoopStage.Connect:
                    _logic.Show(true);
                    _logicShown = true;
                    Knowledge?.Confirm(PrimaryConceptId, 0.4f);
                    Debug.Log("[PRISM] Machine Cathedral: Connect reached. Switching becomes logic.");
                    break;
            }
        }

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        protected override void Tick(float dt)
        {
            Lever.Tick(dt);
            Gear.Tick(dt);
            Pulley.Tick(dt);
            _challenges.Evaluate(dt);
            UpdateWorkBars();
            UpdateFormalizeLabel();

            if (_logicShown) _logic.Tick(dt);
        }

        /// <summary>Shared by every station: the learner turned, pushed or pulled something with
        /// real motion, at the ratio it happened to be set to right now.</summary>
        public void RecordDrive(float displayRatio, string stationEvidenceKey)
        {
            var e = Loop.Evidence;
            e.Record(MachineEvidence.Turn);
            e.Record(stationEvidenceKey);
            e.ObserveBest("best.ratio.max", displayRatio, lowerIsBetter: false);
            e.ObserveBest("best.ratio.min", displayRatio, lowerIsBetter: true);

            if (displayRatio >= 1.5f) e.Record(MachineEvidence.ForceMultiplying);
            else if (displayRatio <= 0.667f) e.Record(MachineEvidence.DistanceMultiplying);

            if (Loop.Stage >= LoopStage.Formalize) e.Record(MachineEvidence.FormalizeObserve);
        }

        /// <summary>
        /// Nearest hand that is both tracked and actively grasping, within range. Every station's
        /// grab logic funnels through this one method rather than the base class's NearestHand,
        /// which does not check IsGrasping and would let an open hand "hold" a handle just by
        /// passing near it.
        ///
        /// Written without the array-literal foreach the base class helpers use, because this is
        /// called several times per station per frame across three stations plus the logic bench —
        /// the one place in this world where that pattern was worth avoiding outright rather than
        /// following the existing convention.
        /// </summary>
        public PrismHands.Hand NearestGraspingHand(Vector3 point, float radius)
        {
            if (Hands == null) return null;
            float bestD = radius * radius;
            PrismHands.Hand best = null;

            var l = Hands.Left;
            if (l != null && l.IsTracked && l.IsGrasping)
            {
                float d = (PrismHands.PointOf(l) - point).sqrMagnitude;
                if (d < bestD) { bestD = d; best = l; }
            }
            var r = Hands.Right;
            if (r != null && r.IsTracked && r.IsGrasping)
            {
                float d = (PrismHands.PointOf(r) - point).sqrMagnitude;
                if (d < bestD) { bestD = d; best = r; }
            }
            return best;
        }

        // -----------------------------------------------------------------
        // concepts
        // -----------------------------------------------------------------

        public override IEnumerable<ConceptSpec> Concepts => new[]
        {
            new ConceptSpec
            {
                Id = "machine-cathedral",
                Title = "Machine Cathedral",
                Domain = ConceptDomain.Machines,
                Kind = KnowledgeKind.System,
                Direction = new Vector3(-0.809f, 0.05f, 0.588f),   // azimuth ~306 deg
                Distance = 3.6f,
                WorldId = "machine-cathedral",
                Capability = "Turn a small effort into a large force, or a small motion into a " +
                             "large one -- never both -- and say which trade a lever, a gear train " +
                             "or a pulley just made.",
                Formalisation = "Every machine here trades one quantity for its reciprocal through " +
                                 "a single ratio: effort times its distance equals load times its " +
                                 "distance. Nothing in this cathedral is exempt.",
                Links = new (string, Relation, float)[]
                {
                    ("mechanical-advantage", Relation.Composes, 0.9f),
                    ("torque", Relation.Composes, 0.85f),
                    ("logic-gates", Relation.Composes, 0.8f),
                }
            },
            new ConceptSpec
            {
                Id = "mechanical-advantage",
                Title = "Mechanical Advantage",
                Domain = ConceptDomain.Machines,
                Kind = KnowledgeKind.Equation,
                Direction = new Vector3(-0.899f, 0.15f, 0.438f),   // azimuth ~296 deg
                Distance = 4.0f,
                Capability = "Predict whether a configuration will make something easier to move " +
                             "or faster to move, before touching it.",
                Formalisation = "MA = load force / effort force = effort distance / load distance. " +
                                 "The two ratios are the same number, which is why a machine can " +
                                 "multiply force or multiply speed but never both at once.",
                Links = new (string, Relation, float)[]
                {
                    ("energy-conservation", Relation.Instantiates, 0.85f),
                    ("torque", Relation.Composes, 0.7f),
                    ("logic-gates", Relation.Prerequisite, 0.6f),
                }
            },
            new ConceptSpec
            {
                Id = "torque",
                Title = "Torque",
                Domain = ConceptDomain.MatterAndEnergy,
                Kind = KnowledgeKind.Theory,
                Direction = new Vector3(-0.743f, -0.10f, 0.669f),  // azimuth ~312 deg
                Distance = 4.3f,
                Capability = "Read a lever arm and a force together as the one quantity that " +
                             "actually turns something.",
                Formalisation = "Torque = force times the perpendicular distance from the pivot. " +
                                 "Doubling the arm halves the force needed for the same turning " +
                                 "effect.",
                Links = new (string, Relation, float)[]
                {
                    ("angular-momentum", Relation.Causes, 0.7f),
                }
            },
            new ConceptSpec
            {
                Id = "logic-gates",
                Title = "Logic Gates",
                Domain = ConceptDomain.Machines,
                Kind = KnowledgeKind.System,
                Direction = new Vector3(-0.643f, 0.20f, 0.766f),   // azimuth ~320 deg
                Distance = 4.8f,
                Capability = "Wire two switches in series or in parallel and predict which one " +
                             "just became an AND and which became an OR.",
                Formalisation = "A closed switch is a 1, an open one a 0. Series conducts only if " +
                                 "every switch is closed (AND); parallel conducts if any switch is " +
                                 "closed (OR). Computation is a shape made of switches.",
                Links = new (string, Relation, float)[]
                {
                    ("feedback", Relation.Prerequisite, 0.55f),
                }
            },
        };
    }
}
