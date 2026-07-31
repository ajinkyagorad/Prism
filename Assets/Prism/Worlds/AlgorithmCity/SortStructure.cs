using System.Collections.Generic;
using Prism.Aesthetic;
using UnityEngine;

namespace Prism.Worlds.AlgorithmCity
{
    /// <summary>
    /// One running sort, fully bundled: the algorithm, its batched beads, and the spire that
    /// physically accumulates its comparison count. Two instances exist for the whole world's
    /// life — Insertion (flat) and Merge (cascading) — built once in <c>BuildWorld</c> and reused
    /// run after run via <see cref="BeginRun"/>.
    ///
    /// Layout law: a bead's column is always its ARRAY INDEX, for the life of the structure. A flat
    /// structure never moves a bead's target off its column — a swap crosses two beads' CURRENT
    /// positions instead (see <see cref="SortVisuals"/>), which is what makes the exchange read as
    /// physical motion rather than two colours trading places. A cascading structure keeps the
    /// column fixed too; only the ROW changes, rising each time the window containing that index is
    /// published. Either way the mapping from "where is this algorithm in its own array" to "where
    /// is this bead in space" is direct and never faked.
    /// </summary>
    public class SortStructure
    {
        public readonly string Label;
        public readonly BeadField Beads;
        public readonly bool IsCascade;

        public ISortProcess Process { get; private set; }
        public int N { get; private set; }

        /// <summary>Multiplies the base step rate. This is "slow the clock" made real: it scales
        /// how often <see cref="ISortProcess.Step"/> is actually called, not an animation speed.</summary>
        public float SpeedMultiplier = 1f;

        /// <summary>"Block a path": while true, this structure's algorithm does not advance at all.</summary>
        public bool GateBlocked;

        /// <summary>Index of the bead currently in a learner's hand, or -1. While held, the whole
        /// structure pauses, so nothing races ahead of the hand that reached into it.</summary>
        public int HeldBeadIndex = -1;

        public bool RunActive;
        /// <summary>True for exactly the Tick() call in which the run finished; the caller reads it
        /// once and evidence/curve bookkeeping happens, then it is expected to move on.</summary>
        public bool JustFinished;
        public float RunElapsedSeconds;

        public Transform SpireView { get; private set; }

        readonly Transform _parent;
        readonly Vector3 _origin;
        readonly float _width;
        readonly float _rowStep;
        readonly System.Func<int[], ISortProcess> _factory;

        float _stepAccumulator;
        const float BaseStepsPerSecond = 8f;

        Mesh _spireMesh;
        Material _spireMat;
        Vector3 _spireBase;
        float _spireHeightScale;
        int _lastSpireComparisons = -1;
        readonly List<Vector3> _spirePath = new List<Vector3> { Vector3.zero, Vector3.zero };

        public SortStructure(string label, Transform parent, Material beadMat, int maxN,
                             Vector3 origin, float width, bool cascade, float rowStep,
                             System.Func<int[], ISortProcess> factory)
        {
            Label = label;
            _parent = parent;
            _origin = origin;
            _width = width;
            IsCascade = cascade;
            _rowStep = rowStep;
            _factory = factory;
            Beads = new BeadField(label + "Beads", parent, beadMat, maxN);
        }

        public Vector3 SlotPos(int index, int row)
        {
            float t = N <= 1 ? 0.5f : (index + 0.5f) / N;
            float x = _origin.x + (t - 0.5f) * _width;
            float y = _origin.y + (IsCascade ? row * _rowStep : 0f);
            return new Vector3(x, y, _origin.z);
        }

        public float BeadScale => Mathf.Clamp(_width / Mathf.Max(N, 1) * 0.30f, 0.005f, 0.017f);

        public float ColorOf(int rawValue) => N <= 1 ? 0.5f : (float)(rawValue - 1) / (N - 1);

        public void BuildSpire(Vector3 baseLocal, Material spireMat, float heightScale)
        {
            _spireBase = baseLocal;
            _spireMat = spireMat;
            _spireHeightScale = heightScale;

            var go = new GameObject(Label + "Spire");
            go.transform.SetParent(_parent, false);
            _spireMesh = new Mesh { name = Label + "SpireMesh" };
            _spireMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _spireMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _spireMat;
            SpireView = go.transform;
        }

        /// <summary>Local-space point the spire rises from. Lets the Apply challenge's budget ring
        /// line its height up with this same spire using the same compression.</summary>
        public Vector3 SpireBaseLocal => _spireBase;

        /// <summary>The same square-root compression <see cref="UpdateSpire"/> uses, exposed so a
        /// target height (the Apply budget) can be drawn at the position the spire will actually
        /// reach when it gets there.</summary>
        public float HeightForComparisons(int comparisons) =>
            Mathf.Max(0.001f, Mathf.Min(0.24f, _spireHeightScale * Mathf.Sqrt(Mathf.Max(0, comparisons))));

        /// <summary>Start a fresh run over a permutation of values 1..N. All beads arrive visually
        /// from <paramref name="spawnLocal"/> rather than popping into their slots.</summary>
        public void BeginRun(int[] seed, Vector3 spawnLocal)
        {
            N = seed.Length;
            Process = _factory((int[])seed.Clone());
            Beads.DeactivateAll();

            float scale = BeadScale;
            for (int i = 0; i < N; i++)
                Beads.Spawn(i, spawnLocal, SlotPos(i, 0), ColorOf(Process.Values[i]), scale);
            Beads.Count = N;

            _stepAccumulator = 0f;
            RunActive = true;
            JustFinished = false;
            RunElapsedSeconds = 0f;
            _lastSpireComparisons = -1;
            HeldBeadIndex = -1;
        }

        /// <summary>Swap two live values by hand — the honest effect of the "swap two" gesture.
        /// Safe at any moment: it can only ever exchange two valid array entries.</summary>
        public void ManualSwap(int a, int b)
        {
            if (Process == null || a == b || a < 0 || b < 0) return;
            var vals = Process.Values;
            if (a >= vals.Length || b >= vals.Length) return;
            (vals[a], vals[b]) = (vals[b], vals[a]);

            var beadA = Beads.Beads[a];
            var beadB = Beads.Beads[b];
            beadA.Value01 = ColorOf(vals[a]);
            beadB.Value01 = ColorOf(vals[b]);
            beadA.Glow = 1f;
            beadB.Glow = 1f;
            Beads.Beads[a] = beadA;
            Beads.Beads[b] = beadB;
        }

        /// <summary>Advance beads and, unless paused, the algorithm. Call once per frame while this
        /// structure is visible; skip entirely while hidden to save the work.</summary>
        public void Tick(float dt, float followPerSecond)
        {
            Beads.Tick(dt, followPerSecond);
            UpdateSpire();

            if (!RunActive || Process == null) return;
            RunElapsedSeconds += dt;

            bool paused = GateBlocked || HeldBeadIndex >= 0;
            if (paused) return;

            _stepAccumulator += dt * Mathf.Max(0.02f, SpeedMultiplier) * BaseStepsPerSecond;
            int budget = 48;   // bounds a catch-up burst after a hitch to a sane number of steps
            while (_stepAccumulator >= 1f && budget-- > 0)
            {
                var step = Process.Step();
                _stepAccumulator -= 1f;

                if (IsCascade) SortVisuals.ApplyCascadeStep(Beads, (MergeSortProcess)Process, step, SlotPos);
                else SortVisuals.ApplyFlatStep(Beads, Process, step, 0);

                if (step.Kind == SortStep.StepKind.Done)
                {
                    RunActive = false;
                    JustFinished = true;
                    break;
                }
            }
        }

        void UpdateSpire()
        {
            if (SpireView == null || Process == null) return;
            int cmp = Process.Comparisons;
            if (cmp == _lastSpireComparisons) return;
            _lastSpireComparisons = cmp;

            // Square-root compressed so a few hundred comparisons still fit in arm's reach — the
            // spire is a monotonic, honestly-measured indicator, not a literal-scale ruler; the
            // exact integer is what the Formalize-stage label shows, and the growth-curve panel
            // (AlgorithmCityChallenge) is where the true numbers are plotted precisely.
            float h = Mathf.Max(0.001f, Mathf.Min(0.24f, _spireHeightScale * Mathf.Sqrt(cmp)));
            _spirePath[0] = _spireBase;
            _spirePath[1] = _spireBase + Vector3.up * h;
            PrismMesh.Tube(_spirePath, 0.006f, 6, _spireMesh);
        }
    }
}
