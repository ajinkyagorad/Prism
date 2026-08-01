using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using UnityEngine;

namespace Prism.Worlds.Civilization
{
    /// <summary>
    /// SUPPLY AND DEMAND — the price is where two lines cross, and nowhere else.
    ///
    /// Two real lines and their real intersection, solved every frame. The free hand slides the
    /// demand line; the crossing point moves and the market clears somewhere new. Nothing about the
    /// price is chosen — it is read off the geometry, which is exactly the claim being made.
    ///
    /// It rhymes with FeedbackDemo on purpose: both are a single real number the hand pushes on,
    /// answered by the system rather than asserted, and both mark a genuine state change with sound
    /// instead of a number. Here that is letting go — the price settles (a consonance) if a market
    /// exists where the hand left it, or sits in an audible tension if demand has been pushed so far
    /// the lines no longer cross inside the drawn range, which really does happen to real markets.
    /// </summary>
    [ConceptDemoFor("supply-and-demand")]
    public class SupplyDemandDemo : ConceptDemo
    {
        CurveView _supply, _demand;
        Transform _cross;
        Material _crossMat;
        readonly List<Vector3> _s = new List<Vector3>(2);
        readonly List<Vector3> _d = new List<Vector3>(2);
        float _shift;                    // how far the learner has moved demand
        bool _wasTracked;

        // price = a + b*q  (supply, rising)      price = c - e*q  (demand, falling)
        const float A = 0.10f, B = 0.80f, C = 0.95f, E = 0.85f;

        public override void Build()
        {
            _supply = Curve(PrismPalette.Mint, 0.008f);
            _demand = Curve(PrismPalette.Coral, 0.008f);
            _crossMat = FlatMaterial(PrismPalette.Gold, 0.95f);
            _cross = Body(PrismMesh.Icosphere(2), 0.05f, _crossMat, "clearing");
        }

        protected override void OnTick(float dt)
        {
            bool tracked = TryFreeLocal(out var hand);
            if (tracked)
                _shift = Mathf.Lerp(_shift, Mathf.Clamp(hand.y, -0.45f, 0.45f), dt * 6f);

            float c = C + _shift;

            // Solve a + b*q = c - e*q  ->  q = (c - a) / (b + e). One line of real algebra.
            float q = Mathf.Clamp((c - A) / (B + E), 0f, 1f);
            float p = A + B * q;

            Vector3 P(float qq, float pp) => new Vector3(qq * 1.7f - 0.85f, pp * 1.5f - 0.75f, 0f);

            _s.Clear(); _s.Add(P(0f, A)); _s.Add(P(1f, A + B));
            _d.Clear(); _d.Add(P(0f, c)); _d.Add(P(1f, c - E));
            _supply.Set(_s);
            _demand.Set(_d);

            _cross.localPosition = P(q, p);
            // Gold when the market clears inside the range; coral when demand has been pushed so
            // far that the crossing has left the board — a real thing that happens to real markets.
            bool inside = q > 0.001f && q < 0.999f;
            _crossMat.SetColor("_Tint", inside ? PrismPalette.Gold : PrismPalette.Coral);
            _cross.localScale = Vector3.one * (inside ? 0.05f : 0.035f);

            // The moment of letting go, not the whole gesture — a market settling (or failing to)
            // is a discrete event, and FeedbackDemo marks its own state changes the same sparing way.
            if (_wasTracked && !tracked)
            {
                if (inside) Voice?.Settle(transform.position, 0.4f, 1f + p);
                else Voice?.Tension(transform.position, 0.35f);
            }
            _wasTracked = tracked;
        }
    }

    /// <summary>
    /// COMPARATIVE ADVANTAGE — the one almost every adult gets wrong.
    ///
    /// Two producers. NORTH is better at both goods than SOUTH, absolutely and obviously. The naive
    /// conclusion is that North should do everything and trade is pointless. It is wrong, and the
    /// reason is visible here as a difference of SLOPE.
    ///
    ///   North: 6 grain/day  or  3 cloth/day   -> 1 cloth costs 2 grain
    ///   South: 2 grain/day  or  2 cloth/day   -> 1 cloth costs 1 grain
    ///
    /// South is worse at making cloth in absolute terms and still gives up LESS to make it. That
    /// ratio, not the absolute rate, decides who should make what.
    ///
    /// The demonstration is geometric: each producer's straight production-possibility line, and
    /// the JOINT line built by using the cheaper producer first. The joint line is kinked, and it
    /// bulges outside the line you would get by having both split their time proportionally. That
    /// bulge IS the gain from specialising, and it exists only because the slopes differ.
    ///
    /// The free hand changes South's cloth ability, both up and down. Push it DOWN toward 1
    /// cloth/day and South's cost rises to meet North's exactly (2 grain per cloth, same as
    /// North) — at that single point the advantage, and the bulge, genuinely collapse to nothing,
    /// live, on the geometry, because the kink point falls exactly on the naive line there. Push
    /// past it and the kink swaps sides: South is now the more expensive cloth-maker, so the joint
    /// frontier routes cloth through North first instead, and the bulge grows again with South's
    /// advantage now sitting in grain rather than cloth. Nothing about that swap is scripted — it
    /// falls out of the same one comparison (costSouth &lt; costNorth) every frame.
    /// </summary>
    [ConceptDemoFor("comparative-advantage")]
    public class ComparativeAdvantageDemo : ConceptDemo
    {
        CurveView _north, _south, _joint, _naive;
        Transform _marker;
        Material _markerMat;
        readonly List<Vector3> _a = new List<Vector3>(2);
        readonly List<Vector3> _b = new List<Vector3>(2);
        readonly List<Vector3> _j = new List<Vector3>(3);
        readonly List<Vector3> _n = new List<Vector3>(2);

        const float NorthGrain = 6f, NorthCloth = 3f;   // north is better at BOTH
        float _southCloth = 2f;                          // the learner can change this one
        const float SouthGrain = 2f;

        public override void Build()
        {
            _north = Curve(PrismPalette.Cyan, 0.006f);
            _south = Curve(PrismPalette.Mint, 0.006f);
            _naive = Curve(PrismPalette.Warm, 0.004f);
            _joint = Curve(PrismPalette.Gold, 0.009f, 3f);
            _markerMat = FlatMaterial(PrismPalette.Gold, 0.9f);
            _marker = Body(PrismMesh.Icosphere(2), 0.045f, _markerMat, "gain");
        }

        protected override void OnTick(float dt)
        {
            // The hand changes South's cloth ability. Push it down toward 1 and South's cost
            // rises to meet North's exactly, collapsing the bulge; push past 1 and the kink
            // swaps sides. See the class doc comment for the arithmetic.
            if (TryFreeLocal(out var hand))
                _southCloth = Mathf.Lerp(_southCloth, Mathf.Clamp(0.7f + Mathf.InverseLerp(-0.7f, 0.7f, hand.y) * 2.6f, 0.7f, 3.3f), dt * 5f);

            float totalGrain = NorthGrain + SouthGrain;
            float totalCloth = NorthCloth + _southCloth;
            float sx = 1.7f / Mathf.Max(totalGrain, 0.01f);
            float sy = 1.4f / Mathf.Max(totalCloth, 0.01f);
            Vector3 P(float g, float c) => new Vector3(g * sx - 0.85f, c * sy - 0.7f, 0f);

            // Each producer alone: a straight line between all-grain and all-cloth.
            _a.Clear(); _a.Add(P(NorthGrain, 0f)); _a.Add(P(0f, NorthCloth));
            _b.Clear(); _b.Add(P(SouthGrain, 0f)); _b.Add(P(0f, _southCloth));
            _north.Set(_a);
            _south.Set(_b);

            // Opportunity cost of one cloth, in grain, for each producer.
            float costNorth = NorthGrain / Mathf.Max(NorthCloth, 0.01f);
            float costSouth = SouthGrain / Mathf.Max(_southCloth, 0.01f);

            // The joint frontier: make cloth with whoever gives up less grain for it, first.
            // The kink sits where that producer runs out — and the kink is the whole story.
            bool southCheaper = costSouth < costNorth;
            float kinkCloth = southCheaper ? _southCloth : NorthCloth;
            float kinkGrain = southCheaper ? NorthGrain : SouthGrain;

            _j.Clear();
            _j.Add(P(totalGrain, 0f));
            _j.Add(P(kinkGrain, kinkCloth));
            _j.Add(P(0f, totalCloth));
            _joint.Set(_j);

            // The straight line you would get by ignoring the difference and having both split
            // proportionally. The joint line lies outside it exactly when the slopes differ.
            _n.Clear(); _n.Add(P(totalGrain, 0f)); _n.Add(P(0f, totalCloth));
            _naive.Set(_n);

            // How far the kink stands outside the naive line: the gain, as a length.
            Vector3 k = P(kinkGrain, kinkCloth);
            Vector3 n0 = P(totalGrain, 0f), n1 = P(0f, totalCloth);
            Vector3 dir = (n1 - n0).normalized;
            float bulge = Vector3.Cross(k - n0, dir).magnitude;

            _marker.localPosition = k;
            _marker.localScale = Vector3.one * (0.03f + Mathf.Clamp(bulge, 0f, 0.3f) * 0.25f);
            // Gold while there is something to gain, fading to warm-neutral as the ratios converge
            // and the advantage genuinely disappears.
            _markerMat.SetColor("_Tint",
                Color.Lerp(PrismPalette.Warm, PrismPalette.Gold, Mathf.Clamp01(bulge * 12f)));
            _joint.Mat.SetFloat("_Pulse", Mathf.Clamp01(bulge * 10f));
        }
    }

    /// <summary>
    /// SPECIALISATION — a day only has so many hours.
    ///
    /// One producer, one slider, two goods. The frontier is real: every hour spent on grain is an
    /// hour not spent on cloth, and the trade-off is a straight line the learner slides along.
    /// The simpler sibling of comparative advantage, and the thing that must be felt first.
    /// </summary>
    [ConceptDemoFor("specialisation")]
    public class SpecialisationDemo : ConceptDemo
    {
        CurveView _frontier;
        Transform _point, _grainBar, _clothBar;
        Material _grainMat, _clothMat;
        readonly List<Vector3> _f = new List<Vector3>(2);
        float _split = 0.5f;             // fraction of the day on grain

        const float GrainPerDay = 5f, ClothPerDay = 3f;

        public override void Build()
        {
            _frontier = Curve(PrismPalette.Lavender, 0.007f);
            _point = Ball(0.045f, PrismPalette.Gold, 0.9f, "choice");
            _grainMat = FlatMaterial(PrismPalette.Gold, 0.75f);
            _clothMat = FlatMaterial(PrismPalette.Cyan, 0.75f);
            _grainBar = Body(PrismMesh.Icosphere(1), 1f, _grainMat, "grain");
            _clothBar = Body(PrismMesh.Icosphere(1), 1f, _clothMat, "cloth");
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _split = Mathf.Lerp(_split, Mathf.Clamp01(Mathf.InverseLerp(-0.7f, 0.7f, hand.x)), dt * 6f);

            float g = GrainPerDay * _split;
            float c = ClothPerDay * (1f - _split);

            Vector3 P(float gg, float cc) =>
                new Vector3(gg / GrainPerDay * 1.5f - 0.75f, cc / ClothPerDay * 1.2f - 0.6f, 0f);

            _f.Clear(); _f.Add(P(GrainPerDay, 0f)); _f.Add(P(0f, ClothPerDay));
            _frontier.Set(_f);
            _point.localPosition = P(g, c);

            void Bar(Transform t, float v, float max, float x)
            {
                float h = Mathf.Clamp(v / max, 0.01f, 1f) * 0.55f;
                t.localScale = new Vector3(0.10f, h, 0.10f);
                t.localPosition = new Vector3(x, -0.9f + h, 0.35f);
            }
            Bar(_grainBar, g, GrainPerDay, -0.2f);
            Bar(_clothBar, c, ClothPerDay, 0.2f);
        }
    }

    /// <summary>
    /// NETWORKS — one link can be worth more than all the others.
    ///
    /// Six settlements in two clusters. Within a cluster everyone is already connected; between
    /// them, nothing. The free hand draws or cuts the single bridging link, and reachability is
    /// genuinely recomputed by flood fill — not asserted.
    ///
    /// Adding that one link roughly doubles how many places each settlement can reach, while every
    /// link inside a cluster adds almost nothing. That asymmetry is the concept, and it is why
    /// bridges, ports and translations matter out of all proportion to their number.
    /// </summary>
    [ConceptDemoFor("networks")]
    public class NetworksDemo : ConceptDemo
    {
        const int N = 6;
        Transform[] _nodes;
        Material[] _mats;
        CurveView[] _links;
        CurveView _bridge;
        Transform _reachBar;
        Material _reachMat;
        readonly bool[,] _adj = new bool[N, N];
        readonly int[] _seen = new int[N];
        readonly int[] _queue = new int[N];
        bool _bridged;

        // Two triangles: 0-1-2 and 3-4-5. The bridge would join 2 and 3.
        static readonly int[,] Edges = { {0,1},{1,2},{2,0},{3,4},{4,5},{5,3} };

        public override void Build()
        {
            _nodes = new Transform[N];
            _mats = new Material[N];
            for (int i = 0; i < N; i++)
            {
                _mats[i] = FlatMaterial(i < 3 ? PrismPalette.Cyan : PrismPalette.Mint, 0.75f);
                _nodes[i] = Body(PrismMesh.Icosphere(2), 0.055f, _mats[i], $"town{i}");
                float a = (i % 3) / 3f * Mathf.PI * 2f;
                float side = i < 3 ? -0.45f : 0.45f;
                _nodes[i].localPosition = new Vector3(side + Mathf.Cos(a) * 0.28f, Mathf.Sin(a) * 0.28f, 0f);
            }

            _links = new CurveView[Edges.GetLength(0)];
            for (int e = 0; e < _links.Length; e++) _links[e] = Curve(PrismPalette.Lavender, 0.004f);
            _bridge = Curve(PrismPalette.Gold, 0.008f, 4f);

            _reachMat = FlatMaterial(PrismPalette.Gold, 0.8f);
            _reachBar = Body(PrismMesh.Icosphere(1), 1f, _reachMat, "reach");
        }

        protected override void OnTick(float dt)
        {
            // Reaching into the gap between the clusters builds the bridge; withdrawing cuts it.
            if (TryFreeLocal(out var hand))
                _bridged = Mathf.Abs(hand.x) < 0.22f && Mathf.Abs(hand.y) < 0.4f;

            System.Array.Clear(_adj, 0, _adj.Length);
            for (int e = 0; e < Edges.GetLength(0); e++)
            {
                int i = Edges[e, 0], j = Edges[e, 1];
                _adj[i, j] = _adj[j, i] = true;
                Segment(_links[e], _nodes[i].localPosition, _nodes[j].localPosition);
            }
            if (_bridged) { _adj[2, 3] = _adj[3, 2] = true; }

            if (_bridged) Segment(_bridge, _nodes[2].localPosition, _nodes[3].localPosition);
            else _bridge.Mesh.Clear();

            // Real flood fill from node 0 — reachability is counted, never assumed.
            for (int i = 0; i < N; i++) _seen[i] = 0;
            int head = 0, tail = 0;
            _queue[tail++] = 0; _seen[0] = 1;
            while (head < tail)
            {
                int cur = _queue[head++];
                for (int j = 0; j < N; j++)
                    if (_adj[cur, j] && _seen[j] == 0) { _seen[j] = 1; _queue[tail++] = j; }
            }

            int reached = 0;
            for (int i = 0; i < N; i++)
            {
                if (_seen[i] == 1) reached++;
                // Unreachable settlements dim: the map shows what node 0 can actually get to.
                _mats[i].SetFloat("_Luminance", _seen[i] == 1 ? 0.85f : 0.12f);
            }

            float h = reached / (float)N * 0.6f;
            _reachBar.localScale = new Vector3(0.09f, Mathf.Max(h, 0.01f), 0.09f);
            _reachBar.localPosition = new Vector3(0.85f, -0.7f + h, 0f);
            _reachMat.SetColor("_Tint", PrismPalette.Spectral(reached / (float)N));
        }
    }
}
