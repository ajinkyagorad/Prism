using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using Prism.Worlds;
using UnityEngine;

namespace Prism.Worlds.AlgorithmCity
{
    /// <summary>Evidence keys. Named constants because the gates and the world must agree exactly.</summary>
    public static class AlgorithmCityEvidence
    {
        public const string Touch = "touch";
        public const string SwapManual = "swap.manual";
        public const string CostRatio = "cost.ratio";
        public const string RunComplete = "run.complete";
        public const string RunSmallN = "run.smallN";
        public const string RunLargeN = "run.largeN";
        public const string ChallengeDone = "challenge.done";
        public const string PredictionGood = "prediction.good";
        public const string PipelineHybrid = "pipeline.hybrid";
    }

    /// <summary>
    /// Algorithm City: walk through a running computation and redirect it with your hands.
    ///
    /// SPINE: sorting. Two structures stand side by side over the same short reach and are always
    /// fed the SAME shuffled batch at once — the direct comparison the Discover gate waits for.
    ///
    ///   Insertion   a single flat row. A new element walks backward through what is already
    ///               settled, swapping one adjacent pair at a time, until it is seated. Its column
    ///               is always its array index; a swap makes two beads visibly CROSS rather than
    ///               two colours trading places instantly.
    ///   Merge       a rising cascade. Bottom-up, no recursion in the implementation, but the same
    ///               algorithm and the same comparison count as the textbook top-down version: runs
    ///               of size 1 merge into runs of 2, then 4, then 8, each finished window rising a
    ///               row. Column stays fixed; only height changes, so "more merged" reads as
    ///               "higher up" without any label.
    ///
    /// Both are driven one honest comparison at a time by <see cref="SortStructure"/> /
    /// <see cref="ISortProcess"/> — see SortingSim.cs for the run rules and the note on why the
    /// implementation is bottom-up. Every number this world shows (spire height, curve point,
    /// label text) is read directly off <c>Comparisons</c>/<c>Moves</c> counted during real
    /// execution; nothing is drawn from n^2 or n*log2(n) as a formula.
    ///
    /// COLOUR LAW (stated once, see also AlgorithmCityBead.shader and SortStructure):
    ///   - a DATUM's hue is its VALUE, mapped onto the spectral ramp. Sortedness is therefore a
    ///     smooth colour gradient appearing out of noise, on both structures, all the time.
    ///   - a STRUCTURE's identity colour (its spire, its growth curve, its label) is fixed: Coral
    ///     for Insertion, Cyan for Merge. This never touches a datum.
    ///   - Gold marks anything a learner takes hold of to steer the world: slider handles, the
    ///     pipeline divider, the prediction marker. Never a datum, never a structure's identity.
    ///
    ///   Wonder     Only the Insertion row exists, already running an ambient demo on a small
    ///              batch, on a loop, before anyone touches anything. No spawner, no Merge, no
    ///              spires, nothing to read. Reach in and take hold of one bead: that is all it
    ///              takes to move on.
    ///   Explore    Merge appears alongside it, the spawner and the physical controls arrive (block
    ///              a path, slow a clock, an input-size rail), and every run from here is the
    ///              learner's own, fed identically to both structures. Cost is visible the whole
    ///              time, as a spire that physically grows with every comparison.
    ///   Discover   Reached only once a learner has personally seen the two spires end up at
    ///              visibly different heights on the same data (evidence: the best comparison-count
    ///              RATIO observed, gated at 1.4x). The growth-curve panel appears, already showing
    ///              every sample from Explore.
    ///   Formalize  The two structures get names and their live comparison counts as text, only now.
    ///   Apply      Keep Merge's spire under a comparison budget, drawn as a ring at the exact
    ///              height that many comparisons reaches.
    ///   Explain    Place a marker on the structure you think will win; the next run is the race.
    ///   Create     A third, learner-configured track: split the batch at a hand-placed divider,
    ///              Insertion left of it, Merge right of it, one real merge to honestly combine them.
    ///   Connect    Return to the atrium; the constellation has changed.
    /// </summary>
    public class AlgorithmCityWorld : PrismWorldBase
    {
        public override string WorldId => "algorithm-city";

        /// <summary>A lit grid at night: the city an algorithm walks through.</summary>
        protected override WorldShellSpec Shell => WorldShellSpec.Datascape;
        public override string PrimaryConceptId => "algorithm-city";
        public override string DisplayName => "Algorithm City";

        public override string[] Shaders => new[] { BeadShaderName };

        const string BeadShaderName = "Prism/AlgorithmCityBead";

        // ---- tuning --------------------------------------------------------
        public const int MinN = 4;
        public const int MaxN = 32;
        const int WonderN = 7;
        const int SmallNThreshold = 8;
        const int LargeNThreshold = 28;
        public const float BeadFollowPerSecond = 6f;
        const float SpireHeightScale = 0.011f;

        const float TrackWidth = 0.30f;
        const float RowStep = 0.030f;

        const float GrabRadius = 0.045f;
        const float SwapRadius = 0.035f;
        const float SliderGrabRadius = 0.035f;
        const float GateTapRadius = 0.032f;
        const float SpawnerTapRadius = 0.05f;

        // Local-space layout. Depth (Z) is kept shallow deliberately: with Reach/EyeToTable set
        // below, the diagonal distance from the eye to the ANCHOR ORIGIN alone is already close to
        // arm's length, matching OrbitalWorld's own baseline (its cradle moons sit roughly 0.74 m
        // from the eye). Every extra centimetre of local Z here is a centimetre added on top of
        // that, so the farthest content (the Create-stage bench) is kept within about 0.76 m —
        // comparable to, not beyond, the reference world's own reach.
        static readonly Vector3 InsertionOrigin = new Vector3(0f, 0.14f, -0.09f);
        static readonly Vector3 MergeOrigin = new Vector3(0f, 0.14f, 0.07f);
        static readonly Vector3 BenchOrigin = new Vector3(0f, 0.14f, 0.15f);
        static readonly Vector3 SpawnerLocalPos = new Vector3(0f, 0.11f, -0.20f);
        static readonly Vector3 InsertionGatePos = new Vector3(-0.175f, 0.14f, -0.09f);
        static readonly Vector3 MergeGatePos = new Vector3(-0.175f, 0.14f, 0.07f);
        static readonly Vector3 InsertionSliderOrigin = new Vector3(0f, 0.075f, -0.09f);
        static readonly Vector3 MergeSliderOrigin = new Vector3(0f, 0.075f, 0.07f);
        static readonly Vector3 ScaleRailOrigin = new Vector3(0f, 0.045f, 0.0f);
        static readonly Vector3 DividerRailOrigin = new Vector3(0f, 0.075f, 0.15f);
        static readonly Vector3 InsertionSpireBase = new Vector3(0.185f, 0.14f, -0.09f);
        static readonly Vector3 MergeSpireBase = new Vector3(0.185f, 0.14f, 0.07f);
        static readonly Vector3 CurveOrigin = new Vector3(0f, 0.28f, 0.0f);
        static readonly Vector3 PredictParkPos = new Vector3(0.0f, 0.20f, -0.20f);

        // ---- structures ------------------------------------------------------
        SortStructure _insertion, _merge;
        SortStructure[] _grabStructures;
        public SortStructure Insertion => _insertion;
        public SortStructure Merge => _merge;
        public Transform CityAnchor => Anchor;
        public Vector3 SpawnerLocal => SpawnerLocalPos;
        public Vector3 InsertionCenterLocal => InsertionOrigin;
        public Vector3 MergeCenterLocal => MergeOrigin + Vector3.up * (RowStep * 2.5f);

        Transform _spawnerView;
        Material _spawnerMat, _beadMat;

        GateToken _insertionGate, _mergeGate;
        GateToken[] _allGates;

        SliderControl _insertionSpeedSlider, _mergeSpeedSlider, _scaleRail;
        SliderControl[] _allSliders;
        int _pendingN = 12;
        public int PendingN => _pendingN;

        PrismLabel _insertionLabel, _mergeLabel;
        int _lastInsLabelCount = -1, _lastMrgLabelCount = -1;
        bool _labelsUnlocked;

        AlgorithmCityChallenge _challenge;

        readonly int[] _scratch = new int[MaxN];

        struct BeadGrab { public SortStructure Structure; public int Index; }
        BeadGrab? _heldL, _heldR;

        bool _pairPending, _pairInsDone, _pairMrgDone, _pairRacing;
        int _pairN;
        float _pairInsTime, _pairMrgTime;
        float _autonomousTimer;
        float _spawnCooldown;
        LoopStage _lastSyncedStage = (LoopStage)(-1);

        // -----------------------------------------------------------------
        // build
        // -----------------------------------------------------------------

        protected override void BuildWorld()
        {
            // Slightly closer and slightly higher than the base defaults (0.60 / 0.50): this world
            // spreads two structures side by side plus a third at Create, so the base anchor is
            // pulled in to keep the farthest content within comfortable reach. See the layout
            // comment above the Origin constants for the reasoning.
            Reach = 0.52f;
            EyeToTable = 0.46f;

            _beadMat = PrismMaterials.New(BeadShaderName);
            var insSpireMat = PrismMaterials.CeramicBody(PrismPalette.Coral, 0.42f);
            var mrgSpireMat = PrismMaterials.CeramicBody(PrismPalette.Cyan, 0.42f);
            var railMat = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.16f);
            var handleMat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.5f);

            _insertion = new SortStructure("Insertion", Anchor, _beadMat, MaxN,
                InsertionOrigin, TrackWidth, false, 0f, vals => new InsertionSortProcess(vals));
            _merge = new SortStructure("Merge", Anchor, _beadMat, MaxN,
                MergeOrigin, TrackWidth, true, RowStep, vals => new MergeSortProcess(vals));
            _grabStructures = new[] { _insertion, _merge };

            _insertion.BuildSpire(InsertionSpireBase, insSpireMat, SpireHeightScale);
            _merge.BuildSpire(MergeSpireBase, mrgSpireMat, SpireHeightScale);

            BuildSpawner();

            _insertionGate = new GateToken("InsertionGate", Anchor, InsertionGatePos,
                PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.22f));
            _mergeGate = new GateToken("MergeGate", Anchor, MergeGatePos,
                PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.22f));
            _allGates = new[] { _insertionGate, _mergeGate };

            float speedInitT = Mathf.InverseLerp(0.15f, 4f, 1f);
            _insertionSpeedSlider = new SliderControl("InsertionSpeed", Anchor, InsertionSliderOrigin,
                Vector3.right, 0.12f, speedInitT, railMat, handleMat);
            _mergeSpeedSlider = new SliderControl("MergeSpeed", Anchor, MergeSliderOrigin,
                Vector3.right, 0.12f, speedInitT, railMat, handleMat);
            _scaleRail = new SliderControl("ScaleRail", Anchor, ScaleRailOrigin,
                Vector3.right, 0.15f, Mathf.InverseLerp(MinN, MaxN, _pendingN), railMat, handleMat);
            _allSliders = new[] { _insertionSpeedSlider, _mergeSpeedSlider, _scaleRail };

            _insertionLabel = PrismLabel.Create("InsertionLabel", Anchor, Head, 0.014f);
            _mergeLabel = PrismLabel.Create("MergeLabel", Anchor, Head, 0.014f);

            _challenge = gameObject.AddComponent<AlgorithmCityChallenge>();
            _challenge.World = this;
            _challenge.BuildCurvePanel(Anchor, CurveOrigin, 0.22f, 0.14f, MaxN, insSpireMat, mrgSpireMat);
            _challenge.BuildBudgetRing(Anchor, PrismMaterials.CeramicBody(PrismPalette.Cyan, 0.55f));
            _challenge.BuildPredictionMarker(Anchor, PredictParkPos,
                PrismMaterials.CeramicBody(PrismPalette.Gold, 0.55f));
            _challenge.BuildBench(Anchor, _beadMat, BenchOrigin, TrackWidth, DividerRailOrigin,
                railMat, handleMat, MaxN);

            // The world begins already computing: an ambient demo runs on Insertion alone, on a
            // small batch, before the learner has touched anything. See TickAutonomous.
            _insertion.BeginRun(ShuffledPermutation(WonderN), SpawnerLocalPos);

            // Bring everything else to whatever state the loop is ALREADY in — matters for a
            // returning learner whose saved stage is past Wonder, since LearningLoop's constructor
            // restores that stage directly rather than firing StageEntered for it.
            SyncRevealState(Loop.Stage);
            _lastSyncedStage = Loop.Stage;
        }

        void BuildSpawner()
        {
            var go = new GameObject("Spawner");
            go.transform.SetParent(Anchor, false);
            go.transform.localPosition = SpawnerLocalPos;
            go.transform.localScale = Vector3.one * 0.020f;
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            _spawnerMat = PrismMaterials.New(PrismMaterials.Seed);
            _spawnerMat.SetColor("_Tint", PrismPalette.Lavender);
            _spawnerMat.SetFloat("_Growth", 0.15f);
            _spawnerMat.SetFloat("_Density", 1.0f);
            go.AddComponent<MeshRenderer>().sharedMaterial = _spawnerMat;
            _spawnerView = go.transform;
        }

        // -----------------------------------------------------------------
        // loop
        // -----------------------------------------------------------------

        protected override void ConfigureLoop(LearningLoop loop)
        {
            loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet reached into the flowing line and taken hold of anything",
                e => e.Count(AlgorithmCityEvidence.Touch) >= 1));

            loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet seen the two structures cost visibly different amounts on the same data",
                e => e.Value(AlgorithmCityEvidence.CostRatio) >= 1.4f));

            loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet run both a small batch and a large one all the way through",
                e => e.Has(AlgorithmCityEvidence.RunSmallN)
                  && e.Has(AlgorithmCityEvidence.RunLargeN)
                  && e.Count(AlgorithmCityEvidence.RunComplete) >= 5));

            loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet run the city again now that the two structures have names",
                e => e.Count(AlgorithmCityEvidence.RunComplete) >= 7));

            loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet kept a structure under its comparison budget",
                e => e.Has(AlgorithmCityEvidence.ChallengeDone)));

            loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet predicted a winner correctly before a race finished",
                e => e.Has(AlgorithmCityEvidence.PredictionGood)));

            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet run a batch through a pipeline of their own that used both structures",
                e => e.Has(AlgorithmCityEvidence.PipelineHybrid)));
        }

        protected override void OnStageEntered(LoopStage stage)
        {
            base.OnStageEntered(stage);   // tells the companion
            _challenge?.OnStageEntered(stage);
            if (stage == LoopStage.Connect)
            {
                Knowledge?.Confirm(PrimaryConceptId, 0.4f);
                Debug.Log("[PRISM] Algorithm City: Connect reached. The constellation changes.");
            }
        }

        void SyncRevealState(LoopStage stage)
        {
            bool exploreOn = stage >= LoopStage.Explore;
            _merge.Beads.View.gameObject.SetActive(exploreOn);
            _merge.SpireView.gameObject.SetActive(exploreOn);
            _insertion.SpireView.gameObject.SetActive(exploreOn);
            _spawnerView.gameObject.SetActive(exploreOn);
            foreach (var g in _allGates) g.SetActive(exploreOn);
            foreach (var s in _allSliders) s.SetActive(exploreOn);

            _labelsUnlocked = stage >= LoopStage.Formalize;

            _challenge?.SyncRevealState(stage);
        }

        public void SnapScaleRail(int n)
        {
            n = Mathf.Clamp(n, MinN, MaxN);
            _scaleRail.SetT(Mathf.InverseLerp(MinN, MaxN, n));
            _pendingN = n;
        }

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        protected override void Tick(float dt)
        {
            if (Loop.Stage != _lastSyncedStage)
            {
                SyncRevealState(Loop.Stage);
                _lastSyncedStage = Loop.Stage;
            }

            if (Loop.Stage == LoopStage.Wonder) TickAutonomous(dt);

            _insertion.GateBlocked = _insertionGate.Blocked;
            _merge.GateBlocked = _mergeGate.Blocked;

            _insertion.Tick(dt, BeadFollowPerSecond);
            _merge.Tick(dt, BeadFollowPerSecond);
            ConsumeFinishedRuns();

            if (_insertion.Beads.View.gameObject.activeInHierarchy) _insertion.Beads.Rebuild();
            if (_merge.Beads.View.gameObject.activeInHierarchy) _merge.Beads.Rebuild();

            TickHands();
            UpdateLabels();

            _spawnCooldown = Mathf.Max(0f, _spawnCooldown - dt);
            _challenge.Tick(dt);
        }

        void TickAutonomous(float dt)
        {
            if (_insertion.RunActive) return;
            _autonomousTimer += dt;
            if (_autonomousTimer < 1.6f) return;
            _autonomousTimer = 0f;
            _insertion.BeginRun(ShuffledPermutation(WonderN), SpawnerLocalPos);
        }

        void ConsumeFinishedRuns()
        {
            if (_insertion.JustFinished)
            {
                _insertion.JustFinished = false;
                if (_pairPending) { _pairInsDone = true; _pairInsTime = _insertion.RunElapsedSeconds; }
            }
            if (_merge.JustFinished)
            {
                _merge.JustFinished = false;
                if (_pairPending) { _pairMrgDone = true; _pairMrgTime = _merge.RunElapsedSeconds; }
            }
            if (_pairPending && _pairInsDone && _pairMrgDone)
            {
                _pairPending = false;
                OnPairComplete();
            }
        }

        void OnPairComplete()
        {
            int ic = _insertion.Process.Comparisons;
            int mc = _merge.Process.Comparisons;
            float ratio = (float)ic / Mathf.Max(1, mc);

            Loop.Evidence.ObserveBest(AlgorithmCityEvidence.CostRatio, ratio, lowerIsBetter: false);
            Loop.Evidence.Record(AlgorithmCityEvidence.RunComplete);
            if (_pairN <= SmallNThreshold) Loop.Evidence.Record(AlgorithmCityEvidence.RunSmallN);
            if (_pairN >= LargeNThreshold) Loop.Evidence.Record(AlgorithmCityEvidence.RunLargeN);

            _challenge.OnPairComplete(_pairN, ic, mc);
            if (_pairRacing) _challenge.EvaluatePrediction(_pairInsTime <= _pairMrgTime);
        }

        void UpdateLabels()
        {
            if (!_labelsUnlocked)
            {
                _insertionLabel.Show(false);
                _mergeLabel.Show(false);
                return;
            }

            if (_insertion.Process != null)
            {
                // The string is only rebuilt when the count actually changed, not every frame --
                // PrismLabel.SetText also short-circuits on an unchanged string, but there is no
                // reason to concatenate a new one every frame just to throw it away unread.
                int c = _insertion.Process.Comparisons;
                if (c != _lastInsLabelCount)
                {
                    _lastInsLabelCount = c;
                    // The name and the notation arrive together, only now -- and only beside a
                    // curve the learner already measured, which is the claim's actual evidence.
                    _insertionLabel.SetText("Insertion sort - O(n^2)\n" + c + " comparisons",
                        PrismPalette.Coral);
                }
                Vector3 p = Anchor.TransformPoint(InsertionOrigin);
                _insertionLabel.PlaceAbove(p, 0.05f);
                _insertionLabel.Show(_insertion.RunActive || NearestHand(p, 0.28f) != null);
            }

            if (_merge.Process != null)
            {
                int c = _merge.Process.Comparisons;
                if (c != _lastMrgLabelCount)
                {
                    _lastMrgLabelCount = c;
                    _mergeLabel.SetText("Merge sort - O(n log n)\n" + c + " comparisons",
                        PrismPalette.Cyan);
                }
                Vector3 p = Anchor.TransformPoint(MergeOrigin) + Vector3.up * (RowStep * 5f);
                _mergeLabel.PlaceAbove(p, 0.05f);
                _mergeLabel.Show(_merge.RunActive || NearestHand(p, 0.28f) != null);
            }
        }

        // -----------------------------------------------------------------
        // hands
        // -----------------------------------------------------------------

        public bool IsHandBusy(PrismHands.Hand h) =>
            (Hands != null && h == Hands.Left && _heldL != null) ||
            (Hands != null && h == Hands.Right && _heldR != null);

        void TickHands()
        {
            if (Hands == null) return;
            TickOneHand(Hands.Left, ref _heldL);
            TickOneHand(Hands.Right, ref _heldR);
        }

        void TickOneHand(PrismHands.Hand h, ref BeadGrab? held)
        {
            if (h == null || !h.IsTracked)
            {
                if (held != null) { held.Value.Structure.HeldBeadIndex = -1; held = null; }
                return;
            }

            Vector3 local = Anchor.InverseTransformPoint(PrismHands.PointOf(h));

            if (held != null)
            {
                var hv = held.Value;
                var bead = hv.Structure.Beads.Beads[hv.Index];
                bead.Position = local;
                bead.Glow = 1f;
                hv.Structure.Beads.Beads[hv.Index] = bead;

                if (h.IsGrasping) return;

                // Released near another bead of the SAME structure: swap them for real.
                int other = hv.Structure.Beads.NearestActive(local, SwapRadius, hv.Index);
                if (other >= 0)
                {
                    hv.Structure.ManualSwap(hv.Index, other);
                    Loop.Evidence.Record(AlgorithmCityEvidence.SwapManual);
                    Hands.Clunk(h, 0.3f);
                }
                hv.Structure.HeldBeadIndex = -1;
                held = null;
                return;
            }

            if (h.IsGrasping)
            {
                var found = FindNearestBead(local, GrabRadius);
                if (found != null)
                {
                    held = found;
                    found.Value.Structure.HeldBeadIndex = found.Value.Index;
                    Loop.Evidence.Record(AlgorithmCityEvidence.Touch);
                    Hands.Buzz(h, 0.22f, 0.04f);
                    return;
                }

                for (int i = 0; i < _allSliders.Length; i++)
                {
                    if (!_allSliders[i].Bead.gameObject.activeInHierarchy) continue;
                    if (Vector3.Distance(local, _allSliders[i].LocalPosition) < SliderGrabRadius)
                    {
                        _allSliders[i].DragTo(local);
                        OnSliderChanged(_allSliders[i]);
                        return;
                    }
                }
                // Grasping found nothing to grab or drag -- fall through. A firm pinch can cross
                // both the IsGrasping and PinchDown thresholds in the same frame, and a tap on the
                // spawner or a gate must not be swallowed just because it was also firm enough to
                // register as a grasp.
            }

            if (h.PinchDown)
            {
                for (int i = 0; i < _allGates.Length; i++)
                {
                    if (!_allGates[i].View.gameObject.activeInHierarchy) continue;
                    if (Vector3.Distance(local, _allGates[i].View.localPosition) < GateTapRadius)
                    {
                        _allGates[i].Toggle();
                        Hands.Clunk(h, 0.25f);
                        return;
                    }
                }

                if (_spawnerView.gameObject.activeInHierarchy && _spawnCooldown <= 0f &&
                    Vector3.Distance(local, SpawnerLocalPos) < SpawnerTapRadius)
                {
                    TrySpawn(h);
                }
            }
        }

        BeadGrab? FindNearestBead(Vector3 localPoint, float maxDist)
        {
            SortStructure bestS = null;
            int bestI = -1;
            float bestD = maxDist;
            for (int k = 0; k < _grabStructures.Length; k++)
            {
                var s = _grabStructures[k];
                if (!s.Beads.View.gameObject.activeInHierarchy) continue;
                int idx = s.Beads.NearestActive(localPoint, bestD);
                if (idx < 0) continue;
                float d = Vector3.Distance(s.Beads.Beads[idx].Position, localPoint);
                if (d < bestD) { bestD = d; bestS = s; bestI = idx; }
            }
            return bestS == null ? (BeadGrab?)null : new BeadGrab { Structure = bestS, Index = bestI };
        }

        void OnSliderChanged(SliderControl s)
        {
            if (s == _insertionSpeedSlider) _insertion.SpeedMultiplier = SpeedFromT(s.T);
            else if (s == _mergeSpeedSlider) _merge.SpeedMultiplier = SpeedFromT(s.T);
            else if (s == _scaleRail) _pendingN = NFromT(s.T);
        }

        static float SpeedFromT(float t) => Mathf.Lerp(0.15f, 4f, t);
        static int NFromT(float t) => Mathf.RoundToInt(Mathf.Lerp(MinN, MaxN, t));

        void TrySpawn(PrismHands.Hand h)
        {
            _spawnCooldown = 0.5f;
            SpawnNewRun();
            Hands.Clunk(h, 0.35f);
        }

        void SpawnNewRun()
        {
            int n = Loop.Stage >= LoopStage.Explore ? Mathf.Clamp(_pendingN, MinN, MaxN) : WonderN;
            var seed = ShuffledPermutation(n);

            bool racing = _challenge.HasPendingPrediction;
            if (racing)
            {
                // A prediction only means something if both structures run at the same rate --
                // the marker is a claim about the ALGORITHM, not about whichever slider was left
                // higher. Both clocks are reset to 1x for exactly this one race.
                float oneXT = Mathf.InverseLerp(0.15f, 4f, 1f);
                _insertionSpeedSlider.SetT(oneXT);
                _mergeSpeedSlider.SetT(oneXT);
                _insertion.SpeedMultiplier = 1f;
                _merge.SpeedMultiplier = 1f;
            }

            _insertion.BeginRun(seed, SpawnerLocalPos);
            _merge.BeginRun(seed, SpawnerLocalPos);

            _pairPending = true;
            _pairInsDone = false;
            _pairMrgDone = false;
            _pairN = n;
            _pairRacing = racing;

            if (Loop.Stage >= LoopStage.Create) _challenge.SeedBench(seed, n);
        }

        int[] ShuffledPermutation(int n)
        {
            n = Mathf.Clamp(n, 1, MaxN);
            for (int i = 0; i < n; i++) _scratch[i] = i + 1;
            for (int i = n - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_scratch[i], _scratch[j]) = (_scratch[j], _scratch[i]);
            }
            var result = new int[n];
            System.Array.Copy(_scratch, result, n);
            return result;
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
                    Id = "algorithm-city",
                    Title = "Algorithm City",
                    Domain = ConceptDomain.Machines,
                    Kind = KnowledgeKind.System,
                    Direction = Dir(126f, 0.00f),
                    Distance = 3.3f,
                    WorldId = "algorithm-city",
                    Capability = "Watch a computation happen, reach in, and change what it does while it runs.",
                    Formalisation = "An algorithm is a physical process with a cost. The same data, sent " +
                                    "through a different structure, can cost ten times less to put in order.",
                    Links = new (string target, Relation relation, float strength)[]
                    {
                        ("algorithmic-cost", Relation.Measures, 0.85f),
                        ("data-structures", Relation.Composes, 0.80f),
                        ("recursion", Relation.Instantiates, 0.75f),
                        ("comparison", Relation.Composes, 0.65f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "algorithmic-cost",
                    Title = "Algorithmic Cost",
                    Domain = ConceptDomain.Mathematics,
                    Kind = KnowledgeKind.Equation,
                    Direction = Dir(108f, 0.35f),
                    Distance = 4.3f,
                    Capability = "Read how a method's cost grows with input size from a curve you " +
                                "measured yourself, not a formula someone told you.",
                    Formalisation = "The number of elementary operations a method needs, as a function " +
                                    "of input size n. Insertion sort costs on the order of n^2 " +
                                    "comparisons; merge sort costs on the order of n log n.",
                    Links = new (string target, Relation relation, float strength)[]
                    {
                        ("comparison", Relation.Composes, 0.75f),
                        ("recursion", Relation.Constrains, 0.55f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "recursion",
                    Title = "Recursion",
                    Domain = ConceptDomain.Machines,
                    Kind = KnowledgeKind.Process,
                    Direction = Dir(144f, 0.30f),
                    Distance = 4.6f,
                    Capability = "Solve a problem by solving two smaller copies of it and combining the answers.",
                    Formalisation = "A process defined in terms of smaller instances of itself, bottoming " +
                                    "out at a case simple enough to answer directly.",
                    Links = new (string target, Relation relation, float strength)[]
                    {
                        ("symmetry", Relation.Analogy, 0.65f),
                        ("feedback", Relation.Analogy, 0.45f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "data-structures",
                    Title = "Data Structures",
                    Domain = ConceptDomain.Machines,
                    Kind = KnowledgeKind.System,
                    Direction = Dir(117f, -0.35f),
                    Distance = 4.9f,
                    Capability = "Choose the arrangement that makes the operation you actually need to do cheap.",
                    Formalisation = "The same values, held in a different shape, change what an operation " +
                                    "on them costs: a flat line and a branching structure pay differently " +
                                    "for the same sort.",
                    Links = System.Array.Empty<(string target, Relation relation, float strength)>()
                };

                yield return new ConceptSpec
                {
                    Id = "comparison",
                    Title = "Comparison",
                    Domain = ConceptDomain.Machines,
                    Kind = KnowledgeKind.Fact,
                    Direction = Dir(135f, -0.40f),
                    Distance = 5.3f,
                    Capability = "See cost as a count of decisions, not a stopwatch reading.",
                    Formalisation = "One elementary decision: is this before that? Every classical " +
                                    "comparison-based sort is built entirely out of counting these.",
                    Links = System.Array.Empty<(string target, Relation relation, float strength)>()
                };
            }
        }

        /// <summary>Azimuth measured clockwise from +Z (forward) about +Y, matching the compass
        /// sense the master's wedge briefs are given in. Elevation is the raw Y component fed to
        /// the direction vector before normalising, not an angle.</summary>
        static Vector3 Dir(float azimuthDeg, float elevation)
        {
            float az = azimuthDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az), elevation, Mathf.Cos(az)).normalized;
        }
    }
}
