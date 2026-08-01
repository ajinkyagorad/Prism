using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Atrium
{
    /// <summary>
    /// The Knowledge Atrium — PRISM's home.
    ///
    /// There is no home screen, no menu and no list. The learner stands inside their own
    /// understanding: an immense calm white space with their constellation floating around them.
    /// Every affordance is bodily.
    ///
    ///   reach toward a concept   it comes to your hand
    ///   pull two together        their relationship reveals itself — or refuses to
    ///   hold one to your chest   you enter the world that teaches it
    ///
    /// The refusal matters as much as the reveal. Bringing together two concepts that are not
    /// related produces spatial tension and a slow beating dissonance, not an error. A learner is
    /// allowed to propose a connection and find out it does not hold; that is what the space is
    /// for.
    /// </summary>
    public class KnowledgeAtrium : MonoBehaviour
    {
        [Header("Wiring")]
        public PrismHands Hands;
        public Camera Head;
        public ConceptGraph Graph;

        [Header("Feel")]
        [Tooltip("Metres. A concept this close to the hand can be taken directly.")]
        public float TouchRadius = 0.16f;

        [Tooltip("Degrees. A concept within this cone of where the hand points can be reached for at any distance.")]
        public float ReachConeDegrees = 11f;

        [Tooltip("Metres. Furthest a concept can be reached for.")]
        public float ReachRange = 6f;

        [Tooltip("Metres. Two held concepts closer than this are being proposed as related.")]
        public float ProposeDistance = 0.11f;

        [Tooltip("Metres from the head. Holding a concept this close enters its world.")]
        public float EnterDistance = 0.28f;

        [Header("Hand presence (assigned by PrismSceneBuilder)")]
        public PrismHandCursor LeftCursor;
        public PrismHandCursor RightCursor;

        public KnowledgeState Knowledge { get; set; }
        public Companion.PrismCompanion Companion { get; set; }

        /// <summary>Raised with a world id when the learner enters a world bodily.</summary>
        public event System.Action<string> WorldEntered;

        readonly List<ConstellationNode> _nodes = new List<ConstellationNode>();
        readonly Dictionary<string, LinkView> _links = new Dictionary<string, LinkView>();

        Transform _constellation;
        PrismLabel _leftLabel, _rightLabel;
        PrismLabel _hint;
        bool _hintRetired;

        // One live demonstration per hand. Holding a concept blooms the phenomenon it stands for;
        // releasing it lets the phenomenon fade and destroy itself.
        ConceptDemo _demoLeft, _demoRight;
        ConstellationNode _heldLeft, _heldRight;
        float _proposeHeldFor;
        string _lastProposal;
        readonly HashSet<ConstellationNode> _hovered = new HashSet<ConstellationNode>();
        readonly List<Vector3> _linkPts = new List<Vector3>(20);
        bool _placed;

        class LinkView
        {
            public Transform View;
            public Material Mat;
            public Mesh Mesh;
            public ConstellationNode A, B;
            public Relation Relation;
            public bool Real;
        }

        void Awake()
        {
            // The sky is no longer the atrium's job — PrismEnvironment owns the landscape now.
            _constellation = new GameObject("Constellation").transform;
            _constellation.SetParent(transform, false);

            _leftLabel  = PrismLabel.Create("LabelLeft",  transform, Head, 0.020f);
            _rightLabel = PrismLabel.Create("LabelRight", transform, Head, 0.020f);

            // One sentence, once, and never again after it has been acted on.
            //
            // The brief says no instructions, and it is right about that — but two device tests in
            // a row produced "nothing happens" and "no functional purpose", and a learner who
            // cannot find the first affordance never reaches the part where discovery is the point.
            // This retires itself permanently the moment they summon anything.
            _hint = PrismLabel.Create("Hint", transform, Head, 0.024f);
        }

        void Start()
        {
            BuildConstellation();
            TryPlace();
        }

        /// <summary>
        /// Place the constellation once the head is actually tracked. Returns true when done.
        /// </summary>
        bool TryPlace()
        {
            var cam = Head != null ? Head : Worlds.Orbital.OrbitalWorld.FindHeadCamera();
            if (cam == null) return false;

            // A head that has not been tracked yet sits exactly at the origin with no rotation.
            bool looksTracked = cam.transform.position.sqrMagnitude > 1e-6f
                             || cam.transform.rotation != Quaternion.identity;
            if (!looksTracked) return false;

            PlaceAroundUser();
            _placed = true;
            Debug.Log($"[PRISM] Atrium placed around the learner at {cam.transform.position:F3}.");
            return true;
        }

        // -----------------------------------------------------------------

        void BuildConstellation()
        {
            if (Graph == null)
            {
                Debug.LogError("[PRISM] KnowledgeAtrium has no ConceptGraph; the atrium will be empty.");
                return;
            }

            Graph.Rebuild();
            foreach (var c in Graph.All)
            {
                if (c == null) continue;
                var go = new GameObject($"Concept_{c.id}");
                go.transform.SetParent(_constellation, false);
                var node = go.AddComponent<ConstellationNode>();
                node.Initialise(c, Knowledge, c.direction.normalized * c.distance);
                _nodes.Add(node);
            }

            // EVERY relationship is drawn, from the first second.
            //
            // These were previously hidden until both ends reached 0.15 mastery — which on a fresh
            // profile means none of them, so a new learner saw seventeen unconnected dots. That
            // threw away the product's whole visual thesis: knowledge is a connected thing, and the
            // constellation is the argument for it. The brief says the constellation "becomes
            // brighter and more interconnected" with progress, which is about BRIGHTNESS, not about
            // existence. So they are all here, faint, and they light up as understanding grows.
            foreach (var c in Graph.All)
            {
                if (c == null) continue;
                foreach (var l in c.links)
                {
                    var other = Graph.Find(l.targetId);
                    if (other == null) continue;
                    EnsureLink(FindNode(c.id), FindNode(other.id), l.relation, true);
                }
            }

            Debug.Log($"[PRISM] Atrium built: {_nodes.Count} concepts, {_links.Count} visible relationships.");
        }

        public void PlaceAroundUser()
        {
            var cam = Head != null ? Head : Worlds.Orbital.OrbitalWorld.FindHeadCamera();
            if (cam == null) return;

            // The constellation is centred on the learner and yawed to their facing. Pitch and
            // roll are dropped: a constellation that inherits head tilt is nauseating.
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;

            transform.position = cam.transform.position;
            _constellation.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        ConstellationNode FindNode(string id)
        {
            foreach (var n in _nodes) if (n.Concept != null && n.Concept.id == id) return n;
            return null;
        }

        // -----------------------------------------------------------------
        // links
        // -----------------------------------------------------------------

        static string LinkKey(ConstellationNode a, ConstellationNode b)
        {
            var x = a.Concept.id; var y = b.Concept.id;
            return string.CompareOrdinal(x, y) <= 0 ? x + "|" + y : y + "|" + x;
        }

        LinkView EnsureLink(ConstellationNode a, ConstellationNode b, Relation relation, bool real)
        {
            if (a == null || b == null) return null;
            var key = LinkKey(a, b);
            if (_links.TryGetValue(key, out var existing)) return existing;

            var go = new GameObject($"Link_{key}");
            go.transform.SetParent(_constellation, false);
            var mesh = new Mesh { name = "Link" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = PrismMaterials.ForRelation(relation, real ? 0.8f : 0.4f, a.Phase);
            if (!real) mat.SetFloat("_Tension", 1f);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            var view = new LinkView { View = go.transform, Mat = mat, Mesh = mesh, A = a, B = b, Relation = relation, Real = real };
            _links[key] = view;
            return view;
        }

        void UpdateLinks()
        {
            foreach (var kv in _links)
            {
                var l = kv.Value;
                if (l.A == null || l.B == null) continue;

                // A gentle catenary rather than a straight line: knowledge does not connect in
                // straight lines, and a sagging curve reads as a current rather than as a wire.
                Vector3 p0 = l.A.transform.localPosition;
                Vector3 p1 = l.B.transform.localPosition;
                Vector3 mid = (p0 + p1) * 0.5f - Vector3.up * (p1 - p0).magnitude * 0.10f;

                // Reused, not reallocated: this runs for every link every frame.
                _linkPts.Clear();
                for (int i = 0; i < 20; i++)
                {
                    float t = i / 19f;
                    _linkPts.Add(Bezier(p0, mid, p1, t));
                }
                PrismMesh.Tube(_linkPts, 0.0022f, 5, l.Mesh);

                // Relationships pulse when the learner is engaging with either end.
                bool active = l.A.Summoned || l.B.Summoned;
                l.Mat.SetFloat("_Pulse", Mathf.MoveTowards(l.Mat.GetFloat("_Pulse"), active ? 1f : 0f,
                                                           Time.deltaTime * 2f));

                // A relationship between two things you do not yet understand is a real
                // relationship you cannot yet use — present, but faint. It brightens as both ends
                // are learned, which is the constellation "becoming more interconnected".
                if (l.Real)
                {
                    float m = Mathf.Min(Knowledge?.MasteryOf(l.A.Concept.id) ?? 0f,
                                        Knowledge?.MasteryOf(l.B.Concept.id) ?? 0f);
                    float strength = Mathf.Lerp(0.22f, 1f, m) + (active ? 0.25f : 0f);
                    l.Mat.SetFloat("_Strength", Mathf.Clamp01(strength));
                }
            }
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        // -----------------------------------------------------------------
        // interaction
        // -----------------------------------------------------------------

        void Update()
        {
            // The head pose is not valid on the first frames, so placement is retried until it is.
            // Placing once in Start() puts the whole constellation at the tracking origin, which is
            // typically below and behind the learner where they will never find it.
            if (!_placed) TryPlace();

            if (Hands != null)
            {
                _hovered.Clear();
                _heldLeft  = ServiceHand(Hands.Left,  _heldLeft);
                _heldRight = ServiceHand(Hands.Right, _heldRight);
                foreach (var n in _nodes) n.SetHover(_hovered.Contains(n));
                ServiceProposal();
                ServiceEntry();
                ServiceHint();
            }

            ApplyQuestionDistortion();
            UpdateLinks();
        }

        ConstellationNode ServiceHand(PrismHands.Hand h, ConstellationNode held)
        {
            if (h == null || !h.IsTracked)
            {
                if (held != null) { held.Summoned = false; held.SetHeld(false); }
                ShowLabel(h, null, false);
                CloseDemo(h);
                return null;
            }

            if (held != null)
            {
                if (!h.IsGrasping)
                {
                    held.Summoned = false;
                    held.SetHeld(false);
                    CloseDemo(h);
                    return null;
                }
                // Summoned concepts come to the hand rather than snapping to it; the lag is what
                // makes them feel like objects with mass rather than cursors.
                var target = _constellation.InverseTransformPoint(PrismHands.PointOf(h));
                held.transform.localPosition = Vector3.Lerp(held.transform.localPosition, target,
                                                            Time.deltaTime * 8f);
                held.SetHeld(true);
                var hc = (h == Hands.Right) ? RightCursor : LeftCursor;
                if (hc != null) { hc.HasTarget = true; hc.TargetPoint = held.transform.position; }

                // Held: name AND what it is for. "What you should be able to DO" is the closest
                // thing this product has to a statement of purpose, and it belongs in the hand.
                ShowLabel(h, held, true);
                return held;
            }

            // What is this hand reaching for? Hovering happens whether or not they grasp, so the
            // learner discovers that concepts respond before they discover how to take one.
            var candidate = FindReachTarget(h);
            if (candidate != null) _hovered.Add(candidate);

            // The bead and beam show the learner where their reach is landing. Without this a
            // failed reach is indistinguishable from a dead app.
            var cursor = (h == Hands.Right) ? RightCursor : LeftCursor;
            if (cursor != null)
            {
                cursor.HasTarget = candidate != null;
                if (candidate != null) cursor.TargetPoint = candidate.transform.position;
            }

            // Reaching toward a concept IS the request for its name. Nothing is labelled otherwise.
            ShowLabel(h, candidate, false);

            if (!h.IsGrasping || candidate == null) return null;

            RetireHint();
            candidate.Summoned = true;
            candidate.SetHeld(true);
            Hands.Buzz(h, 0.25f, 0.04f);
            Companion?.Attend(candidate.transform, "learner summoned a concept", candidate.Concept.id, 0.2f);
            Companion?.Voice?.Resonance(candidate.transform.position, 0.3f);
            SpawnDemo(h, candidate);
            return candidate;
        }

        /// <summary>
        /// Bloom the phenomenon a concept stands for, above the hand holding it.
        ///
        /// This is what turns the constellation from a shelf of labels into something with content.
        /// The demonstration runs real rules and is operated by the learner's OTHER hand — see
        /// ConceptDemo for the grammar.
        /// </summary>
        void SpawnDemo(PrismHands.Hand h, ConstellationNode node)
        {
            CloseDemo(h);
            var cam = Head != null ? Head : Worlds.Orbital.OrbitalWorld.FindHeadCamera();
            var demo = ConceptDemoRegistry.Create(node.Concept, transform, node.transform,
                                                  Hands, h, cam, Companion?.Voice);
            if (h == Hands.Right) _demoRight = demo; else _demoLeft = demo;
        }

        void CloseDemo(PrismHands.Hand h)
        {
            if (h != null && h == Hands.Right)
            {
                if (_demoRight != null) { _demoRight.Close(); _demoRight = null; }
                return;
            }
            if (_demoLeft != null) { _demoLeft.Close(); _demoLeft = null; }
        }

        /// <summary>The first-run hint, and its permanent retirement.</summary>
        void ServiceHint()
        {
            if (_hint == null || _hintRetired) return;

            var cam = Head != null ? Head : Worlds.Orbital.OrbitalWorld.FindHeadCamera();
            if (cam == null) return;

            // Sits below the eyeline, out of the way of the constellation itself.
            _hint.PlaceAbove(cam.transform.position + cam.transform.forward * 1.1f
                             + Vector3.down * 0.35f, 0f);
            _hint.SetText("point at a light, then pinch", PrismPalette.Warm);
            _hint.Show(true);
        }

        void RetireHint()
        {
            if (_hintRetired) return;
            _hintRetired = true;
            _hint?.Show(false);
        }

        /// <summary>
        /// The name of what a hand is engaged with, and — once held — what it is for.
        /// </summary>
        void ShowLabel(PrismHands.Hand h, ConstellationNode node, bool held)
        {
            // A null hand is not the left hand. Without this guard, losing tracking on the right
            // hand hides the left hand's label.
            if (h == null) { _leftLabel?.Show(false); _rightLabel?.Show(false); return; }

            var label = (h == Hands.Right) ? _rightLabel : _leftLabel;
            if (label == null) return;

            if (node == null || node.Concept == null) { label.Show(false); return; }

            var c = node.Concept;
            string body = held && !string.IsNullOrEmpty(c.capability)
                        ? c.title + "\n" + c.capability
                        : c.title;

            label.SetText(body, c.Colour);
            label.PlaceAbove(node.transform.position, node.Radius + 0.045f);
            label.Show(true);
        }

        /// <summary>
        /// What the learner is reaching for.
        ///
        /// The first build of this only ever matched concepts within 13 cm of the hand — but the
        /// constellation is authored from 0.85 m to 2.85 m away, so NOTHING could ever be selected
        /// and the atrium was completely inert. "A concept can be summoned by reaching toward it"
        /// means pointing at it, not walking up to it and touching it.
        ///
        /// Two ways in, checked in this order:
        ///   1. direct touch, for anything already brought within TouchRadius
        ///   2. otherwise the nearest concept inside a narrow cone about where the hand points
        ///
        /// Angular distance rather than screen distance, so it works identically for a controller,
        /// a tracked hand, and either eye.
        /// </summary>
        ConstellationNode FindReachTarget(PrismHands.Hand h)
        {
            Vector3 point = PrismHands.PointOf(h);

            // 1. touch wins outright — if it is in your hand you meant that one.
            ConstellationNode touched = null;
            float touchBest = TouchRadius * TouchRadius;
            foreach (var n in _nodes)
            {
                if (n.Summoned) continue;
                float d = (n.transform.position - point).sqrMagnitude;
                if (d < touchBest) { touchBest = d; touched = n; }
            }
            if (touched != null) return touched;

            // 2. the cone.
            if (!h.ReachValid) return null;
            float cos = Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(ReachConeDegrees, 1f, 45f));
            ConstellationNode best = null;
            float bestScore = -1f;

            foreach (var n in _nodes)
            {
                if (n.Summoned) continue;
                Vector3 to = n.transform.position - h.Reach.origin;
                float dist = to.magnitude;
                if (dist > ReachRange || dist < 1e-4f) continue;

                float align = Vector3.Dot(h.Reach.direction, to / dist);
                if (align < cos) continue;

                // Prefer well-aligned AND near. Alignment dominates; distance breaks ties.
                float score = align - dist * 0.02f;
                if (score > bestScore) { bestScore = score; best = n; }
            }
            return best;
        }

        /// <summary>
        /// Two concepts brought together is a claim. The atrium checks it against the graph and
        /// answers physically: consonance and a real current if the claim holds, beating dissonance
        /// and a shivering tube if it does not.
        /// </summary>
        void ServiceProposal()
        {
            if (_heldLeft == null || _heldRight == null || _heldLeft == _heldRight)
            {
                _proposeHeldFor = 0f;
                _lastProposal = null;
                return;
            }

            float d = (_heldLeft.transform.position - _heldRight.transform.position).magnitude;
            if (d > ProposeDistance) { _proposeHeldFor = 0f; return; }

            _proposeHeldFor += Time.deltaTime;
            var key = LinkKey(_heldLeft, _heldRight);
            if (key == _lastProposal || _proposeHeldFor < 0.35f) return;
            _lastProposal = key;

            bool real = Graph != null && Graph.TryGetRelation(_heldLeft.Concept.id, _heldRight.Concept.id, out var link);
            Graph.TryGetRelation(_heldLeft.Concept.id, _heldRight.Concept.id, out var found);

            var view = EnsureLink(_heldLeft, _heldRight, real ? found.relation : Relation.Analogy, real);
            var where = (_heldLeft.transform.position + _heldRight.transform.position) * 0.5f;

            if (real)
            {
                view.Real = true;
                view.Mat.SetFloat("_Tension", 0f);
                view.Mat.SetFloat("_Strength", 0.9f);
                // Discovering a relationship is itself learning, on both ends.
                Knowledge?.Learn(_heldLeft.Concept.id, 0.08f);
                Knowledge?.Learn(_heldRight.Concept.id, 0.08f);
                Knowledge?.Confirm(_heldLeft.Concept.id, 0.1f);
                _heldLeft.Refresh(Knowledge);
                _heldRight.Refresh(Knowledge);
                Companion?.Voice?.Consonance(where);
                Debug.Log($"[PRISM] Atrium: {_heldLeft.Concept.id} <-> {_heldRight.Concept.id} " +
                          $"holds ({found.relation}).");
            }
            else
            {
                view.Mat.SetFloat("_Tension", 1f);
                Companion?.Voice?.Tension(where);
                Debug.Log($"[PRISM] Atrium: {_heldLeft.Concept.id} <-> {_heldRight.Concept.id} " +
                          "does not hold; tension.");
            }
        }

        /// <summary>
        /// Holding a concept to your chest enters its world. The gesture is deliberately bodily and
        /// deliberately not a button: it is the same motion as taking something in.
        /// </summary>
        void ServiceEntry()
        {
            var cam = Head != null ? Head : Worlds.Orbital.OrbitalWorld.FindHeadCamera();
            if (cam == null) return;

            foreach (var held in new[] { _heldLeft, _heldRight })
            {
                if (held == null || held.Concept == null) continue;
                if (string.IsNullOrEmpty(held.Concept.worldId)) continue;
                float d = (held.transform.position - cam.transform.position).magnitude;
                if (d > EnterDistance) continue;

                Debug.Log($"[PRISM] Atrium: entering world '{held.Concept.worldId}' " +
                          $"via concept '{held.Concept.id}'.");
                held.Summoned = false;
                _heldLeft = _heldRight = null;
                WorldEntered?.Invoke(held.Concept.worldId);
                return;
            }
        }

        /// <summary>Open questions bend the space near them; nothing rests comfortably beside one.</summary>
        void ApplyQuestionDistortion()
        {
            foreach (var n in _nodes)
            {
                if (n.Summoned) continue;
                Vector3 offset = Vector3.zero;

                foreach (var q in _nodes)
                {
                    if (q == n || q.Concept == null) continue;
                    float radius = KnowledgeAppearance.DistortionRadius(q.Concept.kind);
                    if (radius <= 0f) continue;

                    Vector3 d = n.HomePosition - q.transform.localPosition;
                    float dist = d.magnitude;
                    if (dist > radius || dist < 1e-4f) continue;

                    // Pushed outward, strongest right beside the void.
                    float f = 1f - dist / radius;
                    offset += d.normalized * f * f * radius * 0.28f;
                }
                n.SetDistortion(offset);
            }
        }

        /// <summary>Re-read every node from the learner's state. Called after a world is left.</summary>
        public void RefreshAll()
        {
            foreach (var n in _nodes) n.Refresh(Knowledge);

            // Links already all exist; their brightness is recomputed in UpdateLinks from mastery.
        }

        public IReadOnlyList<ConstellationNode> Nodes => _nodes;
    }
}
