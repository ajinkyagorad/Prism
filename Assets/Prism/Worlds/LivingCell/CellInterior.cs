using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Worlds.LivingCell
{
    /// <summary>
    /// The anatomy inside the membrane: nucleus, mitochondria, and a field of ribosomes.
    ///
    /// WHY THIS EXISTS. Rendered, the Living Cell was a translucent blue bag with four glucose
    /// motes beside it. Everything the simulation computes was correct and almost none of it was
    /// visible, so a world about the busiest object in biology looked like an empty balloon.
    ///
    /// WHY IT IS NOT DECORATION. Every part here is driven by a quantity the sim already computes,
    /// so the anatomy is a readout rather than a backdrop:
    ///
    ///   mitochondria  brightness tracks LastCatabolismRate — they light when the cell is burning
    ///   ribosomes     population tracks AtpFraction — protein synthesis is the cell's ATP sink,
    ///                 so they thin out when the learner starves the cell and crowd back when it
    ///                 recovers
    ///   nucleus       steady; it is the one structure that should look unbothered, and its calm
    ///                 is the reference against which the rest visibly reacts
    ///
    /// The learner can neither grab nor break any of it. That is deliberate: this is the cell being
    /// itself, and the lesson is that it keeps running whether or not you are helping.
    /// </summary>
    public class CellInterior
    {
        const int RibosomeCount = 220;
        const int MitochondriaCount = 9;

        readonly Transform _root;
        readonly Transform _nucleus;
        readonly Transform[] _mito;
        readonly Material[] _mitoMat;
        readonly Material _riboMat;

        readonly Mesh _riboMesh;
        readonly Vector3[] _riboPos;
        readonly float[] _riboPhase;
        readonly float[] _riboSize;

        readonly List<Vector3> _v = new List<Vector3>(RibosomeCount * 4);
        readonly List<Vector2> _uv = new List<Vector2>(RibosomeCount * 4);
        readonly List<Color> _c = new List<Color>(RibosomeCount * 4);
        readonly List<int> _tri = new List<int>(RibosomeCount * 6);

        float _t;

        public CellInterior(Transform parent, float membraneRadius, int seed = 91117)
        {
            _root = new GameObject("Interior").transform;
            _root.SetParent(parent, false);

            var rng = new System.Random(seed);
            float Rand() => (float)rng.NextDouble();

            // ---- nucleus ------------------------------------------------------------------
            // Off-centre and large. A cell drawn with a concentric nucleus reads as a diagram;
            // real ones sit wherever the cytoskeleton has left room.
            var nucMat = PrismMaterials.New(PrismMaterials.Gel);
            nucMat.SetColor("_Tint", PrismPalette.Violet);
            nucMat.SetColor("_DeepTint", PrismPalette.Lavender);
            nucMat.SetFloat("_Density", 1.5f);
            nucMat.SetFloat("_NoiseFreq", 7.5f);
            nucMat.SetFloat("_FlowSpeed", 0.05f);
            _nucleus = Part(PrismMesh.Icosphere(3), membraneRadius * 0.34f, nucMat, "Nucleus");
            _nucleus.localPosition = new Vector3(-0.22f, 0.16f, -0.10f) * membraneRadius;

            // ---- mitochondria -------------------------------------------------------------
            _mito = new Transform[MitochondriaCount];
            _mitoMat = new Material[MitochondriaCount];
            for (int i = 0; i < MitochondriaCount; i++)
            {
                _mitoMat[i] = PrismMaterials.CeramicBody(PrismPalette.Coral, 0.45f, 420f);
                var t = Part(PrismMesh.Icosphere(2), membraneRadius * 0.16f, _mitoMat[i], $"Mitochondrion{i}");

                // Scattered through the shell between nucleus and membrane, where they actually sit.
                float a = Rand() * Mathf.PI * 2f;
                float b = Mathf.Acos(1f - 2f * Rand());
                float r = Mathf.Lerp(0.45f, 0.80f, Rand()) * membraneRadius;
                t.localPosition = new Vector3(Mathf.Sin(b) * Mathf.Cos(a),
                                              Mathf.Cos(b) * 0.7f,
                                              Mathf.Sin(b) * Mathf.Sin(a)) * r;
                // Elongated: a mitochondrion is a rod, not a ball, and the silhouette is most of
                // how you recognise one.
                t.localScale = new Vector3(1f, 0.48f, 0.48f) * (membraneRadius * 0.16f);
                t.localRotation = Quaternion.Euler(Rand() * 360f, Rand() * 360f, Rand() * 360f);
                _mito[i] = t;
            }

            // ---- ribosomes ----------------------------------------------------------------
            _riboMat = PrismMaterials.New(PrismMaterials.Mote);
            _riboMat.SetFloat("_VertexColour", 1f);
            _riboMat.SetColor("_Tint", PrismPalette.Mint);
            _riboMat.SetColor("_EdgeTint", PrismPalette.Cyan);
            _riboMat.SetFloat("_Density", 0.75f);
            _riboMat.SetFloat("_Softness", 0.8f);
            _riboMat.renderQueue = InteriorQueue;

            var go = new GameObject("Ribosomes");
            go.transform.SetParent(_root, false);
            _riboMesh = new Mesh { name = "ribosomes" };
            _riboMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _riboMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _riboMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _riboPos = new Vector3[RibosomeCount];
            _riboPhase = new float[RibosomeCount];
            _riboSize = new float[RibosomeCount];
            for (int i = 0; i < RibosomeCount; i++)
            {
                float a = Rand() * Mathf.PI * 2f;
                float b = Mathf.Acos(1f - 2f * Rand());
                // Cube root gives a uniform density through the volume; without it every ribosome
                // crowds the membrane and the middle of the cell looks hollow.
                float r = Mathf.Pow(Rand(), 1f / 3f) * membraneRadius * 0.90f;
                _riboPos[i] = new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Cos(b), Mathf.Sin(b) * Mathf.Sin(a)) * r;
                _riboPhase[i] = Rand();
                _riboSize[i] = Mathf.Lerp(0.0018f, 0.0042f, Rand() * Rand());
            }
        }

        /// <summary>
        /// Everything inside the cell is transparent, and so is the membrane around it. Unity sorts
        /// transparent renderers by distance to their origin, and the interior shares an origin
        /// with the membrane, so the tie broke arbitrarily and the membrane painted over the
        /// anatomy — the nucleus survived only because its offset happened to put it nearer the
        /// camera. Forcing the interior earlier in the queue draws the contents first and blends
        /// the bag over them, which is the right order for looking INTO something.
        /// </summary>
        const int InteriorQueue = 2950;

        Transform Part(Mesh mesh, float radius, Material mat, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mat.renderQueue = InteriorQueue;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = Vector3.one * radius;
            return go.transform;
        }

        /// <summary>
        /// Drive the anatomy from the simulation.
        /// </summary>
        /// <param name="catabolism">LastCatabolismRate — how hard the cell is burning right now.</param>
        /// <param name="atpFraction">Atp / pool total, 0..1.</param>
        public void Tick(float dt, Camera cam, float catabolism, float atpFraction, float catabolismRef)
        {
            _t += dt;

            // Mitochondria light with the burn rate. Normalised against a reference rate rather
            // than an absolute, so the mapping still reads if the sim is retuned.
            float burn = Mathf.Clamp01(catabolism / Mathf.Max(catabolismRef, 1e-6f));
            for (int i = 0; i < _mito.Length; i++)
            {
                // Each lags the others slightly: a cell is not a single switch.
                float lag = 0.75f + 0.25f * Mathf.Sin(_t * 1.7f + i * 1.3f);
                _mitoMat[i].SetFloat("_Luminance", Mathf.Lerp(0.10f, 0.85f, burn * lag));
            }

            if (cam == null) return;

            // Ribosome population follows ATP: synthesis is what the cell spends its ATP on, so
            // starving it visibly empties the cytoplasm.
            int live = Mathf.RoundToInt(Mathf.Lerp(RibosomeCount * 0.25f, RibosomeCount,
                                                   Mathf.Clamp01(atpFraction)));

            Vector3 right = _root.InverseTransformDirection(cam.transform.right);
            Vector3 up = _root.InverseTransformDirection(cam.transform.up);

            _v.Clear(); _uv.Clear(); _c.Clear(); _tri.Clear();
            for (int i = 0; i < live; i++)
            {
                // Cytoplasmic streaming: a slow circulation, not Brownian jitter, because streaming
                // is what you would actually see down a microscope.
                var p = _riboPos[i];
                float ang = dt * (0.25f + _riboPhase[i] * 0.35f);
                float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                p = new Vector3(p.x * cs - p.z * sn, p.y, p.x * sn + p.z * cs);
                p.y += Mathf.Sin(_t * 0.6f + _riboPhase[i] * 8f) * 0.00015f;
                _riboPos[i] = p;

                var col = Color.Lerp(PrismPalette.Cyan, PrismPalette.Mint, _riboPhase[i]);
                col.a = 0.55f + 0.35f * Mathf.Clamp01(atpFraction);

                float s = _riboSize[i];
                var r = right * s; var u = up * s;
                int b = _v.Count;
                _v.Add(p - r - u); _uv.Add(new Vector2(0, 0)); _c.Add(col);
                _v.Add(p + r - u); _uv.Add(new Vector2(1, 0)); _c.Add(col);
                _v.Add(p - r + u); _uv.Add(new Vector2(0, 1)); _c.Add(col);
                _v.Add(p + r + u); _uv.Add(new Vector2(1, 1)); _c.Add(col);
                _tri.Add(b); _tri.Add(b + 2); _tri.Add(b + 1);
                _tri.Add(b + 1); _tri.Add(b + 2); _tri.Add(b + 3);
            }

            _riboMesh.Clear();
            _riboMesh.SetVertices(_v);
            _riboMesh.SetUVs(0, _uv);
            _riboMesh.SetColors(_c);
            _riboMesh.SetTriangles(_tri, 0);
            _riboMesh.RecalculateBounds();
        }
    }
}
