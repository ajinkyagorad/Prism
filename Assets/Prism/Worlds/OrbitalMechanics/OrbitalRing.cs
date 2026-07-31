using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Worlds.Orbital
{
    /// <summary>
    /// A debris ring around the display planet.
    ///
    /// Added because the world was a 4 cm planet and five 6 mm moons on an empty table: correct,
    /// and nothing to look at. But decoration alone would have been the wrong fix, so the ring is
    /// chosen to carry the one law the rest of the world only states.
    ///
    /// WHAT IT SHOWS. Every particle is on a circular orbit, so its angular rate is fixed by
    /// Kepler's third law alone:
    ///
    ///     omega(a) = sqrt(mu / a^3)
    ///
    /// The inner edge therefore laps the outer edge, continuously and visibly. A thousand words
    /// about T^2 proportional to a^3 do less than watching a ring shear itself apart. To make that
    /// unmissable, one particle in seven starts on a single radial spoke and is drawn brighter:
    /// the spoke winds into a spiral within a few seconds, and the spiral tightens forever. That is
    /// differential rotation, and it is the reason Saturn's rings cannot be solid.
    ///
    /// WHY IT IS ANALYTIC, NOT INTEGRATED. These are test particles on exactly circular orbits, for
    /// which the closed-form solution is not an approximation of the physics — it IS the physics,
    /// evaluated without truncation error. Pushing 900 particles through the 240 Hz leapfrog would
    /// cost roughly 200k body-steps a second on a mobile chip to reproduce, less accurately, an
    /// answer available in a sine and a cosine.
    ///
    /// WHAT IT HONESTLY DOES NOT SHOW. The moons in this world are massless test particles too, so
    /// they do not perturb the ring, and no resonance gaps open. A real ring has Cassini divisions
    /// carved by real shepherd masses. Rather than draw a gap the simulation does not earn, there
    /// is none — an absent truth beats a painted one.
    /// </summary>
    public class OrbitalRing
    {
        public const int Count = 900;

        /// <summary>One particle in this many starts on the marker spoke.</summary>
        const int SpokeEvery = 7;

        struct Grain
        {
            public float A;          // semi-major axis (= radius, circular), metres
            public float Omega;      // angular rate, rad/s
            public float Phase;      // angle at t = 0
            /// <summary>Inclination and node baked into one constant rotation: both are fixed for
            /// a circular orbit, so recomputing them per frame would be 900 wasted trig pairs.</summary>
            public Quaternion Tilt;
            public float Size;       // drawn radius, metres
            public bool  Spoke;      // part of the shear marker
        }

        readonly Grain[] _grains;
        readonly Mesh _mesh;
        readonly Material _mat;
        readonly Transform _root;

        // Rebuilt every frame; kept as fields so the ring allocates nothing after construction.
        // At 900 grains this is 3600 vertices a frame — the difference between reusing these lists
        // and reallocating them is about 400 kB/frame of garbage.
        readonly List<Vector3> _v = new List<Vector3>(Count * 4);
        readonly List<Vector2> _uv = new List<Vector2>(Count * 4);
        readonly List<Color> _c = new List<Color>(Count * 4);
        readonly List<int> _tri = new List<int>(Count * 6);

        float _t;

        /// <summary>Seconds of ring time elapsed. The spoke's winding is a clock.</summary>
        public float Elapsed => _t;

        /// <summary>
        /// How far the marker spoke has wound, in turns of relative rotation between its inner and
        /// outer ends. Reaches 1 when the inner edge has lapped the outer edge exactly once.
        /// </summary>
        public float SpokeWinding { get; private set; }

        public OrbitalRing(Transform parent, float inner, float outer, float mu, int seed = 4021)
        {
            _root = new GameObject("Ring").transform;
            _root.SetParent(parent, false);

            _mesh = new Mesh { name = "orbitalRing" };
            _mesh.MarkDynamic();
            // 900 grains x 4 verts overflows a 16-bit index buffer at 65535.
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _root.gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;

            _mat = PrismMaterials.New(PrismMaterials.Mote);
            _mat.SetFloat("_VertexColour", 1f);      // the ring colours itself by orbital speed
            // 0.85 drove every grain past the top of the tonemapper, so the speed colouring —
            // the entire point of colouring them — came out uniformly white.
            _mat.SetFloat("_Density", 0.42f);
            _mat.SetFloat("_Softness", 0.75f);
            var mr = _root.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var rng = new System.Random(seed);
            float Rand() => (float)rng.NextDouble();

            _grains = new Grain[Count];
            for (int i = 0; i < Count; i++)
            {
                // Radius distributed so the ring looks denser at its middle. Sampling uniformly in
                // radius makes the inner edge crowded, because area goes as r.
                float u = Rand();
                float shaped = Mathf.Sqrt(u);
                float a = Mathf.Lerp(inner, outer, shaped);

                bool spoke = (i % SpokeEvery) == 0;

                _grains[i] = new Grain
                {
                    A = a,
                    Omega = Mathf.Sqrt(mu / (a * a * a)),
                    // The spoke starts as a straight radial line; everything else is scattered.
                    Phase = spoke ? 0f : Rand() * Mathf.PI * 2f,
                    // A real ring is flat to a part in ten thousand. This one is given a fraction of
                    // a degree so it reads as a volume rather than a decal when seen edge-on. The
                    // tilt axis lies in the ring plane, which is what an ascending node IS.
                    Tilt = Quaternion.AngleAxis(
                        (Rand() - 0.5f) * 1.6f,
                        new Vector3(Mathf.Cos(Rand() * Mathf.PI * 2f), 0f,
                                    Mathf.Sin(Rand() * Mathf.PI * 2f))),
                    Size = Mathf.Lerp(0.0016f, 0.0042f, Rand() * Rand()),
                    Spoke = spoke,
                };
            }

            _innerOmega = Mathf.Sqrt(mu / (inner * inner * inner));
            _outerOmega = Mathf.Sqrt(mu / (outer * outer * outer));
        }

        readonly float _innerOmega, _outerOmega;

        public void SetVisible(bool v) => _root.gameObject.SetActive(v);

        /// <summary>Wind the ring forward and rebuild its mesh. One draw call.</summary>
        public void Tick(float dt, Camera cam, float timeScale = 1f)
        {
            if (cam == null || !_root.gameObject.activeInHierarchy) return;
            _t += dt * timeScale;
            SpokeWinding = (_innerOmega - _outerOmega) * _t / (Mathf.PI * 2f);

            // Billboard basis in the ring's local space, so grains face the learner wherever they
            // stand — the ring must not vanish when viewed exactly edge-on.
            Vector3 right = _root.InverseTransformDirection(cam.transform.right);
            Vector3 up = _root.InverseTransformDirection(cam.transform.up);

            _v.Clear(); _uv.Clear(); _c.Clear(); _tri.Clear();

            for (int i = 0; i < _grains.Length; i++)
            {
                ref var g = ref _grains[i];
                float th = g.Phase + g.Omega * _t;

                // Circular orbit in its own plane, then tilted onto its inclined one.
                var p = g.Tilt * new Vector3(g.A * Mathf.Cos(th), 0f, g.A * Mathf.Sin(th));

                // Speed maps to colour on the same convention the trails use: slow is cool and
                // dim, fast is warm and bright. Here it doubles as a readout of Kepler's third law,
                // because v = sqrt(mu/a) falls off with radius across the ring.
                float speed = g.Omega * g.A;
                float f = Mathf.InverseLerp(_outerOmega * 0.9f, _innerOmega * 0.35f, speed);
                Color col = Color.Lerp(PrismPalette.Cyan, PrismPalette.Gold, Mathf.Clamp01(f));

                float alpha = 0.42f + 0.34f * Mathf.Clamp01(f);
                float size = g.Size;
                if (g.Spoke)
                {
                    // The marker: brighter, larger, and unmistakably one population.
                    col = Color.Lerp(col, PrismPalette.Warm, 0.55f);
                    alpha = 0.95f;
                    size *= 1.5f;
                }

                var r = right * size;
                var u = up * size;
                int b = _v.Count;
                _v.Add(p - r - u); _uv.Add(new Vector2(0, 0)); _c.Add(col * 1f);
                _v.Add(p + r - u); _uv.Add(new Vector2(1, 0)); _c.Add(col);
                _v.Add(p - r + u); _uv.Add(new Vector2(0, 1)); _c.Add(col);
                _v.Add(p + r + u); _uv.Add(new Vector2(1, 1)); _c.Add(col);
                for (int q = b; q < b + 4; q++)
                {
                    var cc = _c[q]; cc.a = alpha; _c[q] = cc;
                }
                _tri.Add(b); _tri.Add(b + 2); _tri.Add(b + 1);
                _tri.Add(b + 1); _tri.Add(b + 2); _tri.Add(b + 3);
            }

            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_tri, 0);
            _mesh.RecalculateBounds();
        }
    }
}
