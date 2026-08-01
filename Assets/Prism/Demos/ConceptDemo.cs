using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Demos
{
    /// <summary>
    /// A pocket demonstration: the live, manipulable phenomenon that blooms out of a concept when
    /// the learner takes hold of it.
    ///
    /// This exists because the constellation without it is a shelf of labels. Seventeen concepts
    /// could be reached, named, and then did nothing — which is exactly what "no content" means.
    /// The brief's central claim is that a concept is an *inhabitable, manipulable phenomenon*, and
    /// this is the smallest honest version of that: not a whole world, but a real thing that runs
    /// on real rules and answers the hand.
    ///
    /// THE INTERACTION GRAMMAR, consistent across all seventeen:
    ///
    ///     the HOLDING hand carries the phenomenon
    ///     the FREE hand operates it
    ///
    /// That is worth stating once and obeying everywhere. It means a learner who has worked out how
    /// to operate one demonstration has worked out how to operate all of them, and it uses the body
    /// the way the brief asks — two hands, no buttons, no menus.
    ///
    /// Every demonstration is procedural and self-contained. Each runs real rules rather than an
    /// animation: the gravity well is a real potential, angular momentum is really conserved, the
    /// wave on the string really propagates. A demonstration that merely mimed its concept would be
    /// worse than none, because the learner would build intuition from a lie.
    /// </summary>
    public abstract class ConceptDemo : MonoBehaviour
    {
        public PrismHands Hands;
        public Camera Head;
        public ConceptDefinition Concept;
        public Companion.PrismVoice Voice;

        /// <summary>The node this hangs off. The demo follows it.</summary>
        public Transform Carrier;

        /// <summary>Which hand is holding the concept. The other one operates the demonstration.</summary>
        public PrismHands.Hand Holder;

        /// <summary>Size of the demonstration in metres, at full openness.</summary>
        public float Size = 0.17f;

        float _open;
        bool _closing;
        float _age;
        readonly List<Material> _materials = new List<Material>();

        /// <summary>0 while blooming, 1 when fully present.</summary>
        protected float Openness => _open;

        /// <summary>Seconds since this demonstration opened.</summary>
        protected float Age => _age;

        /// <summary>The hand that is NOT holding the concept. May be untracked; always null-check.</summary>
        protected PrismHands.Hand Free
        {
            get
            {
                if (Hands == null) return null;
                return Holder == Hands.Right ? Hands.Left : Hands.Right;
            }
        }

        /// <summary>Free-hand position in this demonstration's local space, or null if unavailable.</summary>
        protected bool TryFreeLocal(out Vector3 local)
        {
            local = Vector3.zero;
            var f = Free;
            if (f == null || !f.IsTracked) return false;
            local = transform.InverseTransformPoint(PrismHands.PointOf(f));
            return true;
        }

        /// <summary>How hard the free hand is pinching, 0 if it is not there.</summary>
        protected float FreePinch => (Free != null && Free.IsTracked) ? Free.Pinch : 0f;

        protected Color Tint => Concept != null ? Concept.Colour : PrismPalette.Cyan;

        void Start()
        {
            Build();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            _open = Mathf.MoveTowards(_open, _closing ? 0f : 1f, dt * 2.4f);
            _age += dt;

            if (Carrier != null)
            {
                // Sits just above the concept in the hand, so it never occludes the thing it came
                // from and never collides with the fingers holding it.
                transform.position = Carrier.position + Vector3.up * (Size * 0.85f);
            }

            // Faces the learner, yaw only. Demonstrations that inherit head roll are nauseating and
            // demonstrations that inherit pitch swing wildly when someone looks down at their hand.
            var cam = Head != null ? Head : Worlds.Orbital.OrbitalWorld.FindHeadCamera();
            if (cam != null)
            {
                Vector3 to = transform.position - cam.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            }

            float s = Mathf.SmoothStep(0f, 1f, _open) * Size;
            transform.localScale = Vector3.one * Mathf.Max(s, 1e-4f);

            if (_open > 0.001f) OnTick(dt);

            if (_closing && _open <= 0.0001f) Destroy(gameObject);
        }

        /// <summary>Begin fading out. The object destroys itself when it has gone.</summary>
        public void Close() => _closing = true;

        /// <summary>Build the geometry. Everything is in local space on a unit-ish scale (about -1..1).</summary>
        public abstract void Build();

        /// <summary>Run the rules and read the free hand.</summary>
        protected abstract void OnTick(float dt);

        // -----------------------------------------------------------------
        // geometry helpers — these keep each demonstration short enough to read in one sitting
        // -----------------------------------------------------------------

        protected Transform Body(Mesh mesh, float radius, Material mat, string name = "part")
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * radius;
            Track(mat);
            return go.transform;
        }

        protected Transform Ball(float radius, Color colour, float luminance = 0.5f, string name = "ball")
            => Body(PrismMesh.Icosphere(2), radius, PrismMaterials.CeramicBody(colour, luminance), name);

        protected Transform Cloud(float radius, Color colour, string name = "cloud")
        {
            var m = PrismMaterials.New(PrismMaterials.Volumetric);
            m.SetColor("_Tint", colour);
            m.SetColor("_EdgeTint", PrismPalette.Warm);
            m.SetFloat("_Density", 0.9f);
            return Body(PrismMesh.Icosphere(3), radius, m, name);
        }

        /// <summary>A rebuildable tube. Call <see cref="CurveView.Set"/> every frame.</summary>
        protected class CurveView
        {
            public Transform T;
            public Mesh Mesh;
            public Material Mat;
            public float Radius = 0.012f;
            public int Sides = 5;
            readonly List<Vector3> _pts = new List<Vector3>();

            public void Set(IList<Vector3> points)
            {
                if (points == null || points.Count < 2) { Mesh.Clear(); return; }
                PrismMesh.Tube(points, Radius, Sides, Mesh);
            }

            public void Set(System.Func<float, Vector3> f, int samples)
            {
                _pts.Clear();
                for (int i = 0; i < samples; i++) _pts.Add(f(i / (float)(samples - 1)));
                Set(_pts);
            }
        }

        protected CurveView Curve(Color colour, float radius = 0.012f, float packets = 0f)
        {
            var go = new GameObject("curve");
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "demoCurve" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mat = PrismMaterials.New(PrismMaterials.Flow);
            mat.SetColor("_Tint", colour);
            mat.SetFloat("_Strength", 0.9f);
            mat.SetFloat("_CoreGain", 0.55f);
            mat.SetFloat("_Packets", Mathf.Max(1f, packets));
            mat.SetFloat("_Pulse", packets > 0f ? 1f : 0f);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            Track(mat);

            return new CurveView { T = go.transform, Mesh = mesh, Mat = mat, Radius = radius };
        }

        /// <summary>An arrow that can be aimed each frame. Length is in local units.</summary>
        protected class ArrowView
        {
            public Transform T;
            public Material Mat;

            public void Aim(Vector3 from, Vector3 dir, float length)
            {
                if (dir.sqrMagnitude < 1e-8f || length <= 1e-5f) { T.gameObject.SetActive(false); return; }
                T.gameObject.SetActive(true);
                T.localPosition = from;
                T.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                T.localScale = new Vector3(1f, 1f, length);
            }
        }

        protected ArrowView MakeArrow(Color colour, float thickness = 1f)
        {
            var go = new GameObject("arrow");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh =
                PrismMesh.Arrow(0.012f * thickness, 0.036f * thickness, 0.10f);
            var mat = PrismMaterials.CeramicBody(colour, 0.6f);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            Track(mat);
            return new ArrowView { T = go.transform, Mat = mat };
        }

        /// <summary>
        /// A deformable sheet in the XZ plane, for potential wells and fields. Returns the mesh so
        /// the demonstration can push its vertices around; call <see cref="SheetApply"/> after.
        /// </summary>
        protected Mesh Sheet(int res, Material mat, out Transform t, float extent = 1f)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                {
                    float u = x / (float)res, v = z / (float)res;
                    verts.Add(new Vector3((u - 0.5f) * 2f * extent, 0f, (v - 0.5f) * 2f * extent));
                    uvs.Add(new Vector2(u, v));
                }
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int i0 = z * (res + 1) + x, i1 = i0 + 1, i2 = i0 + res + 1, i3 = i2 + 1;
                    tris.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
                }

            var mesh = new Mesh { name = "demoSheet" };
            mesh.MarkDynamic();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();

            var go = new GameObject("sheet");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            Track(mat);
            t = go.transform;
            return mesh;
        }

        protected static void SheetApply(Mesh mesh, List<Vector3> verts)
        {
            mesh.SetVertices(verts);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        protected Material FlatMaterial(Color colour, float luminance = 0.45f)
        {
            var m = PrismMaterials.CeramicBody(colour, luminance);
            Track(m);
            return m;
        }

        protected void Track(Material m) { if (m != null) _materials.Add(m); }

        /// <summary>
        /// Draw a straight segment without allocating.
        ///
        /// Several demonstrations redraw a string or a strut every frame. Building a fresh
        /// List&lt;Vector3&gt; each time is death by a thousand cuts in VR — the pendulum wave alone
        /// would have made fifteen of them per frame, about a thousand allocations a second, which
        /// is exactly the kind of steady GC churn that shows up as periodic hitching rather than as
        /// a low frame rate.
        /// </summary>
        readonly List<Vector3> _segment = new List<Vector3> { Vector3.zero, Vector3.zero };
        protected void Segment(CurveView c, Vector3 a, Vector3 b)
        {
            _segment[0] = a;
            _segment[1] = b;
            c.Set(_segment);
        }

        void OnDestroy()
        {
            foreach (var m in _materials) if (m != null) Destroy(m);
            _materials.Clear();
        }

        /// <summary>Fade a material with the bloom, so nothing pops into being.</summary>
        protected void FadeWithOpenness(Material m, float baseAlpha = 1f)
        {
            if (m == null || !m.HasProperty("_Tint")) return;
            var c = m.GetColor("_Tint");
            c.a = baseAlpha * Mathf.SmoothStep(0f, 1f, _open);
            m.SetColor("_Tint", c);
        }
    }
}
