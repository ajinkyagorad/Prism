using System.Collections.Generic;
using UnityEngine;

namespace Prism.Worlds.MachineCathedral
{
    /// <summary>
    /// Procedural geometry for this world only: boxes, cylinders and gears, all built from one
    /// extrusion routine so there is exactly one place winding order can go wrong.
    ///
    /// Every 2D outline here is closed, centred on the origin, and traversed with INCREASING angle
    /// (counter-clockwise, viewed from +Z) — a circle as (cos a, sin a) for a rising a is the
    /// pattern every outline below follows, including the gear and the rectangle. That single
    /// convention is what <see cref="Extrude"/> assumes.
    ///
    /// Gear teeth are flat-topped and flat-flanked (a "castle wall" silhouette: root radius, a
    /// radial step up, a flat tip land, a radial step down), not involute. The contract this world
    /// was built against says involute profiles are unnecessary — what has to be right is the tooth
    /// COUNT and the ROTATION, and both are exact. The radii do follow the real module relationship
    /// (addendum = pitch + 1 module, dedendum = pitch - 1.25 module), so two gears built from the
    /// same module always mesh at the correct centre distance without hand-tuning it per pair.
    /// </summary>
    public static class MachineMesh
    {
        public static List<Vector2> CircleOutline(float radius, int segments)
        {
            segments = Mathf.Max(6, segments);
            var pts = new List<Vector2>(segments);
            for (int i = 0; i < segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                pts.Add(new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius));
            }
            return pts;
        }

        public static List<Vector2> RectOutline(float width, float height)
        {
            float hw = width * 0.5f, hh = height * 0.5f;
            return new List<Vector2>
            {
                new Vector2(-hw, -hh), new Vector2(hw, -hh),
                new Vector2(hw, hh),   new Vector2(-hw, hh)
            };
        }

        /// <summary>
        /// A spur gear's outline, teeth-first: 4 boundary points per tooth (root, step up, tip,
        /// step down), so tooth count in the mesh is tooth count in the mechanics — there is no
        /// path by which the drawn gear and the simulated ratio can disagree.
        /// </summary>
        public static List<Vector2> GearOutline(int teeth, float module, float tipFraction = 0.42f)
        {
            teeth = Mathf.Max(4, teeth);
            float pitchR = module * teeth * 0.5f;
            float addR = pitchR + module;
            float rootR = Mathf.Max(pitchR - 1.25f * module, pitchR * 0.55f);

            float pitch = Mathf.PI * 2f / teeth;
            float tipWidth = pitch * Mathf.Clamp01(tipFraction);
            float gapHalf = (pitch - tipWidth) * 0.5f;

            var pts = new List<Vector2>(teeth * 4);
            for (int i = 0; i < teeth; i++)
            {
                float a0 = i * pitch + gapHalf;
                float a1 = a0 + tipWidth;
                pts.Add(new Vector2(Mathf.Cos(a0) * rootR, Mathf.Sin(a0) * rootR));
                pts.Add(new Vector2(Mathf.Cos(a0) * addR, Mathf.Sin(a0) * addR));
                pts.Add(new Vector2(Mathf.Cos(a1) * addR, Mathf.Sin(a1) * addR));
                pts.Add(new Vector2(Mathf.Cos(a1) * rootR, Mathf.Sin(a1) * rootR));
            }
            return pts;
        }

        /// <summary>Pitch radius for a gear of this tooth count and module — the distance from the
        /// gear's own axis at which it truly meshes. Two meshing gears sit centre-to-centre at the
        /// SUM of their pitch radii; the gear station uses this to place a swapped gear correctly
        /// with no per-gear tuning.</summary>
        public static float PitchRadius(int teeth, float module) => module * teeth * 0.5f;

        /// <summary>
        /// Extrude a closed, origin-centred, CCW 2D outline into a solid prism of thickness
        /// 2*halfThickness. Flat-shaded throughout (each face gets its own vertices and its own
        /// normal) — correct for a mechanical part and cheap for a low-vertex-count gear or beam.
        /// Winding was verified by hand against Unity's clockwise-front convention, not assumed.
        /// </summary>
        public static Mesh Extrude(IList<Vector2> outline, float halfThickness, Mesh reuse = null)
        {
            var mesh = reuse ?? new Mesh { name = "MachineExtrude" };
            mesh.Clear();
            int n = outline.Count;
            if (n < 3) return mesh;

            var verts = new List<Vector3>(n * 4 + 2);
            var norms = new List<Vector3>(n * 4 + 2);
            var tris = new List<int>(n * 12);

            // ---- side walls: one flat quad per edge, duplicated verts for hard edges ----
            for (int i = 0; i < n; i++)
            {
                Vector2 a = outline[i];
                Vector2 b = outline[(i + 1) % n];
                Vector2 edge = (b - a);
                if (edge.sqrMagnitude < 1e-12f) continue;
                Vector2 n2 = new Vector2(edge.y, -edge.x).normalized;   // outward for a CCW outline
                Vector3 nrm = new Vector3(n2.x, n2.y, 0f);

                int b0 = verts.Count;
                verts.Add(new Vector3(a.x, a.y, -halfThickness)); norms.Add(nrm);  // botA
                verts.Add(new Vector3(a.x, a.y, halfThickness)); norms.Add(nrm);  // topA
                verts.Add(new Vector3(b.x, b.y, -halfThickness)); norms.Add(nrm);  // botB
                verts.Add(new Vector3(b.x, b.y, halfThickness)); norms.Add(nrm);  // topB

                // (botA, topA, botB) and (botB, topA, topB) — verified outward-facing.
                tris.Add(b0); tris.Add(b0 + 1); tris.Add(b0 + 2);
                tris.Add(b0 + 2); tris.Add(b0 + 1); tris.Add(b0 + 3);
            }

            // ---- top cap (z = +half, normal +Z): fan uses (centre, B, A) ----
            int topCentre = verts.Count;
            verts.Add(new Vector3(0f, 0f, halfThickness)); norms.Add(Vector3.forward);
            int topStart = verts.Count;
            for (int i = 0; i < n; i++)
            {
                verts.Add(new Vector3(outline[i].x, outline[i].y, halfThickness));
                norms.Add(Vector3.forward);
            }
            for (int i = 0; i < n; i++)
            {
                int a = topStart + i, b = topStart + (i + 1) % n;
                tris.Add(topCentre); tris.Add(b); tris.Add(a);
            }

            // ---- bottom cap (z = -half, normal -Z): fan uses (centre, A, B) ----
            int botCentre = verts.Count;
            verts.Add(new Vector3(0f, 0f, -halfThickness)); norms.Add(Vector3.back);
            int botStart = verts.Count;
            for (int i = 0; i < n; i++)
            {
                verts.Add(new Vector3(outline[i].x, outline[i].y, -halfThickness));
                norms.Add(Vector3.back);
            }
            for (int i = 0; i < n; i++)
            {
                int a = botStart + i, b = botStart + (i + 1) % n;
                tris.Add(botCentre); tris.Add(a); tris.Add(b);
            }

            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Box(Vector3 size, Mesh reuse = null) =>
            Extrude(RectOutline(size.x, size.y), size.z * 0.5f, reuse);

        public static Mesh Cylinder(float radius, float height, int segments = 20, Mesh reuse = null) =>
            Extrude(CircleOutline(radius, segments), height * 0.5f, reuse);

        public static Mesh Gear(int teeth, float module, float thickness, Mesh reuse = null) =>
            Extrude(GearOutline(teeth, module), thickness * 0.5f, reuse);
    }
}
