using System.Collections.Generic;
using UnityEngine;

namespace Prism.Worlds.QuantumGarden
{
    /// <summary>
    /// The two primitives PrismMesh does not provide: a box and a flat quad. Both are unit
    /// sized and cached, exactly like PrismMesh.Icosphere — every user scales one via
    /// transform.localScale rather than baking a new mesh per instance. Kept local to this
    /// world folder rather than added to the shared PrismMesh, per the module contract.
    /// </summary>
    public static class QuantumMesh
    {
        static Mesh _cube;
        static Mesh _quad;

        /// <summary>
        /// Unit cube centred on the origin, hard-edged face normals, wound for Cull Back
        /// (PrismCeramic is an opaque, back-culled shader, so getting this backwards makes the
        /// barrier invisible rather than merely unlit).
        /// </summary>
        public static Mesh UnitCube()
        {
            if (_cube != null) return _cube;

            var v = new List<Vector3>(24);
            var n = new List<Vector3>(24);
            var t = new List<int>(36);

            // Each face given as (centre, right, up) with right x up pointing OUTWARD along the
            // face's own normal — verified by hand against PrismMesh.Disc's known-good winding,
            // whose pattern this reuses exactly (see AddFace).
            AddFace(v, n, t, new Vector3(0.5f, 0, 0), new Vector3(0, 0.5f, 0), new Vector3(0, 0, 0.5f));   // +X
            AddFace(v, n, t, new Vector3(-0.5f, 0, 0), new Vector3(0, 0, 0.5f), new Vector3(0, 0.5f, 0));  // -X
            AddFace(v, n, t, new Vector3(0, 0.5f, 0), new Vector3(0, 0, 0.5f), new Vector3(0.5f, 0, 0));   // +Y
            AddFace(v, n, t, new Vector3(0, -0.5f, 0), new Vector3(0.5f, 0, 0), new Vector3(0, 0, 0.5f));  // -Y
            AddFace(v, n, t, new Vector3(0, 0, 0.5f), new Vector3(0.5f, 0, 0), new Vector3(0, 0.5f, 0));   // +Z
            AddFace(v, n, t, new Vector3(0, 0, -0.5f), new Vector3(0, 0.5f, 0), new Vector3(0.5f, 0, 0));  // -Z

            var mesh = new Mesh { name = "QuantumUnitCube" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            _cube = mesh;
            return _cube;
        }

        /// <summary>
        /// One rectangular face: four verts at centre +/- right +/- up, normal = right x up.
        /// The (i0,i2,i1)(i1,i2,i3) triangulation is the same one PrismMesh.Disc uses for a
        /// parallelogram patch, reused here rather than re-derived per caller.
        /// </summary>
        static void AddFace(List<Vector3> v, List<Vector3> n, List<int> t,
                            Vector3 centre, Vector3 right, Vector3 up)
        {
            Vector3 normal = Vector3.Cross(right, up).normalized;
            int b = v.Count;
            v.Add(centre - right - up);
            v.Add(centre + right - up);
            v.Add(centre - right + up);
            v.Add(centre + right + up);
            n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal);
            t.Add(b); t.Add(b + 2); t.Add(b + 1);
            t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
        }

        /// <summary>
        /// Unit quad in the XY plane centred on the origin, facing -Z (toward the learner, who
        /// stands on the -Z side of everything built in this world). UV 0..1 with (0,0) at the
        /// bottom-left, matching the convention PrismTrailRibbon and PrismCompanion both use.
        /// </summary>
        public static Mesh UnitQuad()
        {
            if (_quad != null) return _quad;

            var mesh = new Mesh { name = "QuantumUnitQuad" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            });
            mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)
            });
            // right(+X) x up(+Y) = +Z, the opposite of this quad's own -Z normal, so the winding
            // is the mirror of AddFace's pattern: (i0,i1,i2)(i1,i3,i2) rather than (i0,i2,i1)(i1,i2,i3).
            mesh.SetTriangles(new[] { 0, 1, 2, 1, 3, 2 }, 0);
            mesh.RecalculateBounds();
            _quad = mesh;
            return _quad;
        }

        /// <summary>
        /// A flat rectangle in the XY plane, centred on the origin, facing -Z, sized directly in
        /// METRES rather than unit space. Callers whose shaders read object-space position as a
        /// physical coordinate (Prism/QuantumField does) need this instead of UnitQuad, because a
        /// scaled transform would rescale the physics along with the geometry. Not cached: built
        /// only a handful of times over a session, at whatever size the caller needs.
        /// </summary>
        public static Mesh Rect(float width, float height)
        {
            float hx = width * 0.5f, hy = height * 0.5f;
            var mesh = new Mesh { name = "QuantumRect" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-hx, -hy, 0f), new Vector3(hx, -hy, 0f),
                new Vector3(-hx, hy, 0f), new Vector3(hx, hy, 0f),
            });
            mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 1, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
