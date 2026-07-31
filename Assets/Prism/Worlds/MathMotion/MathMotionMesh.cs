using System.Collections.Generic;
using UnityEngine;

namespace Prism.Worlds.MathMotion
{
    /// <summary>
    /// Builds a round tube or a flat filled strip along a polyline, every vertex carrying its own
    /// colour.
    ///
    /// This exists because none of PRISM's shared mesh helpers carry a per-vertex signal:
    /// PrismMesh.Tube writes only position and UV, and PrismTrailRibbon's colour comes from a
    /// single scalar baked into a ramp built for a decaying trail, not a persistent curve. Colour
    /// here is decided once in C# (see MathMotionWorld.ColourForSign, the ONE place that decides
    /// colour) and carried as a genuine per-vertex Color, so the shader (Prism/MathMotionCurve)
    /// does no colour maths at all - it only composes whatever colour the simulation already
    /// chose.
    ///
    /// One instance exists per curve drawn (function curve, derivative curve, tangent, trace,
    /// sweep fill) and Tick() rebuilds it every frame the curve can change, so the internal lists
    /// are reused buffers, never re-allocated after the first call - the same pattern
    /// PrismTrailRibbon uses for the same reason.
    /// </summary>
    public class ColouredStripMesh
    {
        readonly List<Vector3> _verts = new List<Vector3>(1024);
        readonly List<Vector2> _uvs   = new List<Vector2>(1024);
        readonly List<Color>   _cols  = new List<Color>(1024);
        readonly List<int>     _tris  = new List<int>(2048);

        public readonly Mesh Mesh;

        public ColouredStripMesh(string name)
        {
            Mesh = new Mesh { name = name };
            Mesh.MarkDynamic();
        }

        /// <summary>
        /// A round tube of `sides` facets following the first `count` points of `path`, each ring
        /// tinted by the matching entry of `colours`. Mirrors PrismMesh.Tube's frame-propagation
        /// algorithm (a running "up" vector carried along the curve so the tube does not twist)
        /// with a per-ring colour added.
        /// </summary>
        public void BuildTube(Vector3[] path, Color[] colours, int count, float radius, int sides)
        {
            _verts.Clear(); _uvs.Clear(); _cols.Clear(); _tris.Clear();
            Mesh.Clear();
            if (path == null || count < 2) return;
            count = Mathf.Min(count, path.Length);

            sides = Mathf.Max(3, sides);
            Vector3 prevUp = Vector3.up;

            for (int i = 0; i < count; i++)
            {
                Vector3 fwd = (i == 0) ? (path[1] - path[0])
                            : (i == count - 1) ? (path[i] - path[i - 1])
                            : (path[i + 1] - path[i - 1]);
                if (fwd.sqrMagnitude < 1e-10f) fwd = Vector3.forward;
                fwd.Normalize();

                Vector3 right = Vector3.Cross(prevUp, fwd);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.right, fwd);
                right.Normalize();
                Vector3 up = Vector3.Cross(fwd, right).normalized;
                prevUp = up;

                float u = (count > 1) ? (float)i / (count - 1) : 0f;
                Color c = (colours != null && i < colours.Length) ? colours[i] : Color.white;

                for (int s = 0; s <= sides; s++)
                {
                    float a = (float)s / sides * Mathf.PI * 2f;
                    Vector3 n = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    _verts.Add(path[i] + n * radius);
                    _uvs.Add(new Vector2(u, (float)s / sides));
                    _cols.Add(c);
                }
            }

            int stride = sides + 1;
            for (int i = 0; i < count - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int i0 = i * stride + s, i1 = i0 + 1;
                    int i2 = i0 + stride,    i3 = i2 + 1;
                    _tris.Add(i0); _tris.Add(i2); _tris.Add(i1);
                    _tris.Add(i1); _tris.Add(i2); _tris.Add(i3);
                }

            Commit();
        }

        /// <summary>
        /// A flat filled sheet between the first `count` points of `top` and a fixed baseline
        /// height, for shading the area between a curve and its axis. Degenerate (zero height)
        /// exactly where top[i].y equals baselineY, which is exactly right at a sweep frontier
        /// that has not reached that x yet.
        /// </summary>
        public void BuildAreaStrip(Vector3[] top, Color[] colours, int count, float baselineY)
        {
            _verts.Clear(); _uvs.Clear(); _cols.Clear(); _tris.Clear();
            Mesh.Clear();
            if (top == null || count < 2) return;
            count = Mathf.Min(count, top.Length);

            for (int i = 0; i < count; i++)
            {
                Color c = (colours != null && i < colours.Length) ? colours[i] : Color.white;
                Vector3 hi = top[i];
                Vector3 lo = new Vector3(top[i].x, baselineY, top[i].z);
                float u = (count > 1) ? (float)i / (count - 1) : 0f;
                _verts.Add(hi); _uvs.Add(new Vector2(u, 1f)); _cols.Add(c);
                _verts.Add(lo); _uvs.Add(new Vector2(u, 0f)); _cols.Add(c);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int i0 = i * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                _tris.Add(i0); _tris.Add(i2); _tris.Add(i1);
                _tris.Add(i1); _tris.Add(i2); _tris.Add(i3);
            }

            Commit();
        }

        void Commit()
        {
            if (_verts.Count > 65000) Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            Mesh.SetVertices(_verts);
            Mesh.SetUVs(0, _uvs);
            Mesh.SetColors(_cols);
            Mesh.SetTriangles(_tris, 0);
            Mesh.RecalculateBounds();
        }
    }
}
