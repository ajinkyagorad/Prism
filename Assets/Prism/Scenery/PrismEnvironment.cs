using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Scenery
{
    /// <summary>
    /// The place the atrium happens in.
    ///
    /// NOTE the namespace is Prism.Scenery, not Prism.Environment: a namespace called
    /// Environment shadows System.Environment for every file in the assembly, which silently
    /// breaks Environment.GetEnvironmentVariable anywhere else in the project.
    ///
    /// The brief asked for "an immense, calm, white space with no obvious walls or floor
    /// boundary". That was faithful on paper and wrong in a headset: with no horizon, no ground
    /// and no depth cues, the eye has nothing to measure against, so every floating concept reads
    /// as a flat blob at an unknowable distance and the whole space feels like a loading screen.
    ///
    /// What is here instead keeps the intent — immense, calm, no walls — and adds the three things
    /// that make a space legible: a HORIZON (scale), GROUND UNDERFOOT (where you are), and AERIAL
    /// PERSPECTIVE (how far away things are). A high plateau at dawn, falling into a mist-filled
    /// valley, with ranges beyond.
    ///
    /// Everything is generated. No textures, no models, nothing downloaded: the sky is real
    /// Rayleigh and Mie scattering, and the land is noise. That keeps the APK small and, more to
    /// the point, keeps this from becoming a generic asset-store landscape — the one outcome that
    /// would cost PRISM its identity.
    /// </summary>
    public class PrismEnvironment : MonoBehaviour
    {
        [Header("Sun")]
        [Tooltip("Degrees above the horizon. Low is a warmer, longer-shadowed, calmer light.")]
        // Stays at dawn. A previous attempt to fix the dark band across the middle of the atrium
        // raised this to 27 degrees and appeared to help — it did not. This field is SERIALIZED into
        // the scene, so editing the default here changes nothing for a scene that already exists,
        // and the improvement came entirely from the landform change in Height(). Verify reporting
        // the scene's actual value ("sun 14.0 deg") is what caught it. Anything tuned here has to be
        // re-applied to the scene by rebuilding it, or it is a change that only looks applied.
        [Range(-5f, 80f)] public float SunElevation = 14f;
        [Range(0f, 360f)] public float SunAzimuth = 35f;
        public float SunIntensity = 22f;
        public Color SunColour = new Color(1f, 0.94f, 0.86f);

        [Tooltip("1 clear, 6 hazy. Higher reads as softer and further away.")]
        [Range(1f, 8f)] public float Turbidity = 2.6f;

        [Header("Land")]
        [Tooltip("Metres. Beyond this the ground is drawn but never walked on.")]
        public float Extent = 900f;
        [Tooltip("Metres. The level ground the learner actually stands on.")]
        public float PlateauRadius = 22f;
        public int RadialSegments = 72;
        public int Rings = 56;
        public int Seed = 20260730;

        [Header("Backdrop")]
        [Tooltip("How bright the world behind a concept is, 0..1. Drives Prism_Compose in every " +
                 "material — too low and concepts glare, too high and they vanish.")]
        [Range(0f, 1f)] public float BackdropLuma = 0.62f;

        Transform _sky;
        Transform _ground;
        Material _skyMat;
        Material _groundMat;

        public Vector3 SunDirection { get; private set; }

        void Awake()
        {
            BuildSky();
            BuildGround();
            Publish();
        }

        void OnValidate() { if (Application.isPlaying) Publish(); }

        void Update()
        {
            // The sky dome and the land follow the learner in the horizontal plane only, so they
            // can never be walked out of, and the horizon never moves relative to the eye. Height
            // is NOT followed — otherwise the ground would rise with the learner's head and the
            // whole point of having a floor is lost.
            var cam = Worlds.Orbital.OrbitalWorld.FindHeadCamera();
            if (cam == null) return;
            var p = cam.transform.position;
            if (_sky != null) _sky.position = p;
            if (_ground != null) _ground.position = new Vector3(p.x, _ground.position.y, p.z);
        }

        void BuildSky()
        {
            var go = new GameObject("Sky");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(3);
            _skyMat = PrismMaterials.New(PrismMaterials.Sky);
            go.AddComponent<MeshRenderer>().sharedMaterial = _skyMat;
            // Inside the far clip (200 m) but far outside anything else.
            go.transform.localScale = Vector3.one * 160f;
            _sky = go.transform;
        }

        void BuildGround()
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = BuildTerrainMesh();
            _groundMat = PrismMaterials.New(PrismMaterials.Ground);
            go.AddComponent<MeshRenderer>().sharedMaterial = _groundMat;
            _ground = go.transform;

            // The learner stands at y = 0 on the plateau; the terrain is placed just under their
            // feet rather than at the tracking origin, which may be anywhere.
            _ground.localPosition = new Vector3(0f, -1.6f, 0f);
        }

        /// <summary>
        /// A radial terrain mesh centred on the learner.
        ///
        /// Radial rather than a square grid for two reasons: there is no edge to see whichever way
        /// they turn, and ring spacing can grow with distance, so a 900 m view costs about four
        /// thousand vertices instead of hundreds of thousands. The near rings are dense enough to
        /// hold the plateau edge; the far ones only have to describe mountains.
        /// </summary>
        Mesh BuildTerrainMesh()
        {
            int seg = Mathf.Max(16, RadialSegments);
            int rings = Mathf.Max(8, Rings);

            var verts = new List<Vector3>((seg + 1) * (rings + 1));
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            var rng = new System.Random(Seed);
            var offset = new Vector3((float)rng.NextDouble() * 1000f,
                                     (float)rng.NextDouble() * 1000f,
                                     (float)rng.NextDouble() * 1000f);

            for (int ring = 0; ring <= rings; ring++)
            {
                float f = (float)ring / rings;
                // Exponential spacing: metres of detail where the learner is, kilometres of reach
                // where they are only looking.
                float r = Mathf.Pow(f, 2.6f) * Extent;

                for (int s = 0; s <= seg; s++)
                {
                    float a = (float)s / seg * Mathf.PI * 2f;
                    var pos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    pos.y = Height(pos + offset, r);
                    verts.Add(pos);
                    uvs.Add(new Vector2(r / Extent, f));
                    norms.Add(Vector3.up);
                }
            }

            int stride = seg + 1;
            for (int ring = 0; ring < rings; ring++)
                for (int s = 0; s < seg; s++)
                {
                    int i0 = ring * stride + s, i1 = i0 + 1;
                    int i2 = i0 + stride, i3 = i2 + 1;
                    tris.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
                }

            var mesh = new Mesh { name = "PrismTerrain" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Terrain height at a point, metres.
        ///
        /// Flat under the learner, a soft lip at the plateau edge, then a valley, then ranges that
        /// grow with distance. The plateau is not decoration: standing on visibly level ground is
        /// what stops the scene reading as "floating above a landscape", which is disorienting in
        /// a way a flat white void at least avoided.
        /// </summary>
        float Height(Vector3 p, float radius)
        {
            float hills = Fbm(p * 0.0032f, 5) - 0.5f;
            float ridges = 1f - Mathf.Abs(Fbm(p * 0.0011f, 4) * 2f - 1f);   // ridged noise

            // Distant relief grows with radius; nothing tall is allowed close by.
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PlateauRadius * 2.2f, Extent * 0.55f, radius));
            float h = hills * 14f * far + Mathf.Pow(ridges, 2.2f) * 150f * far * far;

            // A shallow basin just beyond the plateau, so the mist has somewhere to sit.
            //
            // This was a 26 m trench. Renders showed why that was wrong: the atrium deliberately
            // faces the sun, so the near wall of the trench faces away from it, and a ring of
            // sunless slope seen through the mist that had pooled in it drew a hard dark band right
            // across the middle of every view of the home scene. Lighting could not fix it — the
            // band is a landform, so the landform is what had to change.
            float valley = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PlateauRadius, PlateauRadius * 3.5f, radius));
            h -= valley * 4f * (1f - far * 0.7f);

            // Flatten to exactly level inside the plateau, with a soft lip.
            float plateau = 1f - Mathf.SmoothStep(PlateauRadius * 0.72f, PlateauRadius * 1.35f, radius);
            h = Mathf.Lerp(h, 0f, plateau);

            return h;
        }

        static float Fbm(Vector3 p, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(p) * amp;
                norm += amp;
                p *= 2.07f;
                amp *= 0.5f;
            }
            return sum / Mathf.Max(norm, 1e-5f);
        }

        /// <summary>Value noise matching the shape of Prism_Value3D, so land and materials agree.</summary>
        static float Noise(Vector3 p)
        {
            Vector3 i = new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
            Vector3 f = p - i;
            f = new Vector3(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y), f.z * f.z * (3f - 2f * f.z));

            float n000 = Hash(i + new Vector3(0, 0, 0)), n100 = Hash(i + new Vector3(1, 0, 0));
            float n010 = Hash(i + new Vector3(0, 1, 0)), n110 = Hash(i + new Vector3(1, 1, 0));
            float n001 = Hash(i + new Vector3(0, 0, 1)), n101 = Hash(i + new Vector3(1, 0, 1));
            float n011 = Hash(i + new Vector3(0, 1, 1)), n111 = Hash(i + new Vector3(1, 1, 1));

            return Mathf.Lerp(
                Mathf.Lerp(Mathf.Lerp(n000, n100, f.x), Mathf.Lerp(n010, n110, f.x), f.y),
                Mathf.Lerp(Mathf.Lerp(n001, n101, f.x), Mathf.Lerp(n011, n111, f.x), f.y), f.z);
        }

        static float Hash(Vector3 p)
        {
            p = new Vector3(Frac(p.x * 0.3183099f + 0.1f), Frac(p.y * 0.3183099f + 0.1f), Frac(p.z * 0.3183099f + 0.1f));
            p *= 17f;
            return Frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>Publish the sun and backdrop to every shader that needs them.</summary>
        public void Publish()
        {
            float el = SunElevation * Mathf.Deg2Rad;
            float az = SunAzimuth * Mathf.Deg2Rad;
            SunDirection = new Vector3(Mathf.Cos(el) * Mathf.Sin(az),
                                       Mathf.Sin(el),
                                       Mathf.Cos(el) * Mathf.Cos(az)).normalized;

            Shader.SetGlobalVector("_PrismSunDir",
                new Vector4(SunDirection.x, SunDirection.y, SunDirection.z, SunIntensity));
            Shader.SetGlobalColor("_PrismSunColour", SunColour);
            Shader.SetGlobalFloat("_PrismTurbidity", Turbidity);
            Shader.SetGlobalFloat("_PrismGroundLuma", 1f);
            Shader.SetGlobalFloat("_PrismBackdropLuma", BackdropLuma);
        }

        /// <summary>Hide the landscape (entering passthrough) or bring it back.</summary>
        public void SetVisible(bool visible)
        {
            if (_sky != null) _sky.gameObject.SetActive(visible);
            if (_ground != null) _ground.gameObject.SetActive(visible);

            // With passthrough behind it, the backdrop is the learner's room: mid-toned and
            // unknowable, so sit in the middle rather than assuming either extreme.
            Shader.SetGlobalFloat("_PrismBackdropLuma", visible ? BackdropLuma : 0.45f);
        }
    }
}
