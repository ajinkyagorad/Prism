using System.Collections.Generic;
using Prism.Core;
using UnityEngine;

namespace Prism.Worlds.EvolutionEngine
{
    /// <summary>
    /// Renders the entire living population as ONE dynamic mesh of camera-facing quads — the same
    /// technique <c>PrismCompanion</c> uses for its motes and <c>PrismTrailRibbon</c> uses for a
    /// path, applied here to a whole population instead of a single point or a single history.
    /// Chosen over one GameObject per organism specifically because of the performance rule in the
    /// world contract: "camera-facing quads in a single mesh beat 30 GameObjects", and this world
    /// can have up to <see cref="EvolutionSim.Capacity"/> of them alive at once.
    ///
    /// COLOUR ENCODING (stated once, here, because this is where it is implemented): each
    /// organism's quad is tinted by <c>PrismPalette.Spectral(trait01)</c>, baked into the mesh as
    /// a per-vertex colour and read straight through by Prism/EvolutionEngineOrganism. Hue IS
    /// trait. A learner never needs a legend for this — the population's colour distribution,
    /// glanced at, already IS the histogram that Discover later draws as bars; the bars only add a
    /// count axis to something the eye has been looking at since Wonder. Size is a second,
    /// redundant encoding of the very same number, not a second meaning: because the trait this
    /// world uses is literally body size, drawing a large-trait organism as a physically larger
    /// quad is not decoration, it is the trait's own meaning made visible.
    ///
    /// Vertex alpha carries each organism's fade level (newborns fade in, the dying fade out over
    /// <c>EvolutionSim.BirthFadeSeconds</c> / <c>DeathFadeSeconds</c>), so nothing in this
    /// population ever appears or disappears with a pop — consistent with "nothing in PRISM has a
    /// sharp attack; nothing alerts anybody."
    /// </summary>
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class EvolutionOrganismField : MonoBehaviour
    {
        public float BaseRadius = 0.014f;
        public float MinSizeMul = 0.55f;
        public float MaxSizeMul = 1.55f;

        [Tooltip("Constant lift above the terrain floor, metres, plus a lazy per-individual bob.")]
        public float HoverHeight = 0.012f;

        Mesh _mesh;
        Camera _camera;

        readonly List<Vector3> _verts  = new List<Vector3>();
        readonly List<Vector2> _uvs    = new List<Vector2>();
        readonly List<Color>   _colors = new List<Color>();
        readonly List<int>     _tris   = new List<int>();

        void Awake()
        {
            _mesh = new Mesh { name = "EvolutionOrganisms" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        /// <summary>
        /// Which camera the quads should face. Camera.main is frequently not the drawing camera
        /// under XR Simulation — see OrbitalWorld.FindHeadCamera — so the world passes its own
        /// head camera in explicitly rather than letting this guess.
        /// </summary>
        public void SetCamera(Camera cam) => _camera = cam;

        /// <summary>
        /// Rebuild from the current population. <c>pop</c> entries are expected to be in the same
        /// local space this component's own transform occupies — a direct child of the world
        /// anchor, identity local transform — exactly as EvolutionSim positions them, so no
        /// per-organism world-space conversion is needed here, only for the camera axes.
        /// </summary>
        public void Rebuild(Individual[] pop)
        {
            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null) { _mesh.Clear(); return; }

            Vector3 right = transform.InverseTransformVector(cam.transform.right);
            Vector3 up    = transform.InverseTransformVector(cam.transform.up);

            _verts.Clear(); _uvs.Clear(); _colors.Clear(); _tris.Clear();

            float t = Time.time;
            for (int i = 0; i < pop.Length; i++)
            {
                var ind = pop[i];
                if (!ind.Occupied || ind.FadeT <= 0.002f) continue;

                float u01 = EvolutionSim.TraitToU01(ind.Trait);
                Color col = PrismPalette.Spectral(u01);
                col.a = Mathf.Clamp01(ind.FadeT);

                float size = BaseRadius * Mathf.Lerp(MinSizeMul, MaxSizeMul, u01);
                // A slow, per-individual bob so a resting population still reads as alive. Phase
                // comes from position rather than an index, so it costs nothing to keep stable
                // across births and deaths reshuffling the array.
                float bob = Mathf.Sin(t * 0.9f + ind.Pos.x * 17f + ind.Pos.z * 11f) * 0.003f;
                Vector3 centre = ind.Pos + new Vector3(0f, HoverHeight + bob, 0f);

                Vector3 r = right * size;
                Vector3 u = up * size;

                int b = _verts.Count;
                _verts.Add(centre - r - u); _uvs.Add(new Vector2(0f, 0f)); _colors.Add(col);
                _verts.Add(centre + r - u); _uvs.Add(new Vector2(1f, 0f)); _colors.Add(col);
                _verts.Add(centre - r + u); _uvs.Add(new Vector2(0f, 1f)); _colors.Add(col);
                _verts.Add(centre + r + u); _uvs.Add(new Vector2(1f, 1f)); _colors.Add(col);

                _tris.Add(b);     _tris.Add(b + 2); _tris.Add(b + 1);
                _tris.Add(b + 1); _tris.Add(b + 2); _tris.Add(b + 3);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }
    }
}
