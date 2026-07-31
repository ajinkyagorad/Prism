using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Demos
{
    /// <summary>
    /// CONIC SECTIONS — tilt the plane, and the curve changes its name.
    ///
    /// This is the demonstration that ties the mathematics to the orbital world. The learner tilts
    /// a slicing plane through a double cone with the free hand and the intersection is computed
    /// live. As the plane passes the cone's own half-angle, the closed curve opens: ellipse becomes
    /// parabola becomes hyperbola. It is the same transition a moon makes when it is thrown past
    /// escape speed, and a learner who has done both should feel the connection before it is named.
    /// </summary>
    public class ConicDemo : ConceptDemo
    {
        CurveView _curve;
        Transform _coneUpper, _coneLower, _plane;
        Material _planeMat;
        float _tilt = 0.35f;
        readonly List<Vector3> _pts = new List<Vector3>(128);
        const float HalfAngle = 0.62f;            // the cone's half-angle, radians

        public override void Build()
        {
            var coneMat = PrismMaterials.New(PrismMaterials.Gel);
            coneMat.SetColor("_Tint", Tint);
            coneMat.SetColor("_DeepTint", PrismPalette.Violet);
            coneMat.SetFloat("_Density", 0.5f);

            _coneUpper = Body(Cone(24), 1f, coneMat, "coneUpper");
            _coneLower = Body(Cone(24), 1f, coneMat, "coneLower");
            _coneLower.localRotation = Quaternion.Euler(180f, 0f, 0f);

            _planeMat = PrismMaterials.New(PrismMaterials.Gel);
            _planeMat.SetColor("_Tint", PrismPalette.Warm);
            _planeMat.SetFloat("_Density", 0.35f);
            _plane = Body(PrismMesh.Disc(48, 6), 0.95f, _planeMat, "plane");

            _curve = Curve(PrismPalette.Gold, 0.011f, 2f);
        }

        /// <summary>A cone opening upward from the origin, height 1, unit-ish radius.</summary>
        static Mesh Cone(int sides)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            float r = Mathf.Tan(HalfAngle);
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(Vector3.zero); norms.Add(radial);
                verts.Add(radial * r + Vector3.up); norms.Add(radial);
            }
            for (int i = 0; i < sides; i++)
            {
                int i0 = i * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                tris.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
            }
            var m = new Mesh { name = "cone" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _tilt = Mathf.Lerp(_tilt, Mathf.Clamp(Mathf.InverseLerp(-0.7f, 0.7f, hand.y) * 1.45f, 0f, 1.45f), dt * 6f);

            _plane.localRotation = Quaternion.Euler(_tilt * Mathf.Rad2Deg, 0f, 0f);
            _plane.localPosition = new Vector3(0f, 0.45f, 0f);

            // Intersect the tilted plane with the cone, sampled by angle about the cone axis.
            // The plane through point P with normal n: n.(x - P) = 0. A cone ray at azimuth a and
            // height t is  t * (tan(H) cos a, 1, tan(H) sin a).
            Vector3 n = _plane.localRotation * Vector3.up;
            Vector3 P = _plane.localPosition;
            float tanH = Mathf.Tan(HalfAngle);
            float denomPlane = Vector3.Dot(n, P);

            _pts.Clear();
            for (int i = 0; i <= 160; i++)
            {
                float a = i / 160f * Mathf.PI * 2f;
                var dir = new Vector3(tanH * Mathf.Cos(a), 1f, tanH * Mathf.Sin(a));
                float d = Vector3.Dot(n, dir);
                if (Mathf.Abs(d) < 1e-4f) continue;   // ray parallel to the plane: no intersection
                float t = denomPlane / d;
                if (t < 0f || t > 1.6f) continue;     // behind the apex, or off the drawn cone
                _pts.Add(dir * t);
            }

            if (_pts.Count > 2) _curve.Set(_pts);
            else _curve.Mesh.Clear();

            // Gold while the section closes, violet once it has opened — matching the orbital world,
            // where the same colours mean bound and unbound.
            bool closed = _tilt < HalfAngle;
            _curve.Mat.SetColor("_Tint", closed ? PrismPalette.Gold : PrismPalette.Violet);
        }
    }

    /// <summary>
    /// VECTORS — tip to tail, with the sum falling out.
    ///
    /// Grab either arrow by its head and the resultant redraws. The point is not that addition is
    /// hard; it is that a vector is a thing you can take hold of, which is the intuition every
    /// later physics world depends on. The velocity arrows in the orbital world are the same verb.
    /// </summary>
    public class VectorsDemo : ConceptDemo
    {
        ArrowView _a, _b, _sum;
        CurveView _ghost;
        readonly List<Vector3> _outline = new List<Vector3>(5);
        Vector3 _va = new Vector3(0.6f, 0.15f, 0f);
        Vector3 _vb = new Vector3(0.1f, 0.55f, 0f);
        int _grabbed = -1;

        public override void Build()
        {
            _a = MakeArrow(PrismPalette.Cyan);
            _b = MakeArrow(PrismPalette.Mint);
            _sum = MakeArrow(PrismPalette.Gold, 1.25f);
            _ghost = Curve(PrismPalette.Warm, 0.004f);
        }

        protected override void OnTick(float dt)
        {
            bool hand = TryFreeLocal(out var h);
            if (hand && FreePinch > 0.5f)
            {
                if (_grabbed < 0)
                {
                    float da = (h - _va).magnitude;
                    float db = (h - (_va + _vb)).magnitude;
                    if (da < 0.3f && da <= db) _grabbed = 0;
                    else if (db < 0.3f) _grabbed = 1;
                }
                if (_grabbed == 0) _va = Vector3.ClampMagnitude(h, 1f);
                else if (_grabbed == 1) _vb = Vector3.ClampMagnitude(h - _va, 1f);
            }
            else _grabbed = -1;

            _a.Aim(Vector3.zero, _va, _va.magnitude);
            _b.Aim(_va, _vb, _vb.magnitude);            // tip to tail, literally
            var sum = _va + _vb;
            _sum.Aim(Vector3.zero, sum, sum.magnitude);

            // The parallelogram, faint, so the other route to the same answer is visible too.
            _outline.Clear();
            _outline.Add(Vector3.zero); _outline.Add(_vb); _outline.Add(_vb + _va);
            _outline.Add(_va); _outline.Add(Vector3.zero);
            _ghost.Set(_outline);
        }
    }

    /// <summary>
    /// SYMMETRY — turn it until it looks the same, and feel it click.
    ///
    /// A shape with n-fold symmetry. Rotate it with the free hand; at every angle where it maps
    /// onto itself it snaps, brightens and sounds a consonance. The learner discovers the order of
    /// the symmetry group by feel — and the demonstration quietly makes the point that a symmetry
    /// is exactly a transformation you cannot detect.
    /// </summary>
    public class SymmetryDemo : ConceptDemo
    {
        Transform _shape;
        Material _mat;
        CurveView _marks;
        float _angle;
        int _fold = 5;
        int _lastSnap = -99;

        public override void Build()
        {
            _mat = PrismMaterials.New(PrismMaterials.Seed);
            _mat.SetColor("_Tint", Tint);
            _mat.SetFloat("_Growth", 1f);
            _mat.SetFloat("_Density", 1.1f);
            _shape = Body(Star(_fold), 0.72f, _mat, "shape");
            _marks = Curve(PrismPalette.Warm, 0.004f);
        }

        static Mesh Star(int points)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();
            int n = points * 2;
            for (int i = 0; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float r = (i % 2 == 0) ? 1f : 0.48f;
                verts.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            for (int i = 1; i < verts.Count - 1; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            var m = new Mesh { name = "star" };
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand) && hand.sqrMagnitude > 0.04f)
            {
                float target = Mathf.Atan2(hand.z, hand.x) * Mathf.Rad2Deg;
                _angle = Mathf.LerpAngle(_angle, target, dt * 7f);
            }
            else _angle += dt * 18f;

            _shape.localRotation = Quaternion.Euler(0f, -_angle, 0f);

            // How close are we to an angle where the shape maps onto itself?
            float step = 360f / _fold;
            float nearest = Mathf.Round(_angle / step);
            float err = Mathf.Abs(Mathf.DeltaAngle(_angle, nearest * step));
            float snap = Mathf.Clamp01(1f - err / 9f);

            _mat.SetFloat("_Hover", snap);
            _shape.localScale = Vector3.one * (0.72f * (1f + snap * 0.06f));

            int idx = Mathf.RoundToInt(nearest);
            if (snap > 0.85f && idx != _lastSnap)
            {
                _lastSnap = idx;
                Voice?.Consonance(transform.position, 0.35f, 1.4f);
            }
            else if (snap < 0.4f && idx == _lastSnap) _lastSnap = -99;
        }
    }

    /// <summary>
    /// PROBABILITY — put your hand in the cloud and find out what you get.
    ///
    /// The cloud has a real distribution. Each pinch draws one sample from where the hand is, and
    /// the samples stack into a histogram that converges on the distribution the learner cannot
    /// see directly. Nothing is revealed except by sampling, which is the honest situation.
    /// </summary>
    public class ProbabilityDemo : ConceptDemo
    {
        Transform _cloud;
        Transform[] _bins;
        int[] _counts;
        const int Bins = 11;
        int _total;
        bool _wasPinching;

        public override void Build()
        {
            _cloud = Cloud(0.75f, Tint, "cloud");
            _bins = new Transform[Bins];
            _counts = new int[Bins];
            for (int i = 0; i < Bins; i++)
                _bins[i] = Body(PrismMesh.Icosphere(1), 1f,
                                FlatMaterial(PrismPalette.Spectral(i / (float)(Bins - 1)), 0.7f), $"bin{i}");
        }

        protected override void OnTick(float dt)
        {
            _cloud.localRotation = Quaternion.Euler(0f, Age * 8f, 0f);

            bool pinching = FreePinch > 0.6f;
            if (pinching && !_wasPinching && TryFreeLocal(out var hand))
            {
                // Draw a sample from a Gaussian centred where the hand is. Box-Muller, so the tails
                // are real tails and the learner can be genuinely surprised now and then.
                float u1 = Mathf.Max(Random.value, 1e-6f), u2 = Random.value;
                float g = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
                float sample = Mathf.Clamp(hand.x + g * 0.28f, -1f, 1f);

                int bin = Mathf.Clamp(Mathf.FloorToInt((sample + 1f) * 0.5f * Bins), 0, Bins - 1);
                _counts[bin]++;
                _total++;
                Voice?.Settle(transform.position, 0.25f, 1.5f + sample);
            }
            _wasPinching = pinching;

            int peak = 1;
            for (int i = 0; i < Bins; i++) peak = Mathf.Max(peak, _counts[i]);

            for (int i = 0; i < Bins; i++)
            {
                float h = Mathf.Clamp01(_counts[i] / (float)peak) * 0.7f;
                _bins[i].localScale = new Vector3(0.07f, Mathf.Max(h, 0.004f), 0.07f);
                _bins[i].localPosition = new Vector3(
                    Mathf.Lerp(-0.85f, 0.85f, i / (float)(Bins - 1)), -0.9f + h, 0f);
            }
        }
    }

    /// <summary>
    /// FEEDBACK — the same loop stabilises or runs away, and only one number decides which.
    ///
    /// The free hand sets the loop gain. Below one, a disturbance dies out; above one, the same
    /// disturbance grows without bound. The learner can sit exactly on the boundary and watch it
    /// oscillate forever. Once seen here it is recognisable in climate, in electronics and in a
    /// population, which is precisely the transfer the constellation is built to make possible.
    /// </summary>
    public class FeedbackDemo : ConceptDemo
    {
        CurveView _trace, _loop;
        Transform _node;
        readonly List<float> _history = new List<float>();
        readonly List<Vector3> _pts = new List<Vector3>();
        float _x = 0.25f;
        float _gain = 0.85f;
        float _accum;

        public override void Build()
        {
            _loop = Curve(Tint, 0.008f, 4f);
            _loop.Set(t =>
            {
                float a = t * Mathf.PI * 2f;
                return new Vector3(Mathf.Cos(a) * 0.42f - 0.45f, 0f, Mathf.Sin(a) * 0.42f);
            }, 48);
            _trace = Curve(PrismPalette.Gold, 0.007f);
            _node = Ball(0.055f, PrismPalette.Coral, 0.9f, "node");
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _gain = Mathf.Clamp(Mathf.InverseLerp(-0.7f, 0.7f, hand.y) * 1.8f, 0f, 1.8f);

            // One discrete step of x <- gain * x, every 0.14 s.
            _accum += dt;
            while (_accum > 0.14f)
            {
                _accum -= 0.14f;
                _x *= _gain;
                if (Mathf.Abs(_x) > 4f) _x = 0.25f;          // it ran away; restart so it can be seen again
                if (Mathf.Abs(_x) < 1e-4f) _x = 0.25f;       // it died; restart
                _history.Add(_x);
                if (_history.Count > 60) _history.RemoveAt(0);
            }

            _pts.Clear();
            for (int i = 0; i < _history.Count; i++)
                _pts.Add(new Vector3(Mathf.Lerp(0.05f, 0.95f, i / 59f),
                                     Mathf.Clamp(_history[i], -1.1f, 1.1f) * 0.6f, 0f));
            if (_pts.Count > 2) _trace.Set(_pts);

            _node.localPosition = new Vector3(-0.45f, Mathf.Clamp(_x, -1.1f, 1.1f) * 0.42f, 0f);

            // Cyan below one, coral above: stable and unstable, decided by a single number.
            bool stable = _gain < 1f;
            _loop.Mat.SetColor("_Tint", stable ? PrismPalette.Cyan : PrismPalette.Coral);
            _loop.Mat.SetFloat("_Speed", Mathf.Lerp(0.2f, 2.2f, Mathf.Clamp01(_gain / 1.8f)));
            _trace.Mat.SetColor("_Tint", stable ? PrismPalette.Mint : PrismPalette.Coral);
        }
    }

    /// <summary>
    /// A calm fallback for any concept without a bespoke demonstration.
    ///
    /// It still responds to the hand, so it is never dead — but it deliberately does not pretend to
    /// demonstrate anything. An honest "nothing has been built here yet" beats a decorative
    /// animation that implies a lesson which does not exist.
    /// </summary>
    public class LatentDemo : ConceptDemo
    {
        Transform[] _motes;
        Transform _core;

        public override void Build()
        {
            _core = Cloud(0.45f, Tint, "core");
            _motes = new Transform[10];
            for (int i = 0; i < _motes.Length; i++)
                _motes[i] = Ball(0.03f, PrismPalette.Spectral(i / 9f), 0.7f, $"mote{i}");
        }

        protected override void OnTick(float dt)
        {
            bool hasHand = TryFreeLocal(out var hand);
            for (int i = 0; i < _motes.Length; i++)
            {
                float a = Age * 0.5f + i * Mathf.PI * 2f / _motes.Length;
                var home = new Vector3(Mathf.Cos(a), Mathf.Sin(a * 0.7f) * 0.4f, Mathf.Sin(a)) * 0.7f;
                // They shy away from an approaching hand rather than performing for it.
                if (hasHand)
                {
                    var away = home - hand;
                    float d = away.magnitude;
                    if (d < 0.6f) home += away.normalized * (0.6f - d) * 0.7f;
                }
                _motes[i].localPosition = Vector3.Lerp(_motes[i].localPosition, home, dt * 3f);
            }
            _core.localScale = Vector3.one * (0.45f * (1f + Mathf.Sin(Age * 1.3f) * 0.05f));
        }
    }
}
