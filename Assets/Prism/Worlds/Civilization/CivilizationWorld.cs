using System.Collections.Generic;
using System.Text;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using Prism.Worlds;
using UnityEngine;

namespace Prism.Worlds.Civilization
{
    /// <summary>Evidence keys. Named constants because the gates and the world must agree exactly.</summary>
    public static class CivilizationEvidence
    {
        public const string Touch               = "settlement.touch";
        public const string RouteCreated         = "route.created";
        public const string RouteCut             = "route.cut";
        public const string RouteFlourished      = "route.flourished";
        public const string RouteFailed          = "route.failed";
        public const string RouteEqualized       = "route.equalized";
        public const string Inspected            = "inspected.count";
        public const string NetworkSufficient    = "network.sufficient";
        public const string PredictionMarked     = "prediction.marked";
        public const string PredictionCorrect    = "prediction.correct";
        public const string ResilienceRedundant  = "resilience.redundant";
        public const string ResiliencePassed     = "resilience.passed";
    }

    /// <summary>
    /// The Trade Networks world.
    ///
    /// Nothing here is a person, a nation or a conflict. It is a handful of settlements, each
    /// with a real stock of four goods that rises with production and falls with need, and a
    /// price that emerges from that stock alone. The learner discovers everything by running the
    /// network themselves:
    ///
    ///   Wonder     Five settlements on a table, each a different colour, each surrounded by a
    ///              few small piles of goods. Nothing moves. Nothing is labelled.
    ///   Explore    Pinch near a settlement and drag to another: a route appears and goods start
    ///              to flow along it, coloured by where they came from and where they are going.
    ///              Some routes glow and pulse; some sit dim and barely move at all. Pinch-hold
    ///              an existing route to cut it.
    ///   Discover   Once the learner has personally made BOTH a route that flourished and one
    ///              that failed — the straddle — real numbers appear on approach, and the world
    ///              starts watching for a route it drew levelling out an imbalance.
    ///   Formalize  Names arrive: specialisation, comparative advantage, supply and demand, the
    ///              cost of distance. The one moment this world is built around — connecting the
    ///              two settlements where one out-produces the other at everything, and watching
    ///              them both still gain — gets a single, one-time reveal.
    ///   Apply      Connect the whole network until every settlement is reached by a route that
    ///              is actually flourishing.
    ///   Explain    Two unconnected settlements are highlighted. Mark which one will grow faster
    ///              once joined, draw the route, and watch.
    ///   Create     Build redundancy — every settlement reached by two routes — then cut one
    ///              yourself and watch whether the network holds.
    ///   Connect    Return to the constellation.
    ///
    /// See NOTES.md for the encoding choices, the hand-worked comparative-advantage check, and
    /// what is honest vs approximated.
    /// </summary>
    public class CivilizationWorld : PrismWorldBase
    {
        // ---- layout ----
        const float TableRadius = 0.20f;
        const float BodyRadius = 0.028f;
        const float BeadBaseRadius = 0.014f;
        const float BeadOrbit = 0.052f;

        // ---- interaction ----
        const float SettlementGrabRadius = 0.05f;
        const float SettlementInspectRadius = 0.09f;
        const float PointingConeDegrees = 10f;
        const float PointingRange = 1.2f;
        const float DragMinSpacing = 0.012f;
        const int DragMaxPoints = 64;
        const float DragMinCommitLength = 0.03f;
        const float RouteCutRadius = 0.035f;
        const float RouteCutHoldSeconds = 0.6f;
        const float RouteTubeRadius = 0.0035f;

        // ---- the comparative-advantage pair; see NOTES.md for the arithmetic ----
        const int CaIndexA = 0; // Northpoint: out-produces Southcross at both grain and ore
        const int CaIndexB = 2; // Southcross: absolutely weaker, comparatively better at grain

        public override string WorldId => "civilization";

        /// <summary>Firelight and open country at dusk — the human scale.</summary>
        protected override WorldShellSpec Shell => WorldShellSpec.Hearth;
        public override string PrimaryConceptId => "civilization";
        public override string DisplayName => "Trade Networks";
        public override string[] Shaders => new[] { "Prism/CivilizationRoute" };

        public TradeSim Sim => _sim;

        /// <summary>Set by CivilizationChallenge; read here only to drive a gentle visual pulse.</summary>
        public int HighlightA = -1;
        public int HighlightB = -1;
        public int PredictedIndex = -1;

        TradeSim _sim;
        CivilizationChallenge _challenge;

        class SettlementView
        {
            public Settlement Sim;
            public Transform Body;
            public Material BodyMat;
            public readonly Transform[] Beads = new Transform[Goods.Count];
            public PrismLabel Label;
            public float LabelRefresh;
        }

        class RouteView
        {
            public Route Sim;
            public Transform Tr;
            public Mesh Mesh;
            public Material Mat;
            public float InitialGap;
            public bool FlourishRecorded;
            public bool FailedRecorded;
            public bool EqualizedRecorded;
        }

        readonly List<SettlementView> _settlements = new List<SettlementView>(8);
        readonly List<RouteView> _routes = new List<RouteView>(16);
        readonly HashSet<int> _touchedSettlements = new HashSet<int>();
        readonly HashSet<int> _inspectedSettlements = new HashSet<int>();

        Mesh _bodyMesh, _beadMesh;
        Material _beadMat;

        // ---- drawing a route ----
        readonly List<Vector3> _dragPoints = new List<Vector3>(DragMaxPoints);
        bool _dragging;
        Settlement _dragFrom;
        PrismHands.Hand _dragHand;
        Transform _dragPreviewTr;
        Mesh _dragPreviewMesh;
        Material _dragPreviewMat;

        // ---- cutting a route ----
        bool _cutting;
        RouteView _cuttingTarget;
        PrismHands.Hand _cutHand;
        float _cutHeldFor;

        // ---- the one-time comparative-advantage reveal ----
        bool _caRevealed;
        float _caRevealTimer;

        // -----------------------------------------------------------------
        // construction
        // -----------------------------------------------------------------

        protected override void BuildWorld()
        {
            _sim = new TradeSim();
            BuildGround();
            BuildSettlements();
            BuildDragPreview();

            _challenge = gameObject.AddComponent<CivilizationChallenge>();
            _challenge.World = this;
        }

        void BuildGround()
        {
            // Neutral and low-luminance so it recedes; the settlements are the subject.
            var mat = PrismMaterials.CeramicBody(Color.Lerp(PrismPalette.Warm, PrismPalette.Violet, 0.10f), 0.16f);
            var tr = Body(PrismMesh.Disc(64, 10), TableRadius + 0.10f, mat, "Ground");
            tr.localPosition = Vector3.zero;
        }

        void BuildSettlements()
        {
            _bodyMesh = PrismMesh.Icosphere(2);
            _beadMesh = PrismMesh.Icosphere(1);
            // Neutral: this bead's colour carries no meaning of its own. Identity comes from its
            // fixed position (grain/ore/cloth/salt, always in that compass order) and quantity
            // from its size — the settlement's own body carries the one colour that means
            // something in this world. See NOTES.md.
            _beadMat = PrismMaterials.CeramicBody(Color.Lerp(PrismPalette.Warm, PrismPalette.Gold, 0.12f), 0.28f);

            string[] names = { "Northpoint", "Eastbank", "Southcross", "Westhollow", "Midfield" };

            // Units/second. Fixed geography, chosen and hand-verified in NOTES.md to produce real
            // comparative advantage between Northpoint (index 0) and Southcross (index 2): index 0
            // out-produces index 2 at BOTH grain and ore, yet their production ratios differ, so
            // both still gain from specialising and trading. Order: grain, ore, cloth, salt.
            float[][] production =
            {
                new[] { 3.0f, 2.0f, 0.2f, 0.2f }, // Northpoint: grain and ore
                new[] { 0.3f, 0.2f, 2.5f, 0.2f }, // Eastbank: cloth
                new[] { 1.2f, 0.3f, 0.2f, 0.2f }, // Southcross: grain, weaker than Northpoint at everything
                new[] { 0.3f, 0.2f, 0.2f, 2.2f }, // Westhollow: salt
                new[] { 1.6f, 1.1f, 0.2f, 0.2f }, // Midfield: grain and ore, a second source of both
            };
            float[][] consumption =
            {
                new[] { 3.4f, 0.9f, 1.0f, 1.0f },
                new[] { 0.9f, 0.9f, 0.7f, 1.0f },
                new[] { 0.9f, 0.9f, 1.0f, 1.0f },
                new[] { 0.9f, 0.9f, 1.0f, 0.6f },
                new[] { 0.9f, 0.9f, 1.0f, 1.0f },
            };

            for (int i = 0; i < names.Length; i++)
            {
                float rad = i * 72f * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Sin(rad) * TableRadius, 0f, Mathf.Cos(rad) * TableRadius);
                var sim = _sim.AddSettlement(names[i], pos, production[i], consumption[i]);

                var mat = PrismMaterials.New(PrismMaterials.Seed);
                mat.SetFloat("_Density", 1.0f);
                mat.SetFloat("_FilmNm", 300f + i * 61f);

                var view = new SettlementView { Sim = sim, BodyMat = mat };
                view.Body = Body(_bodyMesh, BodyRadius, mat, "Settlement_" + sim.Id);
                view.Body.localPosition = pos + Vector3.up * BodyRadius;

                for (int g = 0; g < Goods.Count; g++)
                {
                    var bead = Body(_beadMesh, BeadBaseRadius, _beadMat, "Bead_" + Goods.Name((Good)g));
                    float a = g * 90f * Mathf.Deg2Rad;
                    Vector3 off = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * BeadOrbit;
                    bead.localPosition = pos + off + Vector3.up * BeadBaseRadius;
                    view.Beads[g] = bead;
                }

                view.Label = PrismLabel.Create("Label_" + sim.Id, Anchor, Head, 0.012f);
                _settlements.Add(view);
            }
        }

        void BuildDragPreview()
        {
            var go = new GameObject("DragPreview");
            go.transform.SetParent(Anchor, false);
            _dragPreviewMesh = new Mesh { name = "DragPreview" };
            _dragPreviewMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _dragPreviewMesh;
            _dragPreviewMat = PrismMaterials.ForRelation(Relation.Causes, 0.5f);
            _dragPreviewMat.SetFloat("_CoreGain", 0.4f);
            go.AddComponent<MeshRenderer>().sharedMaterial = _dragPreviewMat;
            _dragPreviewTr = go.transform;
            go.SetActive(false);
        }

        // -----------------------------------------------------------------
        // the loop
        // -----------------------------------------------------------------

        protected override void ConfigureLoop(LearningLoop loop)
        {
            loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet approached a settlement",
                e => e.Count(CivilizationEvidence.Touch) >= 1));

            // The gate that matters most, the same shape as orbital mechanics' bound/unbound
            // straddle: only once the learner has personally produced BOTH a route that
            // flourished and one that failed are they ready to be shown why.
            loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet made both a route that flourished and one that failed",
                e => e.Count(CivilizationEvidence.RouteCreated) >= 3
                  && e.Has(CivilizationEvidence.RouteFlourished)
                  && e.Has(CivilizationEvidence.RouteFailed)));

            loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet watched a route it inspected level out an imbalance",
                e => e.Has(CivilizationEvidence.RouteEqualized)
                  && e.Value(CivilizationEvidence.Inspected) >= 2f));

            loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet kept experimenting with the network",
                e => e.Count(CivilizationEvidence.RouteCreated) >= 5));

            loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet connected every settlement to a route that flourishes",
                e => e.Has(CivilizationEvidence.NetworkSufficient)));

            loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet predicted which settlement grows faster and watched it play out",
                e => e.Has(CivilizationEvidence.PredictionCorrect)));

            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet proven a network resilient to a route being cut",
                e => e.Has(CivilizationEvidence.ResiliencePassed)));
        }

        protected override void OnStageEntered(LoopStage stage)
        {
            switch (stage)
            {
                case LoopStage.Apply:
                    _challenge.BeginSufficiency();
                    break;
                case LoopStage.Explain:
                    _challenge.BeginPrediction();
                    break;
                case LoopStage.Create:
                    _challenge.BeginResilience();
                    break;
                case LoopStage.Connect:
                    Knowledge?.Confirm(PrimaryConceptId, 0.4f);
                    Debug.Log("[PRISM] Trade networks: Connect reached.");
                    break;
            }
            base.OnStageEntered(stage);
        }

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        protected override void Tick(float dt)
        {
            HandleInteraction(dt);
            _sim.Advance(dt);
            UpdateSettlementViews(dt);
            UpdateSettlementProximity(dt);
            UpdateRouteViews(dt);
            UpdateComparativeAdvantageReveal(dt);
            _challenge?.Evaluate(dt);
        }

        // -----------------------------------------------------------------
        // interaction: draw a route, or cut one
        // -----------------------------------------------------------------

        void HandleInteraction(float dt)
        {
            if (Hands == null) return;

            if (_dragging) { UpdateDrag(dt); return; }
            if (_cutting) { UpdateCut(dt); return; }

            TryStart(Hands.Left);
            if (!_dragging && !_cutting) TryStart(Hands.Right);
        }

        void TryStart(PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked || !h.PinchDown) return;

            var settlement = FindSettlementNear(h, SettlementGrabRadius, true);
            if (settlement != null)
            {
                _dragging = true;
                _dragHand = h;
                _dragFrom = settlement.Sim;
                _dragPoints.Clear();
                _dragPoints.Add(Anchor.InverseTransformPoint(settlement.Body.position));
                _dragPreviewTr.gameObject.SetActive(true);
                Hands.Buzz(h, 0.2f, 0.04f);
                return;
            }

            var route = FindRouteNear(PrismHands.PointOf(h), RouteCutRadius);
            if (route != null)
            {
                _cutting = true;
                _cutHand = h;
                _cuttingTarget = route;
                _cutHeldFor = 0f;
            }
        }

        void UpdateDrag(float dt)
        {
            var h = _dragHand;
            if (h == null || !h.IsTracked) { CancelDrag(); return; }

            Vector3 localP = Anchor.InverseTransformPoint(PrismHands.PointOf(h));
            Vector3 last = _dragPoints[_dragPoints.Count - 1];
            if ((localP - last).sqrMagnitude >= DragMinSpacing * DragMinSpacing && _dragPoints.Count < DragMaxPoints)
                _dragPoints.Add(localP);

            if (_dragPoints.Count >= 2)
                PrismMesh.Tube(_dragPoints, RouteTubeRadius * 0.7f, 5, _dragPreviewMesh);

            if (h.PinchUp) EndDrag(h);
        }

        void EndDrag(PrismHands.Hand h)
        {
            _dragging = false;
            _dragPreviewTr.gameObject.SetActive(false);

            var end = FindSettlementNear(h, SettlementGrabRadius, true);
            float length = 0f;
            for (int i = 1; i < _dragPoints.Count; i++) length += Vector3.Distance(_dragPoints[i - 1], _dragPoints[i]);

            if (end != null && end.Sim != _dragFrom && length >= DragMinCommitLength)
            {
                _dragPoints[_dragPoints.Count - 1] = Anchor.InverseTransformPoint(end.Body.position);
                CommitRoute(_dragFrom, end.Sim, _dragPoints);
                Hands.Clunk(h, 0.4f);
            }
            else
            {
                Hands.Buzz(h, 0.15f, 0.03f);
            }

            _dragFrom = null;
            _dragHand = null;
        }

        void CancelDrag()
        {
            _dragging = false;
            _dragPreviewTr.gameObject.SetActive(false);
            _dragFrom = null;
            _dragHand = null;
        }

        void CommitRoute(Settlement a, Settlement b, List<Vector3> points)
        {
            var existing = _sim.FindRoute(a, b);
            RouteView view;
            if (existing != null)
            {
                existing.SetPath(points);
                view = FindRouteView(existing);
                RebuildRouteMesh(view);
            }
            else
            {
                var route = new Route { A = a, B = b };
                route.SetPath(points);
                _sim.Routes.Add(route);
                view = BuildRouteView(route);
                _routes.Add(view);
            }

            view.InitialGap = Mathf.Abs(a.SurplusIndex - b.SurplusIndex);
            view.FlourishRecorded = false;
            view.FailedRecorded = false;
            view.EqualizedRecorded = false;

            Loop.Evidence.Record(CivilizationEvidence.RouteCreated);
        }

        RouteView BuildRouteView(Route route)
        {
            var go = new GameObject("Route_" + route.A.Id + "_" + route.B.Id);
            go.transform.SetParent(Anchor, false);
            var mesh = new Mesh { name = "Route" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mat = PrismMaterials.New("Prism/CivilizationRoute");
            mat.SetFloat("_Packets", 5f);
            mat.SetFloat("_Speed", 0.5f);
            mat.SetFloat("_Width", 0.18f);
            mat.SetFloat("_CoreGain", 0.55f);
            mat.SetFloat("_FlowRef", 3f);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            var view = new RouteView { Sim = route, Tr = go.transform, Mesh = mesh, Mat = mat };
            RebuildRouteMesh(view);
            return view;
        }

        void RebuildRouteMesh(RouteView view) => PrismMesh.Tube(view.Sim.Path, RouteTubeRadius, 6, view.Mesh);

        RouteView FindRouteView(Route route)
        {
            for (int i = 0; i < _routes.Count; i++) if (_routes[i].Sim == route) return _routes[i];
            return null;
        }

        void UpdateCut(float dt)
        {
            var h = _cutHand;
            if (h == null || !h.IsTracked || h.Pinch < 0.5f) { CancelCut(); return; }

            Vector3 local = Anchor.InverseTransformPoint(PrismHands.PointOf(h));
            var path = _cuttingTarget.Sim.Path;
            float minD = float.MaxValue;
            for (int p = 1; p < path.Count; p++)
                minD = Mathf.Min(minD, DistancePointToSegment(local, path[p - 1], path[p]));

            if (minD > RouteCutRadius * 1.6f) { CancelCut(); return; }

            _cutHeldFor += dt;
            if (_cutHeldFor >= RouteCutHoldSeconds)
            {
                var target = _cuttingTarget;
                CancelCut();
                SeverRoute(target);
                Hands.Clunk(h, 0.5f);
            }
        }

        void CancelCut()
        {
            _cutting = false;
            _cuttingTarget = null;
            _cutHand = null;
            _cutHeldFor = 0f;
        }

        void SeverRoute(RouteView view)
        {
            _sim.Routes.Remove(view.Sim);
            _routes.Remove(view);
            if (view.Tr != null) Destroy(view.Tr.gameObject);
            if (view.Mesh != null) Destroy(view.Mesh);
            if (view.Mat != null) Destroy(view.Mat);
            Loop.Evidence.Record(CivilizationEvidence.RouteCut);
            _challenge?.NotifyRouteCut();
        }

        /// <summary>Nearest settlement to a specific hand: direct proximity first, then — because
        /// nothing in this product may be out of reach — a narrow pointing cone as a fallback.</summary>
        SettlementView FindSettlementNear(PrismHands.Hand h, float radius, bool allowPointing)
        {
            Vector3 p = PrismHands.PointOf(h);
            SettlementView best = null;
            float bestD = radius * radius;
            for (int i = 0; i < _settlements.Count; i++)
            {
                float d = (_settlements[i].Body.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = _settlements[i]; }
            }
            if (best != null || !allowPointing || !h.ReachValid) return best;

            float cos = Mathf.Cos(PointingConeDegrees * Mathf.Deg2Rad);
            float bestScore = -1f;
            for (int i = 0; i < _settlements.Count; i++)
            {
                Vector3 to = _settlements[i].Body.position - h.Reach.origin;
                float dist = to.magnitude;
                if (dist < 1e-4f || dist > PointingRange) continue;
                float align = Vector3.Dot(h.Reach.direction, to / dist);
                if (align < cos) continue;
                float score = align - dist * 0.02f;
                if (score > bestScore) { bestScore = score; best = _settlements[i]; }
            }
            return best;
        }

        RouteView FindRouteNear(Vector3 worldPoint, float radius)
        {
            Vector3 local = Anchor.InverseTransformPoint(worldPoint);
            RouteView best = null;
            float bestD = radius;
            for (int i = 0; i < _routes.Count; i++)
            {
                var path = _routes[i].Sim.Path;
                for (int p = 1; p < path.Count; p++)
                {
                    float d = DistancePointToSegment(local, path[p - 1], path[p]);
                    if (d < bestD) { bestD = d; best = _routes[i]; }
                }
            }
            return best;
        }

        static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-9f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return Vector3.Distance(p, a + ab * t);
        }

        // -----------------------------------------------------------------
        // view sync
        // -----------------------------------------------------------------

        /// <summary>
        /// COLOUR LAW, the one meaningful colour axis in this world: each settlement's body is
        /// tinted by Spectral(0.5 + 0.5*SurplusIndex) — shortage sits low on the ramp (cyan/mint,
        /// thin and cold), balance sits in the middle (lavender), surplus sits high (coral/gold/
        /// violet, warm and full). It is visible from Wonder onward, before anything is named,
        /// exactly the way Orbital's trail colour shows speed before Kepler's second law is
        /// named. The same colour reappears as the gradient along a route in
        /// Prism/CivilizationRoute. Prosperity — this world's other emergent quantity — is shown
        /// twice over: as Prism/Seed's own crystallisation (_Growth) and as a gentle change in
        /// the settlement's actual size.
        /// </summary>
        void UpdateSettlementViews(float dt)
        {
            for (int i = 0; i < _settlements.Count; i++)
            {
                var v = _settlements[i];
                var s = v.Sim;

                float t = 0.5f + 0.5f * s.SurplusIndex;
                v.BodyMat.SetColor("_Tint", PrismPalette.Spectral(t));

                float growth = Mathf.InverseLerp(TradeSim.ProsperityMin, TradeSim.ProsperityMax, s.Prosperity);
                v.BodyMat.SetFloat("_Growth", growth);

                float hover = (i == HighlightA || i == HighlightB) ? (0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f)) : 0f;
                v.BodyMat.SetFloat("_Hover", hover);
                v.BodyMat.SetFloat("_Held", i == PredictedIndex ? 1f : 0f);

                float scale = BodyRadius * Mathf.Lerp(0.78f, 1.25f, growth);
                v.Body.localScale = Vector3.one * scale;

                for (int g = 0; g < Goods.Count; g++)
                {
                    float sizeFactor = Mathf.Sqrt(Mathf.Clamp(s.Stock[g] / TradeSim.ReferenceStock, 0.05f, 4f));
                    v.Beads[g].localScale = Vector3.one * (BeadBaseRadius * sizeFactor);
                }
            }
        }

        void UpdateSettlementProximity(float dt)
        {
            for (int i = 0; i < _settlements.Count; i++)
            {
                var v = _settlements[i];
                bool near = NearestHand(v.Body.position, SettlementInspectRadius) != null;

                if (near && _touchedSettlements.Add(i))
                    Loop.Evidence.Record(CivilizationEvidence.Touch);

                if (near && Loop.Stage >= LoopStage.Discover && _inspectedSettlements.Add(i))
                    Loop.Evidence.Set(CivilizationEvidence.Inspected, _inspectedSettlements.Count);

                bool showLabel = near && Loop.Stage >= LoopStage.Discover;
                v.Label.Show(showLabel);
                if (!showLabel) continue;

                v.Label.PlaceAbove(v.Body.position, BodyRadius + 0.038f);
                v.LabelRefresh -= dt;
                if (v.LabelRefresh <= 0f)
                {
                    v.LabelRefresh = 0.25f; // throttled: text is rebuilt at most 4x/s, only while inspected
                    v.Label.SetText(BuildLabelText(i), PrismPalette.Spectral(0.5f + 0.5f * v.Sim.SurplusIndex));
                }
            }
        }

        string BuildLabelText(int index)
        {
            if (index == CaIndexA && _caRevealTimer > 0f)
                return "comparative advantage\nNorthpoint outmakes Southcross at everything\nyet both gain: Southcross sells grain, buys ore";

            var s = _settlements[index].Sim;
            bool named = Loop.Stage >= LoopStage.Formalize;
            var sb = new StringBuilder(64);

            if (named)
            {
                int best = 0;
                for (int g = 1; g < Goods.Count; g++) if (s.Production[g] > s.Production[best]) best = g;
                sb.Append("specialises in ").Append(Goods.Name((Good)best)).Append('\n');
            }

            for (int g = 0; g < Goods.Count; g++)
            {
                sb.Append(Goods.Name((Good)g)).Append(' ').Append(Mathf.RoundToInt(s.Stock[g]));
                if (named) sb.Append(s.SurplusOf((Good)g) >= 0f ? " surplus" : " short");
                if (g < Goods.Count - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Per-route bookkeeping: the gradient (settlement colours), the two flow streams the
        /// shader animates, and the three emergent evidence events — flourished, failed,
        /// equalized — each recorded exactly once, the moment the real simulation actually
        /// produces them.
        /// </summary>
        void UpdateRouteViews(float dt)
        {
            for (int i = 0; i < _routes.Count; i++)
            {
                var v = _routes[i];
                var r = v.Sim;

                v.Mat.SetColor("_TintA", PrismPalette.Spectral(0.5f + 0.5f * r.A.SurplusIndex));
                v.Mat.SetColor("_TintB", PrismPalette.Spectral(0.5f + 0.5f * r.B.SurplusIndex));

                float posFlow = 0f, negFlow = 0f;
                for (int g = 0; g < Goods.Count; g++)
                {
                    if (r.Flow[g] >= 0f) posFlow += r.Flow[g]; else negFlow -= r.Flow[g];
                }
                v.Mat.SetFloat("_FlowPos", posFlow);
                v.Mat.SetFloat("_FlowNeg", negFlow);

                float starvedT = Mathf.Clamp01(r.StarvedFor / TradeSim.StarvedSustain);
                if (v == _cuttingTarget) starvedT = Mathf.Max(starvedT, _cutHeldFor / RouteCutHoldSeconds);
                v.Mat.SetFloat("_Starved", starvedT);

                if (r.IsFlourishing && !v.FlourishRecorded)
                {
                    v.FlourishRecorded = true;
                    Loop.Evidence.Record(CivilizationEvidence.RouteFlourished);
                }
                if (r.IsStarved && !v.FailedRecorded)
                {
                    v.FailedRecorded = true;
                    Loop.Evidence.Record(CivilizationEvidence.RouteFailed);
                }
                if (!v.EqualizedRecorded)
                {
                    float gapNow = Mathf.Abs(r.A.SurplusIndex - r.B.SurplusIndex);
                    if (v.InitialGap > 0.25f && gapNow < v.InitialGap * 0.4f)
                    {
                        v.EqualizedRecorded = true;
                        Loop.Evidence.Record(CivilizationEvidence.RouteEqualized);
                    }
                }
            }
        }

        /// <summary>The one scripted moment in this world, and even it is gated on a real action:
        /// the learner has to actually connect Northpoint and Southcross before it plays.</summary>
        void UpdateComparativeAdvantageReveal(float dt)
        {
            if (!_caRevealed && Loop.Stage >= LoopStage.Formalize)
            {
                var a = _settlements[CaIndexA].Sim;
                var b = _settlements[CaIndexB].Sim;
                if (_sim.FindRoute(a, b) != null) { _caRevealed = true; _caRevealTimer = 8f; }
            }
            if (_caRevealTimer > 0f)
            {
                _caRevealTimer -= dt;
                _settlements[CaIndexA].LabelRefresh = 0f;
            }
        }

        // -----------------------------------------------------------------
        // queries used by CivilizationChallenge
        // -----------------------------------------------------------------

        public Vector3 SettlementWorldPos(int index) => _settlements[index].Body.position;

        // -----------------------------------------------------------------
        // concepts
        // -----------------------------------------------------------------

        public override IEnumerable<ConceptSpec> Concepts
        {
            get
            {
                yield return new ConceptSpec
                {
                    Id = "civilization",
                    Title = "Trade Networks",
                    Domain = ConceptDomain.Society,
                    Kind = KnowledgeKind.System,
                    Direction = WedgeDir(328f, 0.020f),
                    Distance = 3.6f,
                    WorldId = "civilization",
                    Capability = "Read a settlement's colour as real surplus or shortage, and connect a network so every settlement gets what it lacks.",
                    Formalisation = "A market of settlements, each holding a real stock of goods that rises with production and falls with need. Price rises as stock falls. Goods move, along a route the learner draws, from wherever a good is cheap toward wherever it is dear, at a rate set by whatever is left of that price gap once the cost of the route itself is paid.",
                    Links = new (string, Relation, float)[]
                    {
                        ("specialisation", Relation.Composes, 0.8f),
                        ("supply-and-demand", Relation.Composes, 0.85f),
                        ("networks", Relation.Instantiates, 0.7f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "specialisation",
                    Title = "Specialisation",
                    Domain = ConceptDomain.Society,
                    Kind = KnowledgeKind.Theory,
                    Direction = WedgeDir(336f, -0.024f),
                    Distance = 4.1f,
                    Capability = "Predict which good a settlement will end up trading away, once it is connected.",
                    Formalisation = "A settlement that produces far more of one good than it needs, and far less of another, gains by trading the first for the second rather than trying to make both.",
                    Links = new (string, Relation, float)[]
                    {
                        ("comparative-advantage", Relation.Causes, 0.9f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "comparative-advantage",
                    Title = "Comparative Advantage",
                    Domain = ConceptDomain.Society,
                    Kind = KnowledgeKind.Theory,
                    Direction = WedgeDir(344f, 0.028f),
                    Distance = 4.6f,
                    Capability = "Explain why a settlement that is worse at making everything can still have something worth trading.",
                    Formalisation = "What a settlement should make is set not by how much of a good it can produce, but by how much of some OTHER good it gives up to produce it. Two settlements can both gain from trade even when one out-produces the other at everything.",
                    Links = System.Array.Empty<(string, Relation, float)>()
                };

                yield return new ConceptSpec
                {
                    Id = "supply-and-demand",
                    Title = "Supply and Demand",
                    Domain = ConceptDomain.Society,
                    Kind = KnowledgeKind.Theory,
                    Direction = WedgeDir(352f, -0.012f),
                    Distance = 5.0f,
                    Capability = "Read a price the way this world sets one: rising as a good runs short, falling as it piles up.",
                    Formalisation = "Price is not announced by anyone; it is what a settlement's own stock implies. Scarcity raises it, surplus lowers it, and the gap between two settlements' prices is what pays for a trade route.",
                    Links = new (string, Relation, float)[]
                    {
                        ("comparative-advantage", Relation.Measures, 0.75f),
                        ("feedback", Relation.Analogy, 0.8f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "networks",
                    Title = "Networks",
                    Domain = ConceptDomain.Society,
                    Kind = KnowledgeKind.System,
                    Direction = WedgeDir(358f, 0.013f),
                    Distance = 3.9f,
                    Capability = "Build redundancy: a network where losing one connection does not isolate anyone.",
                    Formalisation = "Settlements are nodes; routes are edges with a real cost. What can flow across the whole network depends on its shape, not only on any one route.",
                    Links = System.Array.Empty<(string, Relation, float)>()
                };
            }
        }

        /// <summary>A direction vector from an azimuth (degrees, clockwise from straight ahead)
        /// and a small elevation ratio, matching how ConstellationNode places direction*distance.</summary>
        static Vector3 WedgeDir(float azimuthDeg, float elevationRatio)
        {
            float rad = azimuthDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), elevationRatio, Mathf.Cos(rad)).normalized;
        }
    }
}
