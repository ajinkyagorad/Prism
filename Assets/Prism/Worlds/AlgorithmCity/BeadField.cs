using System.Collections.Generic;
using Prism.Aesthetic;
using UnityEngine;

namespace Prism.Worlds.AlgorithmCity
{
    /// <summary>
    /// A swarm of small solid "data bead" bodies, rendered as ONE dynamic mesh.
    ///
    /// The performance budget rules out one GameObject per datum — up to a few dozen data elements
    /// across two structures would blow well past the draw-call budget on its own. Every bead is
    /// instead a copy of a unit icosphere baked into a single shared mesh and rebuilt only while
    /// visible, exactly like <c>PrismTrailRibbon</c> and the companion's motes: reusable lists,
    /// cleared and refilled, never reallocated.
    ///
    /// COLOUR LAW (stated once here, true everywhere it is used): a bead's hue is its VALUE, mapped
    /// onto the spectral ramp by <c>Prism/AlgorithmCityBead</c> from <see cref="Bead.Value01"/>
    /// baked into UV1.x. Nothing about which structure a bead belongs to changes its colour — value
    /// is value, wherever it is standing. A sorted run therefore reads as a smooth colour gradient
    /// with no label anywhere, and a mixed one reads as visible noise.
    /// </summary>
    public class BeadField
    {
        public struct Bead
        {
            public Vector3 Position;   // local space, current (smoothed) visual position
            public Vector3 Target;     // local space, where it is travelling to
            public float Value01;      // colour: this datum's value, mapped to the spectral ramp
            public float Glow;         // 0..1, decays; a just-touched bead brightens and swells
            public float Scale;        // metres, base radius
            public bool Active;
        }

        public readonly Bead[] Beads;
        public int Count;
        public Transform View { get; }

        readonly Mesh _mesh;
        readonly Vector3[] _tplVerts;
        readonly Vector3[] _tplNorms;
        readonly int[] _tplTris;

        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _norms = new List<Vector3>();
        readonly List<Vector2> _uv1 = new List<Vector2>();
        readonly List<int> _tris = new List<int>();

        public BeadField(string name, Transform parent, Material material, int maxCount)
        {
            Beads = new Bead[Mathf.Max(1, maxCount)];

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            View = go.transform;

            _mesh = new Mesh { name = name + "Mesh" };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            var tpl = PrismMesh.Icosphere(1);
            _tplVerts = tpl.vertices;
            _tplNorms = tpl.normals;
            _tplTris = tpl.triangles;
        }

        /// <summary>Bring a bead to life at a given slot, arriving visually from a shared spawn
        /// point rather than popping into existence at its slot.</summary>
        public void Spawn(int index, Vector3 fromLocal, Vector3 targetLocal, float value01, float scale)
        {
            Beads[index] = new Bead
            {
                Position = fromLocal,
                Target = targetLocal,
                Value01 = value01,
                Glow = 1f,
                Scale = scale,
                Active = true
            };
        }

        public void Deactivate(int index)
        {
            if (index < 0 || index >= Beads.Length) return;
            var b = Beads[index];
            b.Active = false;
            Beads[index] = b;
        }

        public void DeactivateAll()
        {
            for (int i = 0; i < Beads.Length; i++) Deactivate(i);
        }

        /// <summary>Advance every active bead toward its target and let its glow decay. No
        /// allocation — safe to call every frame.</summary>
        public void Tick(float dt, float followPerSecond)
        {
            float f = 1f - Mathf.Exp(-followPerSecond * dt);
            for (int i = 0; i < Beads.Length; i++)
            {
                var b = Beads[i];
                if (!b.Active) continue;
                b.Position = Vector3.Lerp(b.Position, b.Target, f);
                b.Glow = Mathf.MoveTowards(b.Glow, 0f, dt / 0.35f);
                Beads[i] = b;
            }
        }

        /// <summary>Rebuild the batched mesh from current bead state. Call once per frame while
        /// <see cref="View"/> is active; skip it entirely while hidden.</summary>
        public void Rebuild()
        {
            _verts.Clear(); _norms.Clear(); _uv1.Clear(); _tris.Clear();

            for (int i = 0; i < Beads.Length; i++)
            {
                var b = Beads[i];
                if (!b.Active) continue;

                float scale = b.Scale * (1f + b.Glow * 0.4f);
                int baseIdx = _verts.Count;

                for (int v = 0; v < _tplVerts.Length; v++)
                {
                    _verts.Add(b.Position + _tplVerts[v] * scale);
                    _norms.Add(_tplNorms[v]);
                    _uv1.Add(new Vector2(b.Value01, b.Glow));
                }
                for (int t = 0; t < _tplTris.Length; t++)
                    _tris.Add(baseIdx + _tplTris[t]);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_norms);
            _mesh.SetUVs(1, _uv1);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }

        /// <summary>The nearest active bead to a point in this field's local space, within
        /// maxDistance, or -1. Used for both grabbing and drop-to-swap targeting.</summary>
        public int NearestActive(Vector3 localPoint, float maxDistance, int exclude = -1)
        {
            int best = -1;
            float bestD = maxDistance * maxDistance;
            for (int i = 0; i < Beads.Length; i++)
            {
                if (i == exclude || !Beads[i].Active) continue;
                float d = (Beads[i].Position - localPoint).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }
}
