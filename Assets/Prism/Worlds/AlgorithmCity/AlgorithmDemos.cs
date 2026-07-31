using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using UnityEngine;

namespace Prism.Worlds.AlgorithmCity
{
    /// <summary>
    /// RECURSION — reach toward the centre and the same structure happens again, smaller.
    ///
    /// Five nested shells, all genuinely concentric (same centre, shrinking radius — this is what
    /// "a smaller copy of itself" looks like when you can walk around it). The free hand's distance
    /// from the centre selects which shell is "the current frame": near the rim is the outermost
    /// call, near the centre is the innermost. The last shell is not another shell at all — it is a
    /// solid seed, because recursion has to bottom out somewhere or it is not recursion, it is just
    /// falling. Nothing here is a formula pretending to be a structure: the depth index is computed
    /// live from real hand position every frame, and there are five real GameObjects, not one mesh
    /// faking five.
    /// </summary>
    [ConceptDemoFor("recursion")]
    public class RecursionDemo : ConceptDemo
    {
        const int Levels = 5;
        Transform[] _shells;
        Material[] _mats;
        float[] _radii;
        float[] _glow;
        int _depth = -1;

        public override void Build()
        {
            _shells = new Transform[Levels];
            _mats = new Material[Levels];
            _radii = new float[Levels];
            _glow = new float[Levels];

            for (int i = 0; i < Levels; i++)
            {
                float r = Mathf.Lerp(0.85f, 0.14f, i / (float)(Levels - 1));
                _radii[i] = r;
                bool isBase = i == Levels - 1;

                Material m;
                if (isBase)
                {
                    m = PrismMaterials.New(PrismMaterials.Seed);
                    m.SetColor("_Tint", PrismPalette.Gold);
                    m.SetFloat("_Growth", 1f);
                    m.SetFloat("_Density", 1.1f);
                }
                else
                {
                    m = PrismMaterials.New(PrismMaterials.Gel);
                    m.SetColor("_Tint", Tint);
                    m.SetColor("_DeepTint", PrismPalette.Violet);
                }
                Track(m);
                _mats[i] = m;
                _shells[i] = Body(PrismMesh.Icosphere(2), r, m, $"shell{i}");
                _shells[i].localRotation = Quaternion.Euler(0f, i * 23f, i * 11f);
            }
        }

        protected override void OnTick(float dt)
        {
            // Near the rim = shallow (the outermost call); near the centre = deep. Reaching inward
            // IS descending, exactly the gesture the concept is named for.
            float t = 1f;
            if (TryFreeLocal(out var hand)) t = Mathf.Clamp01(hand.magnitude / 0.85f);
            int depth = Mathf.Clamp(Mathf.FloorToInt((1f - t) * Levels), 0, Levels - 1);

            if (depth != _depth)
            {
                _depth = depth;
                Voice?.Settle(transform.position, 0.3f, depth == Levels - 1 ? 1.7f : 1.15f);
            }

            for (int i = 0; i < Levels; i++)
            {
                float want = i == _depth ? 1f : 0.12f;
                _glow[i] = Mathf.Lerp(_glow[i], want, dt * 3.5f);

                if (i == Levels - 1) _mats[i].SetFloat("_Hover", _glow[i]);
                else _mats[i].SetFloat("_Density", Mathf.Lerp(0.15f, 0.9f, _glow[i]));

                // The current level breathes a little larger — "you are here", with no text at all.
                _shells[i].localScale = Vector3.one * _radii[i] * (1f + _glow[i] * 0.08f);
                _shells[i].localRotation *= Quaternion.Euler(0f, dt * (9f + i * 3f) * (0.3f + _glow[i]), 0f);
            }
        }
    }

    /// <summary>
    /// ALGORITHMIC COST — the same gesture that doubles the data does not double the work.
    ///
    /// The free hand's horizontal position selects how many of up to twelve motes are "in": that
    /// count is n. Two bars rise from two REAL loops run over that same n every frame — one visits
    /// each item once, the other visits every pair — never from n or n*(n-1)/2 written down as a
    /// formula. Move the hand to double n and watch: the left bar doubles, the right bar very
    /// nearly quadruples. That gap, felt in the hand rather than read as an exponent, is the concept.
    /// </summary>
    [ConceptDemoFor("algorithmic-cost")]
    public class AlgorithmicCostDemo : ConceptDemo
    {
        const int MinN = 2, MaxN = 12;
        Transform[] _motes;
        Transform _linearBar, _pairsBar;
        int _n = MinN;

        public override void Build()
        {
            _motes = new Transform[MaxN];
            for (int i = 0; i < MaxN; i++)
            {
                float a = i / (float)MaxN * Mathf.PI * 2f;
                var mote = Ball(0.05f, PrismPalette.Cyan, 0.5f, $"mote{i}");
                mote.localPosition = new Vector3(Mathf.Cos(a) * 0.55f, 0.4f, Mathf.Sin(a) * 0.55f);
                _motes[i] = mote;
            }
            _linearBar = Body(PrismMesh.Icosphere(1), 1f, FlatMaterial(PrismPalette.Mint, 0.7f), "linear");
            _pairsBar = Body(PrismMesh.Icosphere(1), 1f, FlatMaterial(PrismPalette.Coral, 0.7f), "pairs");
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
            {
                float t = Mathf.InverseLerp(-0.75f, 0.75f, hand.x);
                _n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(MinN, MaxN, t)), MinN, MaxN);
            }

            // Really counted: two real loops over the current n, not a closed-form shortcut.
            int linear = 0;
            for (int i = 0; i < _n; i++) linear++;

            int pairs = 0;
            for (int i = 0; i < _n; i++)
                for (int j = i + 1; j < _n; j++)
                    pairs++;

            for (int i = 0; i < MaxN; i++)
            {
                bool active = i < _n;
                float cur = _motes[i].localScale.x;
                float next = Mathf.Lerp(cur, active ? 0.05f : 0.026f, dt * 6f);
                _motes[i].localScale = Vector3.one * next;
            }

            const float scale = 0.0115f;
            SetBar(_linearBar, linear * scale, -0.28f);
            SetBar(_pairsBar, pairs * scale, 0.28f);
        }

        static void SetBar(Transform t, float h, float x)
        {
            h = Mathf.Max(h, 0.01f);
            t.localScale = new Vector3(0.16f, h, 0.16f);
            t.localPosition = new Vector3(x, -0.85f + h, 0f);
        }
    }

    /// <summary>
    /// DATA STRUCTURES — the same eight values, the same question, two very different bills.
    ///
    /// The top row holds eight values in the order they happened to arrive. The bottom row holds
    /// the identical eight values, sorted. Point at a position in the sorted row to name a target
    /// value, and both rows genuinely search for it — the top by checking each element in turn, the
    /// bottom by halving its remaining range every step, exactly like a real binary search. Watching
    /// the top row crawl its full length while the bottom jumps to an answer in two or three hops is
    /// the whole lesson: the values did not change, only their shape did, and the shape is what you
    /// paid for.
    /// </summary>
    [ConceptDemoFor("data-structures")]
    public class DataStructuresDemo : ConceptDemo
    {
        const int N = 8;
        static readonly int[] Shuffled = { 5, 2, 7, 1, 8, 3, 6, 4 };
        static readonly int[] Sorted = { 1, 2, 3, 4, 5, 6, 7, 8 };

        Transform[] _topMotes, _botMotes;
        Material[] _topMats, _botMats;
        Transform _topBar, _botBar;
        int _target = -1;
        int _topStep, _botLo, _botHi, _topChecks, _botChecks;
        bool _topDone, _botDone;
        float _timer;

        public override void Build()
        {
            _topMotes = new Transform[N]; _topMats = new Material[N];
            _botMotes = new Transform[N]; _botMats = new Material[N];

            for (int i = 0; i < N; i++)
            {
                float x = Mathf.Lerp(-0.8f, 0.8f, i / (float)(N - 1));

                _topMats[i] = FlatMaterial(PrismPalette.Spectral((Shuffled[i] - 1) / (float)(N - 1)), 0.32f);
                _topMotes[i] = Body(PrismMesh.Icosphere(1), 0.065f, _topMats[i], $"top{i}");
                _topMotes[i].localPosition = new Vector3(x, 0.42f, 0f);

                _botMats[i] = FlatMaterial(PrismPalette.Spectral((Sorted[i] - 1) / (float)(N - 1)), 0.32f);
                _botMotes[i] = Body(PrismMesh.Icosphere(1), 0.065f, _botMats[i], $"bot{i}");
                _botMotes[i].localPosition = new Vector3(x, -0.34f, 0f);
            }
            _topBar = Body(PrismMesh.Icosphere(1), 1f, FlatMaterial(PrismPalette.Coral, 0.7f), "topBar");
            _botBar = Body(PrismMesh.Icosphere(1), 1f, FlatMaterial(PrismPalette.Mint, 0.7f), "botBar");
        }

        void RestartSearch()
        {
            _topStep = 0; _topChecks = 0; _topDone = false;
            _botLo = 0; _botHi = N - 1; _botChecks = 0; _botDone = false;
            for (int i = 0; i < N; i++)
            {
                _topMats[i].SetFloat("_Luminance", 0.32f);
                _botMats[i].SetFloat("_Luminance", 0.32f);
            }
        }

        protected override void OnTick(float dt)
        {
            // Point at a slot in the SORTED row to name the value that lives there — a spatial way
            // to choose a target rather than an arbitrary number.
            if (TryFreeLocal(out var hand))
            {
                int idx = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(-0.8f, 0.8f, hand.x) * (N - 1)), 0, N - 1);
                int target = Sorted[idx];
                if (target != _target) { _target = target; RestartSearch(); }
            }

            _timer += dt;
            if (_timer >= 0.24f)
            {
                _timer = 0f;

                // Real linear scan: one element examined per tick, in arrival order.
                if (!_topDone && _topStep < N)
                {
                    bool hit = Shuffled[_topStep] == _target;
                    _topMats[_topStep].SetFloat("_Luminance", hit ? 1.15f : 0.75f);
                    _topChecks++;
                    _topStep++;
                    if (hit) _topDone = true;
                }

                // Real binary search: one midpoint comparison per tick, real range halving.
                if (!_botDone && _botLo <= _botHi)
                {
                    int mid = (_botLo + _botHi) / 2;
                    bool hit = Sorted[mid] == _target;
                    _botMats[mid].SetFloat("_Luminance", hit ? 1.15f : 0.75f);
                    _botChecks++;
                    if (hit) _botDone = true;
                    else if (Sorted[mid] < _target) _botLo = mid + 1;
                    else _botHi = mid - 1;
                }
                else if (!_botDone) _botDone = true;
            }

            SetBar(_topBar, _topChecks * 0.09f, -0.5f);
            SetBar(_botBar, _botChecks * 0.09f, 0.5f);
        }

        static void SetBar(Transform t, float h, float x)
        {
            h = Mathf.Max(h, 0.008f);
            t.localScale = new Vector3(0.10f, h, 0.10f);
            t.localPosition = new Vector3(x, -0.92f + h, 0.5f);
        }
    }

    /// <summary>
    /// COMPARISON — one decision, made honestly, over and over.
    ///
    /// A beam balance. The left pan holds a value that changes after every use; the free hand's
    /// height sets the right pan's value continuously. Pinch, and exactly one real comparison
    /// happens right there — the beam tips toward whichever value actually is greater, decided by a
    /// real "<", not an animation of tipping. Every pinch also adds one unit to a small rising
    /// column: cost as a count of decisions made, which is the atom every sort in Algorithm City is
    /// built from.
    /// </summary>
    [ConceptDemoFor("comparison")]
    public class ComparisonDemo : ConceptDemo
    {
        Transform _beam, _leftPan, _rightPan, _leftBall, _rightBall;
        CurveView _spire;
        float _left = 0.4f, _right = 0.5f, _tilt;
        int _count, _lastCount = -1;
        bool _wasPinching;

        public override void Build()
        {
            _beam = Body(PrismMesh.Icosphere(1), 1f, FlatMaterial(PrismPalette.Warm, 0.35f), "beam");
            _beam.localScale = new Vector3(1.4f, 0.03f, 0.03f);
            _leftPan = Body(PrismMesh.Icosphere(1), 0.05f, FlatMaterial(PrismPalette.Warm, 0.3f), "leftPan");
            _rightPan = Body(PrismMesh.Icosphere(1), 0.05f, FlatMaterial(PrismPalette.Warm, 0.3f), "rightPan");
            _leftBall = Ball(0.08f, PrismPalette.Cyan, 0.6f, "leftBall");
            _rightBall = Ball(0.08f, PrismPalette.Gold, 0.6f, "rightBall");
            _spire = Curve(PrismPalette.Mint, 0.02f);
            _left = Random.Range(0.15f, 0.85f);
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand)) _right = Mathf.Clamp01(hand.y * 0.7f + 0.5f);

            bool pinching = FreePinch > 0.6f;
            if (pinching && !_wasPinching)
            {
                // The one real decision this whole demonstration exists to make.
                bool rightWins = _right > _left;
                _tilt = rightWins ? -1f : 1f;
                _count++;
                Voice?.Consonance(transform.position, 0.3f, 1.3f);
                _left = Random.Range(0.15f, 0.85f);   // the next comparison faces a fresh value
            }
            _wasPinching = pinching;

            _tilt = Mathf.MoveTowards(_tilt, 0f, dt * 0.5f);
            _beam.localRotation = Quaternion.Euler(0f, 0f, _tilt * 18f);

            Vector3 leftPos = _beam.localRotation * new Vector3(-0.7f, 0f, 0f);
            Vector3 rightPos = _beam.localRotation * new Vector3(0.7f, 0f, 0f);
            _leftPan.localPosition = leftPos;
            _rightPan.localPosition = rightPos;
            _leftBall.localPosition = leftPos + Vector3.down * 0.12f;
            _rightBall.localPosition = rightPos + Vector3.down * 0.12f;
            _leftBall.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.11f, _left);
            _rightBall.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.11f, _right);

            if (_count != _lastCount)
            {
                _lastCount = _count;
                float h = Mathf.Min(0.9f, _count * 0.03f);
                Segment(_spire, new Vector3(0f, -0.9f, 0.5f), new Vector3(0f, -0.9f + Mathf.Max(h, 0.001f), 0.5f));
            }
        }
    }
}
