using System.Collections.Generic;
using UnityEngine;

namespace Prism.Aesthetic
{
    /// <summary>
    /// Procedural geometry for the PRISM material family. Everything is generated: there are no
    /// imported models in the core app, which keeps the download small and — more importantly —
    /// means a concept's body can be derived from the concept's own data rather than authored
    /// by hand for each one.
    ///
    /// Smooth normals throughout. Prism/Seed produces its facets by quantising the normal in the
    /// shader, so handing it a hard-edged mesh would fight the effect rather than help it.
    /// </summary>
    public static class PrismMesh
    {
        static readonly Dictionary<int, Mesh> _icoCache = new Dictionary<int, Mesh>();

        /// <summary>
        /// Unit icosphere. Subdivision 2 (320 tris) is the default for constellation seeds;
        /// 3 (1280 tris) for anything the learner will hold close to their face.
        /// </summary>
        public static Mesh Icosphere(int subdivisions = 2)
        {
            subdivisions = Mathf.Clamp(subdivisions, 0, 4);
            if (_icoCache.TryGetValue(subdivisions, out var cached) && cached != null) return cached;

            const float t = 1.618034f;   // golden ratio
            var verts = new List<Vector3>
            {
                new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
                new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
                new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;

            var tris = new List<int>
            {
                0,11,5,  0,5,1,   0,1,7,   0,7,10,  0,10,11,
                1,5,9,   5,11,4,  11,10,2, 10,7,6,  7,1,8,
                3,9,4,   3,4,2,   3,2,6,   3,6,8,   3,8,9,
                4,9,5,   2,4,11,  6,2,10,  8,6,7,   9,8,1
            };

            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<int>(tris.Count * 4);
                var midCache = new Dictionary<long, int>();

                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (midCache.TryGetValue(key, out var m)) return m;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    m = verts.Count - 1;
                    midCache[key] = m;
                    return m;
                }

                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca,  b, bc, ab,  c, ca, bc,  ab, bc, ca });
                }
                tris = next;
            }

            var mesh = new Mesh { name = $"PrismIcosphere{subdivisions}" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            // On a unit sphere the position IS the normal, which is exactly the smooth normal
            // the crystallisation shader wants.
            mesh.SetNormals(verts.ConvertAll(v => v));
            mesh.RecalculateBounds();

            _icoCache[subdivisions] = mesh;
            return mesh;
        }

        /// <summary>
        /// A flat disc in the XZ plane, radius 1, double sided by the shader rather than by
        /// duplicated geometry. Used for the gravitational field's contour plane.
        /// </summary>
        public static Mesh Disc(int segments = 96, int rings = 24)
        {
            segments = Mathf.Max(8, segments);
            rings    = Mathf.Max(1, rings);

            var verts = new List<Vector3>((segments + 1) * (rings + 1));
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();

            for (int ring = 0; ring <= rings; ring++)
            {
                // Rings bunch toward the centre, where the field contours are densest and the
                // fragment-side derivative needs the most geometric help.
                float f = (float)ring / rings;
                float r = Mathf.Pow(f, 1.6f);
                for (int s = 0; s <= segments; s++)
                {
                    float a = (float)s / segments * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                    uvs.Add(new Vector2((float)s / segments, f));
                }
            }

            int stride = segments + 1;
            for (int ring = 0; ring < rings; ring++)
                for (int s = 0; s < segments; s++)
                {
                    int i0 = ring * stride + s, i1 = i0 + 1;
                    int i2 = i0 + stride,       i3 = i2 + 1;
                    tris.AddRange(new[] { i0, i2, i1,  i1, i2, i3 });
                }

            var mesh = new Mesh { name = "PrismDisc" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.SetNormals(verts.ConvertAll(_ => Vector3.up));
            mesh.RecalculateBounds();
            return mesh;
        }

        // Scratch buffers for Tube.
        //
        // Tube is the hottest allocating call in the project: every demonstration curve, every
        // predicted trajectory and all 23 constellation links rebuild through it EVERY FRAME.
        // Allocating four Lists per call came to roughly 115 allocations a frame — about 8,300 a
        // second at 72 Hz — which is precisely the steady GC churn that surfaces as periodic
        // hitching rather than as a low frame rate. Reusing them costs nothing: Tube is synchronous
        // and single-threaded, so the buffers can never be in use by two callers at once.
        static readonly List<Vector3> _tubeVerts = new List<Vector3>(1024);
        static readonly List<Vector3> _tubeNorms = new List<Vector3>(1024);
        static readonly List<Vector2> _tubeUvs   = new List<Vector2>(1024);
        static readonly List<int>     _tubeTris  = new List<int>(4096);

        /// <summary>
        /// A tube following a polyline. u runs 0..1 along the tube and v around it, which is the
        /// UV convention Prism/Flow expects.
        /// </summary>
        public static Mesh Tube(IList<Vector3> path, float radius, int sides = 8, Mesh reuse = null)
        {
            var mesh = reuse ?? new Mesh { name = "PrismTube" };
            mesh.Clear();
            if (path == null || path.Count < 2) return mesh;

            sides = Mathf.Max(3, sides);
            var verts = _tubeVerts; verts.Clear();
            var norms = _tubeNorms; norms.Clear();
            var uvs   = _tubeUvs;   uvs.Clear();
            var tris  = _tubeTris;  tris.Clear();

            // Carry a reference frame along the curve rather than recomputing an arbitrary
            // perpendicular per segment; otherwise the tube twists visibly where the path bends.
            Vector3 prevUp = Vector3.up;

            for (int i = 0; i < path.Count; i++)
            {
                Vector3 fwd = (i == 0) ? (path[1] - path[0])
                            : (i == path.Count - 1) ? (path[i] - path[i - 1])
                            : (path[i + 1] - path[i - 1]);
                if (fwd.sqrMagnitude < 1e-10f) fwd = Vector3.forward;
                fwd.Normalize();

                Vector3 right = Vector3.Cross(prevUp, fwd);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.right, fwd);
                right.Normalize();
                Vector3 up = Vector3.Cross(fwd, right).normalized;
                prevUp = up;

                float u = (float)i / (path.Count - 1);
                for (int s = 0; s <= sides; s++)
                {
                    float a = (float)s / sides * Mathf.PI * 2f;
                    Vector3 n = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    verts.Add(path[i] + n * radius);
                    norms.Add(n);
                    uvs.Add(new Vector2(u, (float)s / sides));
                }
            }

            int stride = sides + 1;
            for (int i = 0; i < path.Count - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int i0 = i * stride + s, i1 = i0 + 1;
                    int i2 = i0 + stride,    i3 = i2 + 1;
                    tris.AddRange(new[] { i0, i2, i1,  i1, i2, i3 });
                }

            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// An arrow along +Z of unit length: shaft plus a cone head. The head is where the
        /// learner grabs a vector to change it, so it is generously sized relative to the shaft.
        /// </summary>
        public static Mesh Arrow(float shaftRadius = 0.004f, float headRadius = 0.012f,
                                 float headLength = 0.03f, int sides = 12)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris  = new List<int>();

            float shaftLen = Mathf.Max(0.001f, 1f - headLength);

            // Shaft
            for (int s = 0; s <= sides; s++)
            {
                float a = (float)s / sides * Mathf.PI * 2f;
                var n = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                verts.Add(n * shaftRadius);                       norms.Add(n);
                verts.Add(n * shaftRadius + Vector3.forward * shaftLen); norms.Add(n);
            }
            for (int s = 0; s < sides; s++)
            {
                int i0 = s * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                tris.AddRange(new[] { i0, i2, i1,  i1, i2, i3 });
            }

            // Head
            int baseIdx = verts.Count;
            var tip = Vector3.forward;
            for (int s = 0; s <= sides; s++)
            {
                float a = (float)s / sides * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                var rim = radial * headRadius + Vector3.forward * shaftLen;
                var n = (radial * headLength + Vector3.forward * headRadius).normalized;
                verts.Add(rim); norms.Add(n);
                verts.Add(tip); norms.Add(n);
            }
            for (int s = 0; s < sides; s++)
            {
                int i0 = baseIdx + s * 2, i1 = i0 + 1, i2 = i0 + 2;
                tris.AddRange(new[] { i0, i2, i1 });
            }

            var mesh = new Mesh { name = "PrismArrow" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
