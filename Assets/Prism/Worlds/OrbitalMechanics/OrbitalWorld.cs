using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.Orbital
{
    /// <summary>Evidence keys. Named constants because the gates and the world must agree exactly.</summary>
    public static class OrbitalEvidence
    {
        public const string Release        = "release";
        public const string Bound          = "bound";
        public const string Unbound        = "unbound";
        public const string Absorbed       = "absorbed";
        public const string ConicCircle    = "conic.circle";
        public const string ConicEllipse   = "conic.ellipse";
        public const string ConicParabola  = "conic.parabola";
        public const string ConicHyperbola = "conic.hyperbola";
        public const string HandleUsed     = "handle.used";
        public const string NearCircular   = "near-circular";
        public const string ChallengeDone  = "challenge.complete";
        public const string PredictionGood = "prediction.correct";
        public const string Construction   = "construction.stable";
        public const string BestEccentricity = "best.eccentricity";
    }

    /// <summary>
    /// The Orbital Mechanics world.
    ///
    /// The choreography is the lesson, so it is worth stating plainly what the learner is shown
    /// and — more importantly — what they are NOT shown yet:
    ///
    ///   Wonder     A planet. One moon, hanging still. No field, no labels, no arrows, no trail.
    ///              Let go of the moon and it falls. That is all that happens.
    ///   Explore    More moons, and trails that colour themselves by speed. Still no words. The
    ///              learner throws things for as long as they like. Some come back; some leave.
    ///   Discover   Once they have made BOTH a bound and an unbound path — which is to say, once
    ///              they have found the boundary themselves — the invisible becomes visible: the
    ///              gravitational potential appears as contours, and each moon grows a velocity
    ///              arrow they can take hold of by its head.
    ///   Formalize  Now the shapes get named, spatially: the complete conic is drawn before the
    ///              moon has finished tracing it, the apsides are marked, and vis-viva appears as
    ///              three lengths that must balance.
    ///   Apply      Put a moon into a circular orbit on a given ring, and keep it there.
    ///   Explain    Predict where a moon will be, by placing a marker. Being wrong is informative
    ///              and is recorded as a misconception, not as a failure.
    ///   Create     Build a two-moon system of your own that survives.
    ///   Connect    Return to the atrium; the constellation has changed.
    /// </summary>
    public class OrbitalWorld : MonoBehaviour
    {
        [Header("Wiring (assigned by PrismSceneBuilder)")]
        public PrismHands Hands;
        public Camera Head;

        [Header("Concept")]
        public string ConceptId = "orbital-mechanics";

        [Header("Layout")]
        [Tooltip("Metres below the head. Measured down from the eye, never up from an assumed floor.")]
        public float EyeToTable = 0.50f;
        [Tooltip("Metres in front of the head.")]
        public float Reach = 0.55f;
        public float PlanetRadius = PrismScale.DisplayPlanetRadius;
        public float MoonRadius = 0.006f;
        public int MoonCount = 5;
        public float GrabRadius = 0.07f;

        // ---- runtime ----
        public OrbitalSim Sim { get; private set; }
        public LearningLoop Loop { get; private set; }
        public KnowledgeState Knowledge { get; set; }

        Transform _anchor;
        Transform _planet;
        OrbitalSim.Attractor _primary;

        readonly List<Moon> _moons = new List<Moon>();
        Moon _held;
        PrismHands.Hand _holdingHand;

        Material _fieldMat;
        Transform _fieldDisc;
        float _fieldReveal;

        Mesh _predictMesh;
        Transform _predictView;
        Material _predictMat;

        Mesh _conicMesh;
        Transform _conicView;
        Material _conicMat;

        OrbitalChallenge _challenge;
        Companion.PrismCompanion _companion;
        PrismLabel _conicLabel;

        /// <summary>A moon: one simulation particle, one body, one trail.</summary>
        public class Moon
        {
            public OrbitalSim.Particle P;
            public Transform View;
            public PrismTrailRibbon Trail;
            public Material Mat;
            public Transform Arrow;
            public Material ArrowMat;
            public bool Held;
            public bool Idle = true;          // parked in the cradle, never released yet
            public Vector3 CradleOffset;
            public float BoundFor;            // seconds continuously bound, for the Create gate
            public OrbitElements Elements;
        }

        void Awake()
        {
            Sim = new OrbitalSim();
            BuildWorld();
            BuildLoop();
        }

        void Start() => TryPlace();

        bool _placed;

        /// <summary>
        /// Place the world once the head is genuinely tracked. Doing it unconditionally in Start()
        /// puts the table at the tracking origin, which on a Quest is under the floor behind the
        /// learner — the world is built correctly and simply cannot be found.
        /// </summary>
        bool TryPlace()
        {
            var cam = Head != null ? Head : FindHeadCamera();
            if (cam == null) return false;
            bool tracked = cam.transform.position.sqrMagnitude > 1e-6f
                        || cam.transform.rotation != Quaternion.identity;
            if (!tracked) return false;

            PlaceInFrontOfUser();
            _placed = true;
            return true;
        }

        // -----------------------------------------------------------------
        // construction
        // -----------------------------------------------------------------

        /// <summary>
        /// Two-body motion belongs in the dark, where a conic is the only visible shape.
        ///
        /// This world predates <see cref="PrismWorldBase"/> and is still wired straight into the
        /// session, so it opts into the shell explicitly rather than inheriting it. Same helper,
        /// same result — see <see cref="WorldShellSpec"/>.
        /// </summary>
        public WorldShellSpec Shell => WorldShellSpec.DeepSpace;

        /// <summary>Hide the Atrium's highland and put deep space around the planet.</summary>
        public void EnterShell() => WorldShell.TakeOver(Shell);

        /// <summary>Give the highland back on the way out.</summary>
        public void LeaveShell() => WorldShell.HandBack();

        void BuildWorld()
        {
            _anchor = new GameObject("OrbitalAnchor").transform;
            _anchor.SetParent(transform, false);

            WorldShell.Build(_anchor, Shell);

            // A debris ring between the atmosphere and the moon cradle. It is here to be looked at,
            // but it earns its place by making Kepler's third law visible: see OrbitalRing.
            _ring = new OrbitalRing(_anchor,
                                    inner: PlanetRadius * 1.9f,
                                    outer: PlanetRadius * 3.3f,
                                    mu: PrismScale.Mu);

            // The planet. Ceramic body with a gel atmosphere over it, so it has an inside.
            _planet = MakeBody("Planet", PrismMesh.Icosphere(3), PlanetRadius,
                               PrismMaterials.CeramicBody(PrismPalette.Warm, 0.30f, 0f));
            _planet.SetParent(_anchor, false);

            var atmos = MakeBody("Atmosphere", PrismMesh.Icosphere(3), PlanetRadius * 1.28f,
                                 MakeAtmosphere());
            atmos.SetParent(_planet, false);
            atmos.localScale = Vector3.one * 1.28f;

            _primary = new OrbitalSim.Attractor
            {
                Position = _anchor.position,
                Mu = PrismScale.Mu,
                Radius = PlanetRadius
            };
            Sim.Attractors.Add(_primary);

            BuildFieldDisc();
            BuildMoons();
            BuildPredictionAndConic();

            // The one piece of text in this world, and it does not appear until Formalize. Until
            // then the learner has thrown a dozen moons and been told nothing at all.
            _conicLabel = PrismLabel.Create("ConicLabel", _anchor, Head, 0.016f);

            _challenge = gameObject.AddComponent<OrbitalChallenge>();
            _challenge.World = this;
        }

        Material MakeAtmosphere()
        {
            var m = PrismMaterials.New(PrismMaterials.Gel);
            m.SetColor("_Tint", PrismPalette.Cyan);
            m.SetColor("_DeepTint", PrismPalette.Violet);
            m.SetFloat("_Density", 0.9f);
            m.SetFloat("_NoiseFreq", 4.2f);
            m.SetFloat("_FlowSpeed", 0.18f);
            m.SetVector("_FlowAxis", new Vector4(0, 1, 0, 0));
            return m;
        }

        OrbitalRing _ring;

        void BuildFieldDisc()
        {
            var go = new GameObject("GravitationalField");
            _fieldDisc = go.transform;
            _fieldDisc.SetParent(_anchor, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = PrismMesh.Disc(128, 40);
            var mr = go.AddComponent<MeshRenderer>();
            _fieldMat = PrismMaterials.New(PrismMaterials.Field);
            _fieldMat.SetFloat("_Reveal", 0f);
            // Contour interval chosen against the potential range a learner actually explores:
            // phi runs about -0.09 to -0.02 J/kg between the planet's surface and arm's length,
            // so ~0.0018 per contour puts roughly forty lines across the useful range.
            _fieldMat.SetFloat("_ContourStep", 0.0018f);
            _fieldMat.SetFloat("_FadeOuter", 0.85f);
            _fieldMat.SetFloat("_FadeInner", PlanetRadius * 0.9f);
            mr.sharedMaterial = _fieldMat;
            _fieldDisc.localScale = new Vector3(0.95f, 1f, 0.95f);
        }

        void BuildMoons()
        {
            for (int i = 0; i < MoonCount; i++)
            {
                var m = new Moon();
                float a = (float)i / MoonCount * Mathf.PI * 2f;

                // The cradle: moons wait in a gentle ring just inside comfortable reach, so there
                // is always one to pick up without hunting for it.
                m.CradleOffset = new Vector3(Mathf.Cos(a) * 0.16f, 0.055f, Mathf.Sin(a) * 0.16f);

                var mat = PrismMaterials.New(PrismMaterials.Seed);
                mat.SetColor("_Tint", PrismPalette.Spectral(0.10f + 0.5f * i / Mathf.Max(1, MoonCount - 1)));
                mat.SetFloat("_Growth", 1f);           // a moon is a thing, not a concept
                mat.SetFloat("_Density", 1.1f);
                mat.SetFloat("_FilmNm", 320f + i * 47f);
                m.Mat = mat;

                m.View = MakeBody($"Moon{i}", PrismMesh.Icosphere(2), MoonRadius, mat);
                m.View.SetParent(_anchor, false);
                m.View.localPosition = m.CradleOffset;

                // Trail
                var tgo = new GameObject($"Moon{i}Trail");
                tgo.transform.SetParent(_anchor, false);
                var tmr = tgo.AddComponent<MeshRenderer>();
                tgo.AddComponent<MeshFilter>();
                m.Trail = tgo.AddComponent<PrismTrailRibbon>();
                tmr.sharedMaterial = PrismMaterials.TrailMaterial(
                    PrismScale.CircularSpeed(0.45f), PrismScale.EscapeSpeed(PlanetRadius * 1.6f));

                // Velocity arrow, hidden until Discover.
                var ago = new GameObject($"Moon{i}Vector");
                ago.transform.SetParent(_anchor, false);
                var amf = ago.AddComponent<MeshFilter>();
                amf.sharedMesh = PrismMesh.Arrow();
                var amr = ago.AddComponent<MeshRenderer>();
                m.ArrowMat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.5f);
                amr.sharedMaterial = m.ArrowMat;
                m.Arrow = ago.transform;
                m.Arrow.gameObject.SetActive(false);

                _moons.Add(m);
            }
        }

        void BuildPredictionAndConic()
        {
            _predictMesh = new Mesh { name = "PredictedPath" };
            _predictMesh.MarkDynamic();
            var pgo = new GameObject("PredictedPath");
            pgo.transform.SetParent(_anchor, false);
            pgo.AddComponent<MeshFilter>().sharedMesh = _predictMesh;
            _predictMat = PrismMaterials.ForRelation(Relation.Causes, 0.55f);
            _predictMat.SetFloat("_CoreGain", 0.35f);
            pgo.AddComponent<MeshRenderer>().sharedMaterial = _predictMat;
            _predictView = pgo.transform;
            _predictView.gameObject.SetActive(false);

            _conicMesh = new Mesh { name = "AnalyticConic" };
            _conicMesh.MarkDynamic();
            var cgo = new GameObject("AnalyticConic");
            cgo.transform.SetParent(_anchor, false);
            cgo.AddComponent<MeshFilter>().sharedMesh = _conicMesh;
            _conicMat = PrismMaterials.ForRelation(Relation.Constrains, 0.8f);
            _conicMat.SetFloat("_CoreGain", 0.5f);
            _conicMat.SetFloat("_Packets", 2f);
            cgo.AddComponent<MeshRenderer>().sharedMaterial = _conicMat;
            _conicView = cgo.transform;
            _conicView.gameObject.SetActive(false);
        }

        static Transform MakeBody(string name, Mesh mesh, float radius, Material mat)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * radius;
            return go.transform;
        }

        /// <summary>
        /// Put the world where the learner's body is, not where the scene origin happens to be.
        ///
        /// Head pitch and roll are flattened: inheriting them tilts the whole simulation with the
        /// learner's neck, which reads as seasickness. And the height is measured DOWN from the
        /// eye rather than up from a floor, because there may not be a floor — under XR Simulation
        /// the eye can start at 2.3 m, and a table placed at an assumed 0.74 m would then sit a
        /// metre and a half below the eyeline where nobody will ever find it.
        /// </summary>
        public void PlaceInFrontOfUser()
        {
            var cam = Head != null ? Head : FindHeadCamera();
            if (cam == null)
            {
                Debug.LogWarning("[PRISM] OrbitalWorld: no head camera; leaving the world at the origin.");
                return;
            }

            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();

            _anchor.position = cam.transform.position + fwd * Reach + Vector3.down * EyeToTable;
            _anchor.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            _primary.Position = _anchor.position;
            foreach (var m in _moons) { m.Trail.SetCamera(cam); m.Trail.Clear(); }
            PushFieldBodies();
        }

        /// <summary>
        /// The camera that is actually drawing. Camera.main is unreliable here — under XR
        /// Simulation it is frequently not the camera rendering the view, which produces the
        /// maddening symptom of every viewport calculation claiming the geometry is centred while
        /// nothing is visible.
        /// </summary>
        public static Camera FindHeadCamera()
        {
            Camera best = null;
            foreach (var c in Camera.allCameras)
            {
                if (!c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (best == null || c.depth >= best.depth) best = c;
            }
            return best != null ? best : Camera.main;
        }

        // -----------------------------------------------------------------
        // the loop
        // -----------------------------------------------------------------

        void BuildLoop()
        {
            Loop = new LearningLoop(ConceptId, Knowledge);

            Loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet let go of anything",
                e => e.Count(OrbitalEvidence.Release) >= 1));

            // The gate that matters most. We are waiting for the learner to have produced BOTH a
            // path that came back and a path that did not — because the whole of orbital mechanics
            // hangs off the sign of one quantity, and a learner who has personally straddled that
            // boundary is ready to be shown it. Asking for four throws first means they arrive
            // there by playing rather than by being instructed.
            Loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet made both a bound and an unbound path",
                e => e.Count(OrbitalEvidence.Release) >= 4
                  && e.Has(OrbitalEvidence.Bound)
                  && e.Has(OrbitalEvidence.Unbound)));

            Loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet used a velocity arrow to make a near-circular orbit",
                e => e.Has(OrbitalEvidence.HandleUsed) && e.Has(OrbitalEvidence.NearCircular)));

            Loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet produced all three kinds of path",
                e => (e.Has(OrbitalEvidence.ConicCircle) || e.Has(OrbitalEvidence.ConicEllipse))
                  && e.Has(OrbitalEvidence.ConicHyperbola)
                  && e.Count(OrbitalEvidence.Release) >= 8));

            Loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet parked a moon on the target ring",
                e => e.Has(OrbitalEvidence.ChallengeDone)));

            Loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet predicted where a moon would be",
                e => e.Has(OrbitalEvidence.PredictionGood)));

            Loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet built a system of their own that survives",
                e => e.Has(OrbitalEvidence.Construction)));

            Loop.StageEntered += OnStageEntered;
        }

        /// <summary>
        /// The last stage whose reveals have been applied. Compared against Loop.Stage every frame.
        /// </summary>
        LoopStage _appliedStage = (LoopStage)(-1);

        /// <summary>
        /// Apply everything a stage turns on, derived from the CURRENT stage rather than from a
        /// transition event.
        ///
        /// The distinction matters for a returning learner. LoopStage is persisted in the profile,
        /// so someone who leaves at Apply comes back with Loop.Stage already == Apply — and no
        /// StageEntered event ever fires, because nothing transitioned. The ring challenge would
        /// then never start and the world would be silently dead for exactly the people who had got
        /// furthest into it. Driving from state instead of from the edge fixes that, and is
        /// idempotent because it only runs when the stage actually differs.
        /// (Found by the Mathematics of Motion module, which hit the same trap and guarded for it.)
        /// </summary>
        void SyncStage()
        {
            if (Loop == null || Loop.Stage == _appliedStage) return;
            var stage = Loop.Stage;
            _appliedStage = stage;

            // Everything at or below the current stage must be on, not just the newest one — a
            // learner restored straight to Apply still needs Discover's arrows and Formalize's conic.
            if (stage >= LoopStage.Discover)
                foreach (var m in _moons) if (!m.Idle) m.Arrow.gameObject.SetActive(true);

            if (stage >= LoopStage.Formalize)
                _conicView.gameObject.SetActive(true);

            if (stage == LoopStage.Apply) _challenge.BeginRingChallenge(0.26f);
            if (stage == LoopStage.Explain) _challenge.BeginPrediction();

            if (stage == LoopStage.Connect)
            {
                Knowledge?.Confirm(ConceptId, 0.4f);
                Debug.Log("[PRISM] Orbital mechanics: Connect reached. The constellation changes.");
            }

            _companion?.OnStage(stage, Loop);
        }

        /// <summary>
        /// The loop's transition event. Reveals are NOT done here — see SyncStage(), which derives
        /// them from the current stage so a restored session behaves like a live one.
        /// </summary>
        void OnStageEntered(LoopStage stage) { }

        public void SetCompanion(Companion.PrismCompanion c) => _companion = c;

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;
            if (!_placed) TryPlace();
            Loop.Tick(dt);
            SyncStage();          // drives reveals from Loop.Stage, including a restored session

            HandleGrab();
            Sim.Advance(dt);
            _ring?.Tick(dt, Head != null ? Head : FindHeadCamera(), Sim.TimeScale);
            SyncViews(dt);
            UpdateFieldReveal(dt);
            UpdatePrediction();
            UpdateConic();

            _challenge?.Evaluate(dt);
        }

        void HandleGrab()
        {
            if (Hands == null) return;

            if (_held == null)
            {
                TryGrabWith(Hands.Left);
                if (_held == null) TryGrabWith(Hands.Right);
                return;
            }

            // Carry
            var carry = PrismHands.PointOf(_holdingHand);
            _held.View.position = carry;
            _held.P.Position = carry;
            _held.P.Velocity = Vector3.zero;

            if (!_holdingHand.IsGrasping || !_holdingHand.IsTracked)
                Release();
        }

        void TryGrabWith(PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked || !h.IsGrasping) return;

            Vector3 point = PrismHands.PointOf(h);

            Moon best = null;
            float bestD = GrabRadius * GrabRadius;
            foreach (var m in _moons)
            {
                float d = (m.View.position - point).sqrMagnitude;
                if (d < bestD) { bestD = d; best = m; }
            }

            // Nothing in the hand: allow reaching for one instead. The table is within arm's reach
            // by design, but a learner seated slightly too far away should not find the world inert.
            if (best == null && h.ReachValid)
            {
                float cos = Mathf.Cos(Mathf.Deg2Rad * 12f);
                float bestScore = -1f;
                foreach (var m in _moons)
                {
                    Vector3 to = m.View.position - h.Reach.origin;
                    float dist = to.magnitude;
                    if (dist < 1e-4f || dist > 2.5f) continue;
                    float align = Vector3.Dot(h.Reach.direction, to / dist);
                    if (align < cos) continue;
                    float score = align - dist * 0.02f;
                    if (score > bestScore) { bestScore = score; best = m; }
                }
            }

            if (best == null) return;

            _held = best;
            _holdingHand = h;
            best.Held = true;
            best.P.Active = false;
            best.Trail.Clear();
            _predictView.gameObject.SetActive(true);
            Hands.Buzz(h, 0.25f, 0.04f);
        }

        void Release()
        {
            var m = _held;
            var h = _holdingHand;
            _held = null;
            _holdingHand = null;
            _predictView.gameObject.SetActive(false);
            if (m == null) return;

            m.Held = false;
            m.Idle = false;
            m.P.Position = m.View.position;
            // One to one, deliberately. Scaling the throw down would make orbits easier to hit
            // and would destroy the thing this world is for: the learner's own arm is the
            // instrument, and the difference between orbit and escape is 8.7 cm/s of wrist.
            m.P.Velocity = h != null ? h.Velocity : Vector3.zero;
            m.P.Active = true;
            m.P.Age = 0f;
            m.P.JustAbsorbed = false;
            m.BoundFor = 0f;

            if (!Sim.Particles.Contains(m.P)) Sim.Particles.Add(m.P);

            var el = Sim.ElementsOf(m.P);
            m.Elements = el;
            RecordRelease(el);

            if (Loop.Stage >= LoopStage.Discover) m.Arrow.gameObject.SetActive(true);
            if (h != null) Hands.Clunk(h, 0.35f);
        }

        void RecordRelease(OrbitElements el)
        {
            var e = Loop.Evidence;
            e.Record(OrbitalEvidence.Release);
            e.Record(el.IsBound ? OrbitalEvidence.Bound : OrbitalEvidence.Unbound);

            switch (el.Kind)
            {
                case ConicKind.Circle:    e.Record(OrbitalEvidence.ConicCircle);    break;
                case ConicKind.Ellipse:   e.Record(OrbitalEvidence.ConicEllipse);   break;
                case ConicKind.Parabola:  e.Record(OrbitalEvidence.ConicParabola);  break;
                case ConicKind.Hyperbola: e.Record(OrbitalEvidence.ConicHyperbola); break;
            }

            if (el.IsBound)
            {
                e.ObserveBest(OrbitalEvidence.BestEccentricity, el.Eccentricity, lowerIsBetter: true);
                if (el.Eccentricity < 0.12f) e.Record(OrbitalEvidence.NearCircular);
            }

            Debug.Log($"[PRISM] release: {el}");
        }

        void SyncViews(float dt)
        {
            var cam = Head != null ? Head : FindHeadCamera();
            if (_conicLabel != null && _conicLabel.Head == null) _conicLabel.Head = cam;

            foreach (var m in _moons)
            {
                if (m.Held) continue;

                if (m.Idle)
                {
                    // Waiting moons drift gently in the cradle so the scene is never quite static.
                    var target = _anchor.TransformPoint(m.CradleOffset);
                    float bob = Mathf.Sin(Time.time * 0.7f + m.CradleOffset.x * 9f) * 0.004f;
                    m.View.position = target + Vector3.up * bob;
                    continue;
                }

                if (m.P.JustAbsorbed)
                {
                    m.P.JustAbsorbed = false;
                    Loop.Evidence.Record(OrbitalEvidence.Absorbed);
                    ReturnToCradle(m);
                    continue;
                }

                if (!m.P.Active) { ReturnToCradle(m); continue; }

                // Gone for good: recycle rather than leaving it to fly forever.
                if ((m.P.Position - _primary.Position).sqrMagnitude > 4f) { ReturnToCradle(m); continue; }

                m.View.position = m.P.Position;
                float speed = m.P.Velocity.magnitude;
                m.Trail.Push(m.P.Position, speed);

                m.Elements = Sim.ElementsOf(m.P);
                m.BoundFor = m.Elements.IsBound ? m.BoundFor + dt : 0f;

                UpdateArrow(m);
            }
        }

        void ReturnToCradle(Moon m)
        {
            m.Idle = true;
            m.P.Active = false;
            m.Trail.Clear();
            m.Arrow.gameObject.SetActive(false);
            m.View.position = _anchor.TransformPoint(m.CradleOffset);
        }

        /// <summary>
        /// The velocity arrow. Grabbing it by the head changes the moon's velocity directly — the
        /// control IS the quantity, which is the interaction principle this whole product runs on.
        /// </summary>
        void UpdateArrow(Moon m)
        {
            if (!m.Arrow.gameObject.activeSelf) return;

            float speed = m.P.Velocity.magnitude;
            if (speed < 1e-5f) { m.Arrow.gameObject.SetActive(false); return; }

            // Arrow length is proportional to speed, scaled so circular speed at the reference
            // radius is a comfortable 8 cm of arrow.
            float len = Mathf.Clamp(speed / PrismScale.CircularSpeed(PrismScale.ReferenceRadius) * 0.08f,
                                    0.02f, 0.30f);
            m.Arrow.position = m.P.Position;
            m.Arrow.rotation = Quaternion.LookRotation(m.P.Velocity.normalized, Vector3.up);
            m.Arrow.localScale = new Vector3(1f, 1f, len);

            // Colour the arrow by how close this speed is to circular here: the learner can see
            // "too fast" and "too slow" without being told a number.
            float r = (m.P.Position - _primary.Position).magnitude;
            float ratio = speed / Mathf.Max(PrismScale.CircularSpeed(r), 1e-5f);
            m.ArrowMat.SetColor("_Tint", PrismPalette.Spectral(Mathf.InverseLerp(0.5f, 1.6f, ratio)));

            if (Hands == null) return;
            TryDragArrow(m);
        }

        void TryDragArrow(Moon m)
        {
            // Pinch near the arrowhead to change the velocity vector. Magnitude and direction come
            // from where the hand is relative to the moon, so both are changed by one gesture.
            var head = m.P.Position + m.P.Velocity.normalized * (m.Arrow.localScale.z);
            foreach (var h in new[] { Hands.Left, Hands.Right })
            {
                if (h == null || !h.IsTracked || h.Pinch < 0.6f) continue;
                if ((h.Position - head).sqrMagnitude > 0.045f * 0.045f) continue;

                Vector3 dir = h.Position - m.P.Position;
                if (dir.sqrMagnitude < 1e-6f) continue;

                float newSpeed = dir.magnitude / 0.08f * PrismScale.CircularSpeed(PrismScale.ReferenceRadius);
                m.P.Velocity = dir.normalized * Mathf.Clamp(newSpeed, 0.01f, 1.2f);
                m.Trail.Clear();

                Loop.Evidence.Record(OrbitalEvidence.HandleUsed);
                var el = Sim.ElementsOf(m.P);
                if (el.IsBound)
                {
                    Loop.Evidence.ObserveBest(OrbitalEvidence.BestEccentricity, el.Eccentricity, true);
                    if (el.Eccentricity < 0.12f) Loop.Evidence.Record(OrbitalEvidence.NearCircular);
                }
                break;
            }
        }

        void UpdateFieldReveal(float dt)
        {
            float want = Loop.Stage >= LoopStage.Discover ? 1f : 0f;
            if (Mathf.Approximately(_fieldReveal, want)) return;

            // Four seconds. Slow enough to read as something arriving rather than being switched on.
            _fieldReveal = Mathf.MoveTowards(_fieldReveal, want, dt / 4f);
            _fieldMat.SetFloat("_Reveal", _fieldReveal);
        }

        void PushFieldBodies()
        {
            var v = new Vector4[8];
            v[0] = new Vector4(_primary.Position.x, _primary.Position.y, _primary.Position.z, _primary.Mu);
            _fieldMat.SetVectorArray("_Bodies", v);
            _fieldMat.SetInt("_BodyCount", 1);
            _fieldMat.SetVector("_Centre", _primary.Position);
        }

        /// <summary>The path the moon WILL take, drawn while it is still in the hand.</summary>
        void UpdatePrediction()
        {
            if (_held == null || _holdingHand == null) return;

            var path = Sim.Predict(_holdingHand.Position, _holdingHand.Velocity, 12f, 96);
            if (path.Count < 2) return;

            for (int i = 0; i < path.Count; i++) path[i] = _anchor.InverseTransformPoint(path[i]);
            PrismMesh.Tube(path, 0.0016f, 5, _predictMesh);
        }

        /// <summary>
        /// The complete conic, drawn from the elements — including the part the moon has not
        /// reached. This is the Formalize stage's whole method: the shape is asserted before it is
        /// traversed, so the learner can check the claim against what then happens.
        /// </summary>
        void UpdateConic()
        {
            if (Loop.Stage < LoopStage.Formalize) return;

            Moon focus = null;
            float best = float.MaxValue;
            foreach (var m in _moons)
            {
                if (m.Idle || m.Held || !m.P.Active) continue;
                float e = m.Elements.Eccentricity;
                if (e < best) { best = e; focus = m; }
            }
            if (focus == null)
            {
                _conicView.gameObject.SetActive(false);
                _conicLabel?.Show(false);
                return;
            }

            _conicView.gameObject.SetActive(true);

            // The shape gets its name only after the learner has drawn it themselves, and the
            // eccentricity is shown beside it so the word and the number arrive together.
            if (_conicLabel != null)
            {
                var el = focus.Elements;
                string body = el.KindName + "   e = " + el.Eccentricity.ToString("0.00");
                if (el.IsBound && !float.IsInfinity(el.Period))
                    body += "\nperiod " + el.Period.ToString("0.0") + " s";
                _conicLabel.SetText(body, PrismPalette.Spectral(Mathf.Clamp01(el.Eccentricity)));
                _conicLabel.PlaceAbove(focus.P.Position, 0.05f);
                _conicLabel.Show(true);
            }
            var pts = ConicPath(focus, 128);
            if (pts.Count < 3) return;
            for (int i = 0; i < pts.Count; i++) pts[i] = _anchor.InverseTransformPoint(pts[i]);
            PrismMesh.Tube(pts, 0.0012f, 5, _conicMesh);
        }

        /// <summary>
        /// Sample the analytic conic r(theta) = p / (1 + e cos theta) in the orbital plane. For an
        /// unbound path we only sample the branch that exists, which is why the sweep is clamped
        /// by the true anomaly at infinity.
        /// </summary>
        List<Vector3> ConicPath(Moon m, int samples)
        {
            var pts = new List<Vector3>(samples);
            var el = m.Elements;
            Vector3 rVec = m.P.Position - _primary.Position;
            float h2 = el.AngularMomentum.sqrMagnitude;
            if (h2 < 1e-12f) return pts;

            float p = h2 / _primary.Mu;                       // semi-latus rectum
            float e = el.Eccentricity;

            // Build the orbital frame: x toward periapsis, z along the motion.
            Vector3 n = el.PlaneNormal;
            Vector3 periDir;
            if (e > 1e-4f)
            {
                float v2 = m.P.Velocity.sqrMagnitude;
                float r = rVec.magnitude;
                Vector3 eVec = ((v2 - _primary.Mu / r) * rVec
                              - Vector3.Dot(rVec, m.P.Velocity) * m.P.Velocity) / _primary.Mu;
                periDir = eVec.normalized;
            }
            else periDir = rVec.normalized;

            Vector3 q = Vector3.Cross(n, periDir).normalized;

            // How far round we may go before the denominator vanishes.
            float sweep = Mathf.PI;
            if (e > 1f) sweep = Mathf.Acos(Mathf.Clamp(-1f / e, -1f, 1f)) * 0.97f;

            for (int i = 0; i < samples; i++)
            {
                float th = -sweep + 2f * sweep * i / (samples - 1);
                float denom = 1f + e * Mathf.Cos(th);
                if (denom < 1e-3f) continue;
                float r = p / denom;
                if (r > 2.5f) continue;
                pts.Add(_primary.Position + (periDir * Mathf.Cos(th) + q * Mathf.Sin(th)) * r);
            }

            // Close a bound orbit so it reads as a completed shape.
            if (el.IsBound && pts.Count > 2) pts.Add(pts[0]);
            return pts;
        }

        // -----------------------------------------------------------------
        // queries used by the challenge and the companion
        // -----------------------------------------------------------------

        public IReadOnlyList<Moon> Moons => _moons;
        public Vector3 PlanetPosition => _primary.Position;
        public float Mu => _primary.Mu;
        public Transform Anchor => _anchor;
    }
}
