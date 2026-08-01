using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.AlgorithmCity
{
    /// <summary>
    /// Apply, Explain and Create, plus the growth-curve panel that Discover and Formalize read
    /// from. Split out of <see cref="AlgorithmCityWorld"/> exactly as OrbitalChallenge is split out
    /// of OrbitalWorld: these are the stages that layer a specific question on top of the ordinary
    /// "spawn a batch, watch it sort" loop, rather than being part of that loop itself.
    ///
    ///   Apply    keep the Merge structure's spire under a comparison BUDGET, drawn as a ring at
    ///            the exact height that many comparisons will reach — the target is a place, not a
    ///            number.
    ///   Explain  place a marker on whichever structure you think will win, then the next run IS
    ///            the race: both clocks are set to the same speed so the only thing that decides
    ///            it is the algorithm.
    ///   Create   a third, learner-configured track: everything left of a hand-placed divider is
    ///            sorted by Insertion, everything right of it by Merge, and the two results are
    ///            honestly combined by one more real merge pass.
    /// </summary>
    public class AlgorithmCityChallenge : MonoBehaviour
    {
        public AlgorithmCityWorld World;

        // ---- growth curve --------------------------------------------------
        struct CurvePoint { public int N; public int Comparisons; }
        readonly List<CurvePoint> _insCurve = new List<CurvePoint>();
        readonly List<CurvePoint> _mrgCurve = new List<CurvePoint>();
        readonly List<Vector3> _curveScratch = new List<Vector3>();
        Mesh _insCurveMesh, _mrgCurveMesh;
        Transform _insCurveView, _mrgCurveView;
        Vector3 _curveOrigin;
        float _curveWidth, _curveHeight;
        int _curveMaxN;
        int _maxComparisonsSeen = 1;

        // ---- apply: comparison budget --------------------------------------
        Transform _budgetRing;
        Mesh _budgetRingMesh;
        readonly List<Vector3> _ringScratch = new List<Vector3>();
        int _lastRingN = -1;
        const int ApplyMinN = 24;

        // ---- explain: predict the winner ------------------------------------
        Transform _predictMarker;
        Vector3 _predictParkLocal;
        bool _predictPlaced;
        SortStructure _predictedStructure;

        // ---- create: the pipeline bench -------------------------------------
        BeadField _benchBeads;
        SliderControl _dividerSlider;
        Vector3 _benchOrigin;
        float _benchWidth;
        int _benchN, _benchDivider;
        ISortProcess _benchLeft, _benchRight;
        MergeSortProcess _benchCombine;
        enum BenchPhase { Idle, Split, Combine }
        BenchPhase _benchPhase = BenchPhase.Idle;
        float _benchAccumulator;
        const float BenchStepsPerSecond = 8f;

        // -----------------------------------------------------------------
        // construction
        // -----------------------------------------------------------------

        public void BuildCurvePanel(Transform parent, Vector3 origin, float width, float height, int maxN,
                                    Material insMat, Material mrgMat)
        {
            _curveOrigin = origin; _curveWidth = width; _curveHeight = height; _curveMaxN = Mathf.Max(1, maxN);

            var insGo = new GameObject("GrowthCurveInsertion");
            insGo.transform.SetParent(parent, false);
            _insCurveMesh = new Mesh { name = "GrowthCurveInsertionMesh" };
            _insCurveMesh.MarkDynamic();
            insGo.AddComponent<MeshFilter>().sharedMesh = _insCurveMesh;
            insGo.AddComponent<MeshRenderer>().sharedMaterial = insMat;
            _insCurveView = insGo.transform;

            var mrgGo = new GameObject("GrowthCurveMerge");
            mrgGo.transform.SetParent(parent, false);
            _mrgCurveMesh = new Mesh { name = "GrowthCurveMergeMesh" };
            _mrgCurveMesh.MarkDynamic();
            mrgGo.AddComponent<MeshFilter>().sharedMesh = _mrgCurveMesh;
            mrgGo.AddComponent<MeshRenderer>().sharedMaterial = mrgMat;
            _mrgCurveView = mrgGo.transform;
        }

        public void BuildBudgetRing(Transform parent, Material mat)
        {
            var go = new GameObject("BudgetRing");
            go.transform.SetParent(parent, false);
            _budgetRingMesh = new Mesh { name = "BudgetRingMesh" };
            _budgetRingMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _budgetRingMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            _budgetRing = go.transform;
        }

        public void BuildPredictionMarker(Transform parent, Vector3 parkLocal, Material mat)
        {
            _predictParkLocal = parkLocal;
            var go = new GameObject("PredictionMarker");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(1);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * 0.016f;
            go.transform.localPosition = parkLocal;
            _predictMarker = go.transform;
        }

        public void BuildBench(Transform parent, Material beadMat, Vector3 origin, float width,
                               Vector3 dividerOrigin, Material railMat, Material dividerMat, int maxN)
        {
            _benchOrigin = origin;
            _benchWidth = width;
            _benchBeads = new BeadField("BenchBeads", parent, beadMat, maxN);
            _dividerSlider = new SliderControl("Divider", parent, dividerOrigin, Vector3.right,
                                               width * 0.5f, 0.5f, railMat, dividerMat, 0.009f);
        }

        // -----------------------------------------------------------------
        // reveal
        // -----------------------------------------------------------------

        public void SyncRevealState(LoopStage stage)
        {
            bool curveOn = stage >= LoopStage.Discover;
            _insCurveView.gameObject.SetActive(curveOn);
            _mrgCurveView.gameObject.SetActive(curveOn);

            _budgetRing.gameObject.SetActive(stage >= LoopStage.Apply);
            _predictMarker.gameObject.SetActive(stage >= LoopStage.Explain);

            bool createOn = stage >= LoopStage.Create;
            _benchBeads.View.gameObject.SetActive(createOn);
            _dividerSlider.SetActive(createOn);
        }

        /// <summary>One-shot moment the world enters a stage: the challenge's fixed input sizes are
        /// dialled in for the learner, on the SAME physical rail they already know how to use.</summary>
        public void OnStageEntered(LoopStage stage)
        {
            if (stage == LoopStage.Apply) World.SnapScaleRail(28);
            else if (stage == LoopStage.Explain) World.SnapScaleRail(20);
        }

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        public void Tick(float dt)
        {
            TickPrediction();
            TickDividerHand();
            TickBench(dt);
            UpdateBudgetRing();
        }

        void TickPrediction()
        {
            if (World.Loop.Stage < LoopStage.Explain || _predictPlaced) return;
            var hands = World.Hands;
            if (hands == null) return;
            DriveMarkerHand(hands.Left);
            DriveMarkerHand(hands.Right);
        }

        void DriveMarkerHand(PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked || World.IsHandBusy(h)) return;
            Vector3 local = World.CityAnchor.InverseTransformPoint(PrismHands.PointOf(h));

            if (h.Pinch > 0.6f)
            {
                _predictMarker.localPosition = local;
            }
            else if (h.PinchUp && Vector3.Distance(local, _predictMarker.localPosition) < 0.10f)
            {
                float dIns = Vector3.Distance(local, World.InsertionCenterLocal);
                float dMrg = Vector3.Distance(local, World.MergeCenterLocal);
                _predictedStructure = dIns <= dMrg ? World.Insertion : World.Merge;
                _predictPlaced = true;
            }
        }

        /// <summary>True the instant a race is armed; consumed by the world at the next spawn.</summary>
        public bool HasPendingPrediction => _predictPlaced;

        /// <summary>Called once the paired run the prediction was armed for has finished.</summary>
        public void EvaluatePrediction(bool insertionWon)
        {
            if (!_predictPlaced) return;

            bool guessedInsertion = _predictedStructure == World.Insertion;
            bool correct = guessedInsertion == insertionWon;

            if (correct)
            {
                World.Loop.Evidence.Record(AlgorithmCityEvidence.PredictionGood);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.35f);
            }
            else
            {
                // The most informative outcome available, recorded honestly as a misconception
                // rather than as a failed attempt — the constellation will show this concept as
                // not yet settled, which at this exact moment is true.
                World.Knowledge?.FlagMisconception(World.PrimaryConceptId, 0.15f);
            }

            _predictPlaced = false;
            _predictedStructure = null;
            _predictMarker.localPosition = _predictParkLocal;
        }

        void UpdateBudgetRing()
        {
            if (_budgetRing == null || !_budgetRing.gameObject.activeInHierarchy) return;
            int n = World.PendingN;
            if (n == _lastRingN) return;
            _lastRingN = n;

            int budget = BudgetFor(n);
            float h = World.Merge.HeightForComparisons(budget);
            Vector3 centre = World.Merge.SpireBaseLocal + Vector3.up * h;

            _ringScratch.Clear();
            const int segs = 20;
            const float r = 0.018f;
            for (int i = 0; i <= segs; i++)
            {
                float a = (float)i / segs * Mathf.PI * 2f;
                _ringScratch.Add(centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            PrismMesh.Tube(_ringScratch, 0.0016f, 5, _budgetRingMesh);
        }

        /// <summary>
        /// The comparison budget for a batch of size n: comfortably above Merge's near-constant
        /// n*log2(n) cost and, for the n>=24 this challenge insists on, comfortably below
        /// Insertion's typical n^2/4 — checked against both algorithms' real behaviour in NOTES.md,
        /// not asserted from the formula alone.
        /// </summary>
        static int BudgetFor(int n) => Mathf.CeilToInt(n * Mathf.Log(Mathf.Max(2, n), 2f) * 1.3f);

        void TickDividerHand()
        {
            if (World.Loop.Stage < LoopStage.Create) return;
            var hands = World.Hands;
            if (hands == null) return;
            DriveDivider(hands.Left);
            DriveDivider(hands.Right);
        }

        void DriveDivider(PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked || !h.IsGrasping || World.IsHandBusy(h)) return;
            Vector3 local = World.CityAnchor.InverseTransformPoint(PrismHands.PointOf(h));
            if (Vector3.Distance(local, _dividerSlider.LocalPosition) < 0.035f)
                _dividerSlider.DragTo(local);
        }

        /// <summary>Seed the bench with the same batch just sent to the two main structures, split
        /// at the divider's current position. Called by the world once Create is reached.</summary>
        public void SeedBench(int[] seed, int n)
        {
            _benchN = n;
            _benchDivider = Mathf.Clamp(Mathf.RoundToInt(_dividerSlider.T * n), 0, n);

            _benchBeads.DeactivateAll();
            float scale = Mathf.Clamp(_benchWidth / Mathf.Max(n, 1) * 0.30f, 0.005f, 0.017f);
            for (int i = 0; i < n; i++)
            {
                float v = n <= 1 ? 0.5f : (float)(seed[i] - 1) / (n - 1);
                _benchBeads.Spawn(i, World.SpawnerLocal, BenchSlot(i, n), v, scale);
            }
            _benchBeads.Count = n;

            if (_benchDivider <= 0)
            {
                _benchLeft = null;
                _benchRight = new MergeSortProcess((int[])seed.Clone());
            }
            else if (_benchDivider >= n)
            {
                _benchLeft = new InsertionSortProcess((int[])seed.Clone());
                _benchRight = null;
            }
            else
            {
                var left = new int[_benchDivider];
                var right = new int[n - _benchDivider];
                System.Array.Copy(seed, 0, left, 0, _benchDivider);
                System.Array.Copy(seed, _benchDivider, right, 0, right.Length);
                _benchLeft = new InsertionSortProcess(left);
                _benchRight = new MergeSortProcess(right);
            }

            _benchCombine = null;
            _benchPhase = BenchPhase.Split;
            _benchAccumulator = 0f;
        }

        Vector3 BenchSlot(int index, int n)
        {
            float t = n <= 1 ? 0.5f : (index + 0.5f) / n;
            float x = _benchOrigin.x + (t - 0.5f) * _benchWidth;
            return new Vector3(x, _benchOrigin.y, _benchOrigin.z);
        }

        void TickBench(float dt)
        {
            bool visible = _benchBeads.View.gameObject.activeInHierarchy;
            if (visible)
            {
                _benchBeads.Tick(dt, AlgorithmCityWorld.BeadFollowPerSecond);
                _benchBeads.Rebuild();
            }

            if (_benchPhase == BenchPhase.Idle || !visible) return;

            _benchAccumulator += dt * BenchStepsPerSecond;
            int budget = 64;
            while (_benchAccumulator >= 1f && budget-- > 0)
            {
                _benchAccumulator -= 1f;

                if (_benchPhase == BenchPhase.Split)
                {
                    bool leftDone = _benchLeft == null || _benchLeft.IsDone;
                    bool rightDone = _benchRight == null || _benchRight.IsDone;
                    if (leftDone && rightDone)
                    {
                        BeginCombine();
                        // A degenerate split (divider at an extreme) can finish inside
                        // BeginCombine itself; do not loop back into a phase that no longer exists.
                        if (_benchPhase == BenchPhase.Idle) break;
                        continue;
                    }

                    if (!leftDone)
                    {
                        var step = _benchLeft.Step();
                        SortVisuals.ApplyFlatStep(_benchBeads, _benchLeft, step, 0);
                    }
                    if (!rightDone)
                    {
                        var step = _benchRight.Step();
                        SortVisuals.ApplyFlatStep(_benchBeads, _benchRight, step, _benchDivider);
                    }
                }
                else
                {
                    var step = _benchCombine.Step();
                    SortVisuals.ApplyFlatStep(_benchBeads, _benchCombine, step, 0);
                    if (_benchCombine.IsDone) { FinishBench(); break; }
                }
            }
        }

        void BeginCombine()
        {
            var combined = new int[_benchN];
            int li = 0;
            if (_benchLeft != null)
            {
                System.Array.Copy(_benchLeft.Values, 0, combined, 0, _benchLeft.Values.Length);
                li = _benchLeft.Values.Length;
            }
            if (_benchRight != null)
                System.Array.Copy(_benchRight.Values, 0, combined, li, _benchRight.Values.Length);

            _benchCombine = MergeSortProcess.ForSingleCombine(combined, li);
            _benchPhase = BenchPhase.Combine;
            if (_benchCombine.IsDone) FinishBench();
        }

        void FinishBench()
        {
            _benchPhase = BenchPhase.Idle;
            if (_benchDivider > 0 && _benchDivider < _benchN)
            {
                // A genuine hybrid: both structures did real work in one composed pipeline.
                World.Loop.Evidence.Record(AlgorithmCityEvidence.PipelineHybrid);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.3f);
            }
        }

        // -----------------------------------------------------------------
        // growth curve
        // -----------------------------------------------------------------

        /// <summary>Record one real, measured (n, comparisons) sample per algorithm from a run the
        /// learner just produced, and redraw both curves from the accumulated samples. Never drawn
        /// from n*log2(n) or n^2/4 — only ever from <see cref="ISortProcess.Comparisons"/>.</summary>
        public void OnPairComplete(int n, int insertionComparisons, int mergeComparisons)
        {
            _insCurve.Add(new CurvePoint { N = n, Comparisons = insertionComparisons });
            _mrgCurve.Add(new CurvePoint { N = n, Comparisons = mergeComparisons });
            _insCurve.Sort((a, b) => a.N - b.N);
            _mrgCurve.Sort((a, b) => a.N - b.N);
            _maxComparisonsSeen = Mathf.Max(_maxComparisonsSeen, insertionComparisons, mergeComparisons);

            RebuildCurve(_insCurve, _insCurveMesh);
            RebuildCurve(_mrgCurve, _mrgCurveMesh);

            EvaluateApply(n, mergeComparisons);
        }

        void RebuildCurve(List<CurvePoint> points, Mesh mesh)
        {
            if (points.Count < 2) { mesh.Clear(); return; }

            _curveScratch.Clear();
            foreach (var p in points)
            {
                float tx = Mathf.Clamp01((float)p.N / _curveMaxN);
                float ty = Mathf.Clamp01((float)p.Comparisons / Mathf.Max(1, _maxComparisonsSeen));
                _curveScratch.Add(_curveOrigin + new Vector3((tx - 0.5f) * _curveWidth, ty * _curveHeight, 0f));
            }
            PrismMesh.Tube(_curveScratch, 0.0016f, 5, mesh);
        }

        void EvaluateApply(int n, int mergeComparisons)
        {
            if (World.Loop.Stage != LoopStage.Apply) return;
            if (n < ApplyMinN) return;
            if (mergeComparisons <= BudgetFor(n))
            {
                World.Loop.Evidence.Record(AlgorithmCityEvidence.ChallengeDone);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.3f);
            }
        }
    }
}
