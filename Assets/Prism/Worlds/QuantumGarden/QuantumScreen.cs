using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Worlds.QuantumGarden
{
    /// <summary>
    /// The detector screen: three coplanar layers, closest to furthest from the learner.
    ///
    ///   Detections   one small quad per landed particle. The empirical record — never erased
    ///                except when the learner deliberately starts a fresh plate by changing
    ///                which slits are open.
    ///   Field        Prism/QuantumField: the live |psi|^2 sum, hidden until Discover.
    ///   Backing      an opaque plate behind both, so the screen reads as a physical object.
    ///
    /// Dots are stored in a fixed-capacity ring buffer (<see cref="MaxDots"/>) so a long session
    /// costs the same each update regardless of how many particles have landed in total: once
    /// full, a new detection overwrites the oldest rather than growing the mesh forever. Dots do
    /// not need to face the camera — they are marks IN the screen's own plane, like ink on a
    /// plate — so, unlike PrismTrailRibbon or the companion's motes, nothing here has to be
    /// rebuilt every frame; the mesh is only touched on the frame a new detection actually
    /// arrives.
    /// </summary>
    public class QuantumScreen
    {
        public const int MaxDots = 2500;
        const float DotHalfSize = 0.0038f;

        readonly List<Vector3> _dotVerts = new List<Vector3>(MaxDots * 4);
        readonly List<Vector2> _dotUVs = new List<Vector2>(MaxDots * 4);
        readonly List<int> _dotTris = new List<int>(MaxDots * 6);

        Mesh _dotMesh;
        int _dotWriteHead;
        bool _dotDirty;

        readonly Vector4[] _sourceBuffer = new Vector4[20];

        public Material FieldMaterial { get; private set; }
        public Material BackingMaterial { get; private set; }
        public Material DotMaterial { get; private set; }
        public Transform Root { get; private set; }

        /// <summary>How many detections are currently visible (saturates at <see cref="MaxDots"/>).</summary>
        public int LiveDotCount { get; private set; }

        public void Build(Transform anchor)
        {
            var root = new GameObject("Screen");
            root.transform.SetParent(anchor, false);
            root.transform.localPosition = new Vector3(0f, 0f, TwoSlitSim.ScreenZ);
            Root = root.transform;

            BuildBacking();
            BuildField();
            BuildDots();
        }

        void BuildBacking()
        {
            var go = new GameObject("Backing");
            go.transform.SetParent(Root, false);

            var mesh = QuantumMesh.Rect(TwoSlitSim.ScreenHalfWidth * 2f + 0.03f,
                                        TwoSlitSim.ScreenHalfHeight * 2f + 0.03f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            // A dim, quiet plate: it exists to give the screen a physical presence and to
            // occlude the source behind it, not to compete with the dots or the field.
            BackingMaterial = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.08f, 0f);
            go.AddComponent<MeshRenderer>().sharedMaterial = BackingMaterial;
        }

        void BuildField()
        {
            var go = new GameObject("Field");
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.002f);   // a hair closer than the backing

            var mesh = QuantumMesh.Rect(TwoSlitSim.ScreenHalfWidth * 2f, TwoSlitSim.ScreenHalfHeight * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            FieldMaterial = PrismMaterials.New("Prism/QuantumField");
            FieldMaterial.SetFloat("_Reveal", 0f);
            FieldMaterial.SetFloat("_SlitRelZ", TwoSlitSim.SlitZ - TwoSlitSim.ScreenZ);
            FieldMaterial.SetFloat("_HalfWidth", TwoSlitSim.ScreenHalfWidth);
            FieldMaterial.SetFloat("_HalfHeight", TwoSlitSim.ScreenHalfHeight);
            FieldMaterial.SetFloat("_EdgeSoften", 0.025f);
            FieldMaterial.SetFloat("_Opacity", 0.8f);
            go.AddComponent<MeshRenderer>().sharedMaterial = FieldMaterial;
        }

        void BuildDots()
        {
            var go = new GameObject("Detections");
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.004f);   // closest layer: the real data

            _dotMesh = new Mesh { name = "QuantumDots" };
            _dotMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _dotMesh;

            // Dots are a detection event, not a wave — a plain warm mark, deliberately NOT
            // coloured by phase (a single click has no phase to show). The live field overlay is
            // where phase colour belongs; see QuantumField.shader.
            DotMaterial = PrismMaterials.New(PrismMaterials.Mote);
            DotMaterial.SetColor("_Tint", PrismPalette.Gold);
            DotMaterial.SetColor("_EdgeTint", PrismPalette.Warm);
            DotMaterial.SetFloat("_Density", 1.35f);
            DotMaterial.SetFloat("_Softness", 0.55f);
            go.AddComponent<MeshRenderer>().sharedMaterial = DotMaterial;
        }

        /// <summary>Record one detection at a local (x, y) on the screen plane.</summary>
        public void AddDetection(float x, float y)
        {
            int slot = _dotWriteHead;
            _dotWriteHead = (_dotWriteHead + 1) % MaxDots;
            LiveDotCount = Mathf.Min(LiveDotCount + 1, MaxDots);

            var c = new Vector3(x, y, 0f);
            var right = new Vector3(DotHalfSize, 0f, 0f);
            var up = new Vector3(0f, DotHalfSize, 0f);

            Vector3 v0 = c - right - up, v1 = c + right - up, v2 = c - right + up, v3 = c + right + up;

            int baseIdx = slot * 4;
            if (baseIdx < _dotVerts.Count)
            {
                // Recycling a slot the ring buffer has used before: only the position moves.
                _dotVerts[baseIdx] = v0; _dotVerts[baseIdx + 1] = v1;
                _dotVerts[baseIdx + 2] = v2; _dotVerts[baseIdx + 3] = v3;
            }
            else
            {
                // First time this slot has ever been written: extend the mesh, UVs and its
                // triangles once. Capacity was reserved in the constructor, so this never
                // reallocates beyond MaxDots quads over the life of the world.
                _dotVerts.Add(v0); _dotVerts.Add(v1); _dotVerts.Add(v2); _dotVerts.Add(v3);
                _dotUVs.Add(new Vector2(0, 0)); _dotUVs.Add(new Vector2(1, 0));
                _dotUVs.Add(new Vector2(0, 1)); _dotUVs.Add(new Vector2(1, 1));
                // Same winding as QuantumMesh's quads: right x up faces +Z, so a -Z-facing quad
                // (toward the learner) needs the mirrored index order. Prism/Mote is Cull Off
                // regardless, so this only matters for tidiness, not visibility.
                _dotTris.Add(baseIdx); _dotTris.Add(baseIdx + 1); _dotTris.Add(baseIdx + 2);
                _dotTris.Add(baseIdx + 1); _dotTris.Add(baseIdx + 3); _dotTris.Add(baseIdx + 2);
            }
            _dotDirty = true;
        }

        /// <summary>Start a fresh plate: the record of a PREVIOUS configuration should not bleed into a new one.</summary>
        public void Clear()
        {
            _dotWriteHead = 0;
            LiveDotCount = 0;
            for (int i = 0; i < _dotVerts.Count; i++) _dotVerts[i] = Vector3.zero;
            _dotDirty = true;
        }

        /// <summary>Push accumulated dot changes to the GPU. Cheap to call every frame; only touches the mesh when dirty.</summary>
        public void Apply()
        {
            if (!_dotDirty) return;
            _dotDirty = false;
            _dotMesh.SetVertices(_dotVerts);
            _dotMesh.SetUVs(0, _dotUVs);
            _dotMesh.SetTriangles(_dotTris, 0);
            _dotMesh.RecalculateBounds();
        }

        /// <summary>
        /// Push the CURRENT amplitude sum to the field shader — the same open sub-sources,
        /// wavelength and peak the CPU sampler uses, so the live picture and the detections it
        /// is predicting are never allowed to disagree.
        /// </summary>
        public void SyncField(TwoSlitSim sim)
        {
            int n = sim.FillActiveSources(_sourceBuffer);
            FieldMaterial.SetVectorArray("_Sources", _sourceBuffer);
            FieldMaterial.SetInt("_SourceCount", n);
            FieldMaterial.SetFloat("_K", (2f * Mathf.PI) / Mathf.Max(sim.Wavelength, 1e-5f));
            FieldMaterial.SetFloat("_IntensityRef", sim.MaxIntensity());
        }

        public void SetFieldReveal(float t) => FieldMaterial.SetFloat("_Reveal", Mathf.Clamp01(t));
    }
}
