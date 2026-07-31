using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds
{
    /// <summary>
    /// A concept the world wants added to the constellation. Plain data, so a world module can
    /// declare its own concepts without touching the master-owned seeder.
    /// </summary>
    public struct ConceptSpec
    {
        public string Id;
        public string Title;
        public ConceptDomain Domain;
        public KnowledgeKind Kind;
        public Vector3 Direction;
        public float Distance;
        public string Capability;
        public string Formalisation;
        /// <summary>Set only on the concept that OPENS this world. Leave empty on the rest.</summary>
        public string WorldId;
        /// <summary>Relationships to other concepts, by id. Dangling targets are dropped with a warning.</summary>
        public (string target, Relation relation, float strength)[] Links;
    }

    /// <summary>
    /// Base class for every Concept World.
    ///
    /// Exists so that ten worlds built independently share one skeleton rather than inventing ten.
    /// A world module supplies geometry, loop gates and a tick; everything about placement, lazy
    /// building, the learning loop and the return path is handled here.
    ///
    /// TWO RULES THAT MATTER MORE THAN THE API:
    ///
    /// 1. BUILD LAZILY. <see cref="BuildWorld"/> is called the first time the learner enters, never
    ///    in Awake. With ten worlds in one scene, building all of them at startup would cost memory
    ///    and load time for nine places nobody has gone.
    ///
    /// 2. RUN REAL RULES. Every simulation in PRISM computes its subject rather than animating a
    ///    performance of it. A demonstration that mimes its concept is worse than none, because the
    ///    learner builds intuition from a lie. If the honest simulation is too expensive, simplify
    ///    the MODEL and say so in a comment — do not fake the result.
    /// </summary>
    public abstract class PrismWorldBase : MonoBehaviour
    {
        [Header("Wiring (assigned by PrismSceneBuilder)")]
        public PrismHands Hands;
        public Camera Head;

        public KnowledgeState Knowledge { get; set; }
        public Companion.PrismCompanion Companion { get; set; }

        /// <summary>Stable id. The concept that opens this world carries the same string in its worldId.</summary>
        public abstract string WorldId { get; }

        /// <summary>The concept whose mastery this world advances.</summary>
        public abstract string PrimaryConceptId { get; }

        /// <summary>Human-readable, for logs and the loading path.</summary>
        public virtual string DisplayName => WorldId;

        /// <summary>Shaders this world creates by name at runtime. Master unions these into Always Included.</summary>
        public virtual string[] Shaders => System.Array.Empty<string>();

        /// <summary>Concepts this world contributes to the constellation.</summary>
        public virtual IEnumerable<ConceptSpec> Concepts => System.Array.Empty<ConceptSpec>();

        public LearningLoop Loop { get; private set; }

        /// <summary>Everything this world builds goes under here. Placed in front of the learner on entry.</summary>
        protected Transform Anchor { get; private set; }

        [Header("Placement")]
        [Tooltip("Metres below the eye. Measured DOWN FROM THE HEAD — never up from an assumed floor.")]
        public float EyeToTable = 0.50f;
        [Tooltip("Metres in front of the head.")]
        public float Reach = 0.60f;

        bool _built;
        bool _entered;

        // -----------------------------------------------------------------

        protected virtual void Awake()
        {
            Anchor = new GameObject($"{WorldId}Anchor").transform;
            Anchor.SetParent(transform, false);
            gameObject.SetActive(false);      // worlds are dormant until entered
        }

        /// <summary>Called by PrismSession when the learner steps into this world.</summary>
        public void Enter()
        {
            gameObject.SetActive(true);

            if (!_built)
            {
                Loop = new LearningLoop(PrimaryConceptId, Knowledge);
                Loop.StageEntered += OnStageEntered;
                ConfigureLoop(Loop);

                BuildStage();
                BuildWorld();
                _built = true;
                Debug.Log($"[PRISM] World '{WorldId}' built on first entry.");
            }

            PlaceInFrontOfUser();
            ShellTakeOver();
            _entered = true;
            OnEnter();
        }

        public void Leave()
        {
            if (!_entered) return;
            _entered = false;
            ShellHandBack();
            OnLeave();
            Knowledge?.Save();
            gameObject.SetActive(false);
        }

        void Update()
        {
            if (!_entered || !_built) return;
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            Loop?.Tick(dt);
            TickStage(dt);
            Tick(dt);
        }

        // -----------------------------------------------------------------
        // to implement
        // -----------------------------------------------------------------

        /// <summary>
        /// The stage every world stands on, built before its own content.
        ///
        /// Added after the first renders showed the real problem with all eleven worlds at once:
        /// a handful of small objects hanging in the air over a featureless plain, with nothing
        /// establishing where the world IS. A phenomenon floating in nothing reads as a debug
        /// scene; the same phenomenon inside its own sky, with motes drifting through it, reads as
        /// a place you have been taken to.
        ///
        /// An early version also put a lit dais under every world. It was removed: the worlds build
        /// their content ABOVE the anchor and the dais sat below it, so the two never appeared in
        /// one frame, and a ceramic plinth inside a cell or in deep space was a contradiction the
        /// shell had already made unnecessary.
        ///
        /// It is deliberately in the BASE class: one implementation improves every world, including
        /// the ten built by separate authors who never saw each other's work.
        /// </summary>
        protected virtual void BuildStage()
        {
            BuildShell();

            // A shallow volume of slow motes. Depth cue and atmosphere in one, at the cost of a
            // single dynamic mesh.
            _motes = new GameObject("StageMotes");
            _motes.transform.SetParent(Anchor, false);
            _moteMesh = new Mesh { name = "stageMotes" };
            _moteMesh.MarkDynamic();
            _motes.AddComponent<MeshFilter>().sharedMesh = _moteMesh;
            var moteMat = PrismMaterials.New(PrismMaterials.Mote);
            moteMat.SetColor("_Tint", PrismPalette.Lavender);
            moteMat.SetColor("_EdgeTint", PrismPalette.Cyan);
            moteMat.SetFloat("_Density", 0.55f);
            _motes.AddComponent<MeshRenderer>().sharedMaterial = moteMat;

            _motePos = new Vector3[MoteCount];
            _motePhase = new float[MoteCount];
            for (int i = 0; i < MoteCount; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Random.value) * StageRadius * 1.15f;
                _motePos[i] = new Vector3(Mathf.Cos(a) * r,
                                          Random.Range(-StageDrop * 0.6f, StageRadius * 0.9f),
                                          Mathf.Sin(a) * r);
                _motePhase[i] = Random.value;
            }
        }

        /// <summary>
        /// Where this world takes you. Override to pick a preset and adjust it; see
        /// <see cref="WorldShellSpec"/> for why every world needs its own.
        /// </summary>
        protected virtual WorldShellSpec Shell => WorldShellSpec.Studio;

        void BuildShell() => WorldShell.Build(Anchor, Shell, StageDrop);
        void ShellTakeOver() => WorldShell.TakeOver(Shell);
        void ShellHandBack() => WorldShell.HandBack();

        [Header("Stage")]
        [Tooltip("Radius of the dais the world stands on, metres.")]
        public float StageRadius = 0.42f;
        [Tooltip("How far the dais sits below the world's anchor, metres.")]
        public float StageDrop = 0.10f;

        const int MoteCount = 40;
        GameObject _motes;
        Mesh _moteMesh;
        Vector3[] _motePos;
        float[] _motePhase;
        readonly List<Vector3> _mv = new List<Vector3>();
        readonly List<Vector2> _mu = new List<Vector2>();
        readonly List<int> _mt = new List<int>();

        /// <summary>Drift the stage motes. Camera-facing quads in one mesh: one draw call.</summary>
        void TickStage(float dt)
        {
            if (_moteMesh == null) return;
            var cam = Head != null ? Head : Orbital.OrbitalWorld.FindHeadCamera();
            if (cam == null) return;

            Vector3 right = Anchor.InverseTransformVector(cam.transform.right);
            Vector3 up = Anchor.InverseTransformVector(cam.transform.up);

            _mv.Clear(); _mu.Clear(); _mt.Clear();
            float t = Time.time;
            for (int i = 0; i < _motePos.Length; i++)
            {
                var p = _motePos[i];
                // A slow convection: motes rise, drift, and wrap. Nothing in PRISM is ever static.
                p.y += (0.012f + _motePhase[i] * 0.01f) * dt;
                p.x += Mathf.Sin(t * 0.22f + _motePhase[i] * 9f) * 0.004f * dt * 60f * 0.016f;
                if (p.y > StageRadius * 0.95f) p.y = -StageDrop * 0.6f;
                _motePos[i] = p;

                float s = 0.004f + _motePhase[i] * 0.005f;
                var r = right * s; var u = up * s;
                int b = _mv.Count;
                _mv.Add(p - r - u); _mu.Add(new Vector2(0, 0));
                _mv.Add(p + r - u); _mu.Add(new Vector2(1, 0));
                _mv.Add(p - r + u); _mu.Add(new Vector2(0, 1));
                _mv.Add(p + r + u); _mu.Add(new Vector2(1, 1));
                _mt.Add(b); _mt.Add(b + 2); _mt.Add(b + 1);
                _mt.Add(b + 1); _mt.Add(b + 2); _mt.Add(b + 3);
            }
            _moteMesh.Clear();
            _moteMesh.SetVertices(_mv);
            _moteMesh.SetUVs(0, _mu);
            _moteMesh.SetTriangles(_mt, 0);
            _moteMesh.RecalculateBounds();
        }

        /// <summary>Build all geometry under <see cref="Anchor"/>. Called once, on first entry.</summary>
        protected abstract void BuildWorld();

        /// <summary>
        /// Add the gates that carry the learner through Wonder -> Explore -> ... -> Connect.
        /// Gates are predicates over evidence the learner has PRODUCED — never over an answer they
        /// have given. There is no button meaning "I understand".
        /// </summary>
        protected abstract void ConfigureLoop(LearningLoop loop);

        /// <summary>Run the simulation and read the hands.</summary>
        protected abstract void Tick(float dt);

        protected virtual void OnEnter() { }
        protected virtual void OnLeave() { }
        protected virtual void OnStageEntered(LoopStage stage)
        {
            Companion?.OnStage(stage, Loop);
        }

        // -----------------------------------------------------------------
        // helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Put the world where the learner's body is.
        ///
        /// Yaw only — inheriting head pitch and roll tilts the whole simulation with the neck and
        /// reads as seasickness. Height is measured DOWN FROM THE EYE because there may be no floor:
        /// under XR Simulation the eye can start at 2.3 m, and content placed at an assumed table
        /// height then sits a metre and a half below the eyeline where nobody finds it.
        /// </summary>
        public void PlaceInFrontOfUser()
        {
            var cam = Head != null ? Head : Orbital.OrbitalWorld.FindHeadCamera();
            if (cam == null) return;

            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();

            Anchor.position = cam.transform.position + fwd * Reach + Vector3.down * EyeToTable;
            Anchor.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        protected Transform Body(Mesh mesh, float radius, Material mat, string name = "part")
        {
            var go = new GameObject(name);
            go.transform.SetParent(Anchor, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * radius;
            return go.transform;
        }

        /// <summary>Nearest hand to a world point, or null if neither is tracked.</summary>
        protected PrismHands.Hand NearestHand(Vector3 worldPoint, float maxDistance = 0.25f)
        {
            if (Hands == null) return null;
            PrismHands.Hand best = null;
            float bestD = maxDistance * maxDistance;
            foreach (var h in new[] { Hands.Left, Hands.Right })
            {
                if (h == null || !h.IsTracked) continue;
                float d = (PrismHands.PointOf(h) - worldPoint).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; }
            }
            return best;
        }

        /// <summary>
        /// What the learner is pointing at, among a set of candidates. Angular rather than
        /// screen-space, so it behaves identically for a controller and a tracked hand.
        /// </summary>
        protected T PointedAt<T>(IEnumerable<T> candidates, System.Func<T, Vector3> positionOf,
                                 float coneDegrees = 12f, float range = 4f) where T : class
        {
            if (Hands == null) return null;
            T best = null;
            float bestScore = -1f;
            float cos = Mathf.Cos(Mathf.Deg2Rad * coneDegrees);

            foreach (var h in new[] { Hands.Left, Hands.Right })
            {
                if (h == null || !h.IsTracked || !h.ReachValid) continue;
                foreach (var c in candidates)
                {
                    if (c == null) continue;
                    Vector3 to = positionOf(c) - h.Reach.origin;
                    float dist = to.magnitude;
                    if (dist < 1e-4f || dist > range) continue;
                    float align = Vector3.Dot(h.Reach.direction, to / dist);
                    if (align < cos) continue;
                    float score = align - dist * 0.02f;
                    if (score > bestScore) { bestScore = score; best = c; }
                }
            }
            return best;
        }
    }
}
