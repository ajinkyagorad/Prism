using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Demos
{
    /// <summary>
    /// GRAVITY — a real potential well you can dent with your hand.
    ///
    /// The sheet is displaced by the actual summed potential of the masses on it, so the funnel
    /// shape is not drawn, it is computed. Bring the free hand close and it becomes a second mass:
    /// two wells merge, and the marble that was circling one starts to feel the other.
    /// </summary>
    public class GravityDemo : ConceptDemo
    {
        const int Res = 26;
        Mesh _sheet;
        readonly List<Vector3> _base = new List<Vector3>();
        readonly List<Vector3> _work = new List<Vector3>();
        Transform _primary, _marble, _handMass;
        Vector3 _p, _v;

        public override void Build()
        {
            var mat = PrismMaterials.New(PrismMaterials.Field);
            mat.SetFloat("_ContourStep", 0.09f);
            mat.SetFloat("_Opacity", 0.5f);
            mat.SetFloat("_FadeOuter", 1.15f);
            mat.SetInt("_BodyCount", 0);
            _sheet = Sheet(Res, mat, out _, 1f);
            _sheet.GetVertices(_base);

            _primary = Ball(0.10f, Tint, 0.7f, "mass");
            _marble = Ball(0.045f, PrismPalette.Gold, 0.8f, "marble");
            _handMass = Ball(0.07f, PrismPalette.Coral, 0.8f, "handMass");
            _handMass.gameObject.SetActive(false);

            _p = new Vector3(0.55f, 0f, 0f);
            _v = new Vector3(0f, 0f, 0.62f);       // roughly circular for the mu below
        }

        protected override void OnTick(float dt)
        {
            bool hasHand = TryFreeLocal(out var hand);
            hand.y = 0f;
            hasHand &= hand.sqrMagnitude < 1.4f;
            _handMass.gameObject.SetActive(hasHand);
            if (hasHand) _handMass.localPosition = hand + Vector3.up * 0.06f;

            // Two attractors: the concept, and the learner's hand.
            const float mu = 0.22f, muHand = 0.13f, soft = 0.16f;

            Vector3 Accel(Vector3 at)
            {
                Vector3 a = Vector3.zero;
                Vector3 d = -at;
                a += d * (mu / Mathf.Pow(d.sqrMagnitude + soft * soft, 1.5f));
                if (hasHand)
                {
                    Vector3 dh = hand - at;
                    a += dh * (muHand / Mathf.Pow(dh.sqrMagnitude + soft * soft, 1.5f));
                }
                return a;
            }

            // Leapfrog, same reason as everywhere else in this project: the marble must not spiral.
            for (int i = 0; i < 4; i++)
            {
                float h = dt / 4f;
                _v += Accel(_p) * (h * 0.5f);
                _p += _v * h;
                _v += Accel(_p) * (h * 0.5f);
            }
            if (_p.magnitude > 1.3f) { _p = new Vector3(0.55f, 0f, 0f); _v = new Vector3(0f, 0f, 0.62f); }
            _marble.localPosition = _p + Vector3.up * 0.03f;

            // The sheet dips by the potential. Clamped near the masses or it would spike to infinity.
            _work.Clear();
            for (int i = 0; i < _base.Count; i++)
            {
                var v = _base[i];
                float phi = -mu / Mathf.Sqrt(v.sqrMagnitude + soft * soft);
                if (hasHand)
                {
                    var dh = v - hand;
                    phi -= muHand / Mathf.Sqrt(dh.sqrMagnitude + soft * soft);
                }
                v.y = Mathf.Max(phi * 0.9f, -0.85f);
                _work.Add(v);
            }
            SheetApply(_sheet, _work);
        }
    }

    /// <summary>
    /// INVERSE SQUARE — why it is a square, shown rather than asserted.
    ///
    /// A fixed number of rays leave a point. Move your hand out and the same rays spread over a
    /// shell whose area grows as r^2, so the count crossing your hand's patch falls as 1/r^2. The
    /// patch shows its own tally, so the learner watches the number fall by four when they double
    /// the distance.
    /// </summary>
    public class InverseSquareDemo : ConceptDemo
    {
        const int Rays = 64;
        Transform _source;
        CurveView[] _rays;
        Transform _patch;
        Material _patchMat;
        readonly List<Vector3> _pts = new List<Vector3>(2);

        public override void Build()
        {
            _source = Ball(0.09f, PrismPalette.Gold, 1f, "source");
            _rays = new CurveView[Rays];
            for (int i = 0; i < Rays; i++) _rays[i] = Curve(Tint, 0.006f);

            _patchMat = PrismMaterials.New(PrismMaterials.Gel);
            _patchMat.SetColor("_Tint", PrismPalette.Coral);
            _patchMat.SetFloat("_Density", 1.2f);
            _patch = Body(PrismMesh.Icosphere(2), 0.16f, _patchMat, "patch");
        }

        protected override void OnTick(float dt)
        {
            float r = 0.75f;
            if (TryFreeLocal(out var hand) && hand.magnitude > 0.2f)
                r = Mathf.Clamp(hand.magnitude, 0.3f, 1.25f);

            _patch.localPosition = (TryFreeLocal(out var h2) && h2.sqrMagnitude > 1e-4f)
                                 ? h2.normalized * r : Vector3.forward * r;

            for (int i = 0; i < Rays; i++)
            {
                // Fibonacci sphere: evenly spread directions with no clumping at the poles.
                float t = (i + 0.5f) / Rays;
                float phi = Mathf.Acos(1f - 2f * t);
                float theta = Mathf.PI * (1f + Mathf.Sqrt(5f)) * i;
                var dir = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta),
                                      Mathf.Cos(phi),
                                      Mathf.Sin(phi) * Mathf.Sin(theta));
                _pts.Clear();
                _pts.Add(dir * 0.09f);
                _pts.Add(dir * r);
                _rays[i].Set(_pts);
            }

            // Intensity as the learner would measure it: 1/r^2, normalised to 1 at r = 0.5.
            float intensity = 0.25f / (r * r);
            _patchMat.SetFloat("_Density", Mathf.Clamp(intensity * 1.4f, 0.15f, 3f));
            _patchMat.SetColor("_Tint", PrismPalette.Spectral(Mathf.Clamp01(1f - intensity * 0.4f)));
            _patch.localScale = Vector3.one * 0.16f;
        }

        /// <summary>Flux through the hand's patch, for a future readout.</summary>
        public float Intensity { get; private set; }
    }

    /// <summary>
    /// KEPLER'S SECOND LAW — equal areas in equal times, drawn as they are swept.
    ///
    /// Wedges fill in behind the moon at fixed time intervals. They look wildly different in shape
    /// and are identical in area, which is the whole surprise. The free hand scrubs time.
    /// </summary>
    public class KeplerDemo : ConceptDemo
    {
        CurveView _orbit, _wedge;
        Transform _focus, _moon;
        readonly List<Vector3> _fan = new List<Vector3>();
        float _theta;
        const float Ecc = 0.55f, P = 0.55f;   // semi-latus rectum

        public override void Build()
        {
            _orbit = Curve(Tint, 0.008f);
            _wedge = Curve(PrismPalette.Gold, 0.006f);
            _focus = Ball(0.10f, PrismPalette.Gold, 0.8f, "focus");
            _moon = Ball(0.04f, PrismPalette.Cyan, 0.9f, "moon");

            _orbit.Set(t =>
            {
                float a = t * Mathf.PI * 2f;
                float r = P / (1f + Ecc * Mathf.Cos(a));
                return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }, 96);
        }

        Vector3 At(float theta)
        {
            float r = P / (1f + Ecc * Mathf.Cos(theta));
            return new Vector3(Mathf.Cos(theta) * r, 0f, Mathf.Sin(theta) * r);
        }

        protected override void OnTick(float dt)
        {
            // Real angular rate: dtheta/dt = h / r^2, which is what makes it hurry at periapsis.
            float rate = 1f;
            if (TryFreeLocal(out var hand)) rate = Mathf.Clamp(1f + hand.x * 2.2f, -2f, 3f);

            float r = P / (1f + Ecc * Mathf.Cos(_theta));
            _theta += (0.20f / (r * r)) * rate * dt;
            _moon.localPosition = At(_theta);

            // The wedge swept over the last fixed interval of TIME.
            const float window = 0.55f;
            _fan.Clear();
            _fan.Add(Vector3.zero);
            float th = _theta;
            float elapsed = 0f;
            while (elapsed < window && _fan.Count < 40)
            {
                float rr = P / (1f + Ecc * Mathf.Cos(th));
                float step = 0.05f;
                th -= (0.20f / (rr * rr)) * step;
                elapsed += step;
                _fan.Add(At(th));
            }
            _fan.Add(Vector3.zero);
            _wedge.Set(_fan);
        }
    }

    /// <summary>
    /// ESCAPE VELOCITY — the boundary, found by hand.
    ///
    /// The free hand's height sets the launch speed and the predicted path redraws live. Below the
    /// line it comes back; above it, it never does. The path changes colour the instant the sign of
    /// the specific energy changes, so the learner sees the boundary as a boundary.
    /// </summary>
    public class EscapeVelocityDemo : ConceptDemo
    {
        CurveView _path;
        Transform _planet, _probe;
        Material _pathMat;
        const float Mu = 0.16f, R0 = 0.30f;
        readonly List<Vector3> _pts = new List<Vector3>(120);

        public override void Build()
        {
            _planet = Ball(0.22f, Tint, 0.6f, "planet");
            _probe = Ball(0.035f, PrismPalette.Gold, 0.9f, "probe");
            _path = Curve(PrismPalette.Cyan, 0.008f);
            _pathMat = _path.Mat;
        }

        protected override void OnTick(float dt)
        {
            float vEsc = Mathf.Sqrt(2f * Mu / R0);
            float speed = vEsc * 0.75f;
            if (TryFreeLocal(out var hand))
                speed = Mathf.Clamp(Mathf.InverseLerp(-0.6f, 0.8f, hand.y), 0f, 1f) * vEsc * 1.6f;

            var pos = new Vector3(R0, 0f, 0f);
            var vel = new Vector3(0f, 0f, speed);
            float energy = 0.5f * speed * speed - Mu / R0;

            _pts.Clear();
            for (int i = 0; i < 120; i++)
            {
                float h = 0.055f;
                Vector3 A(Vector3 p) => -p * (Mu / Mathf.Pow(p.sqrMagnitude + 0.02f, 1.5f));
                vel += A(pos) * (h * 0.5f);
                pos += vel * h;
                vel += A(pos) * (h * 0.5f);
                _pts.Add(pos);
                if (pos.magnitude > 1.35f) break;
            }
            _path.Set(_pts);
            if (_pts.Count > 0) _probe.localPosition = _pts[Mathf.Min(_pts.Count - 1, (int)(Age * 30f) % _pts.Count)];

            // Cyan while bound, violet once it is gone. The colour IS the sign of the energy.
            _pathMat.SetColor("_Tint", energy < 0f ? PrismPalette.Cyan : PrismPalette.Violet);
            _pathMat.SetFloat("_Pulse", energy >= 0f ? 1f : 0.2f);
        }
    }

    /// <summary>
    /// TIDES — why there are two bulges and not one.
    ///
    /// The ocean shell is displaced by the DIFFERENTIAL field: the moon's pull at each point minus
    /// its pull at the planet's centre. That difference stretches the water along the line to the
    /// moon in both directions, which is the part everyone gets wrong. Drag the moon and both
    /// bulges follow it.
    /// </summary>
    public class TidesDemo : ConceptDemo
    {
        const int Res = 48;
        Mesh _ocean;
        readonly List<Vector3> _base = new List<Vector3>();
        readonly List<Vector3> _work = new List<Vector3>();
        Transform _planet, _moon;
        Vector3 _moonPos = new Vector3(0.95f, 0f, 0f);

        public override void Build()
        {
            _planet = Ball(0.32f, PrismPalette.Mint, 0.45f, "planet");

            var mat = PrismMaterials.New(PrismMaterials.Gel);
            mat.SetColor("_Tint", PrismPalette.Cyan);
            mat.SetColor("_DeepTint", PrismPalette.Violet);
            mat.SetFloat("_Density", 1.3f);

            var sphere = PrismMesh.Icosphere(3);
            _ocean = Object.Instantiate(sphere);
            _ocean.name = "ocean";
            _ocean.MarkDynamic();
            _ocean.GetVertices(_base);

            var go = new GameObject("ocean");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _ocean;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * 0.38f;
            Track(mat);

            _moon = Ball(0.10f, PrismPalette.Warm, 0.7f, "moon");
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand) && hand.sqrMagnitude > 0.25f)
                _moonPos = hand.normalized * 0.95f;
            else
                _moonPos = new Vector3(Mathf.Cos(Age * 0.35f), 0f, Mathf.Sin(Age * 0.35f)) * 0.95f;
            _moon.localPosition = _moonPos;

            // Tidal displacement is the DIFFERENCE between the pull here and the pull at the centre.
            _work.Clear();
            Vector3 centrePull = _moonPos.normalized * (1f / _moonPos.sqrMagnitude);
            for (int i = 0; i < _base.Count; i++)
            {
                var n = _base[i];                       // unit sphere: position is the normal
                Vector3 d = _moonPos - n;
                Vector3 pull = d.normalized / Mathf.Max(d.sqrMagnitude, 0.02f);
                float radial = Vector3.Dot(pull - centrePull, n);
                _work.Add(n * (1f + Mathf.Clamp(radial * 0.16f, -0.16f, 0.22f)));
            }
            SheetApply(_ocean, _work);
        }
    }

    /// <summary>
    /// WHY DOESN'T THE MOON FALL DOWN — Newton's cannonball, which is the answer.
    ///
    /// It IS falling. It keeps missing. Raise the free hand to fire faster: the ball lands closer
    /// and closer to the horizon, then all the way round, then leaves. Nothing else in this project
    /// answers its question as completely, which is why the Question node gets it.
    /// </summary>
    public class CannonballDemo : ConceptDemo
    {
        CurveView _path;
        Transform _globe, _ball;
        Material _pathMat;
        readonly List<Vector3> _pts = new List<Vector3>(200);
        const float Mu = 0.20f, R = 0.34f;

        public override void Build()
        {
            _globe = Ball(R, PrismPalette.Mint, 0.45f, "globe");
            _ball = Ball(0.028f, PrismPalette.Coral, 1f, "ball");
            _path = Curve(PrismPalette.Gold, 0.007f);
            _pathMat = _path.Mat;
        }

        protected override void OnTick(float dt)
        {
            float vCirc = Mathf.Sqrt(Mu / R);
            float t = 0.45f;
            if (TryFreeLocal(out var hand)) t = Mathf.Clamp01(Mathf.InverseLerp(-0.55f, 0.75f, hand.y));
            float speed = t * vCirc * 1.7f;

            var pos = new Vector3(0f, R, 0f);          // the top of the mountain
            var vel = new Vector3(speed, 0f, 0f);      // fired horizontally

            _pts.Clear();
            bool landed = false;
            for (int i = 0; i < 200; i++)
            {
                float h = 0.05f;
                Vector3 A(Vector3 p) => -p * (Mu / Mathf.Pow(p.sqrMagnitude + 0.004f, 1.5f));
                vel += A(pos) * (h * 0.5f);
                pos += vel * h;
                vel += A(pos) * (h * 0.5f);
                _pts.Add(pos);
                if (pos.magnitude < R * 0.99f) { landed = true; break; }
                if (pos.magnitude > 1.35f) break;
            }
            _path.Set(_pts);
            if (_pts.Count > 0)
                _ball.localPosition = _pts[Mathf.Min(_pts.Count - 1, (int)(Age * 45f) % _pts.Count)];

            // Coral while it still falls back to the ground, gold once it has stopped landing.
            _pathMat.SetColor("_Tint", landed ? PrismPalette.Coral : PrismPalette.Gold);
            _pathMat.SetFloat("_Pulse", landed ? 0.15f : 1f);
        }
    }

    /// <summary>
    /// ORBITAL MECHANICS — a miniature of the world this concept opens.
    /// A moon on an eccentric orbit with a speed-coloured trail, so the constellation shows a
    /// preview of what is inside before the learner commits to entering.
    /// </summary>
    public class OrbitalMiniDemo : ConceptDemo
    {
        CurveView _orbit;
        Transform _planet, _moon;
        float _theta;
        float _ecc = 0.42f;

        // The orbit curve only changes shape when the learner changes the eccentricity, so it is
        // rebuilt on change rather than every frame — and through the LIST overload, because
        // Set(Func<float,Vector3>, int) allocates a delegate on every call when the lambda captures
        // `this`. (Trap reported by the Machine Cathedral module, which hit it in its own draft.)
        readonly System.Collections.Generic.List<Vector3> _orbitPts =
            new System.Collections.Generic.List<Vector3>(80);
        float _builtEcc = float.NaN;

        public override void Build()
        {
            _planet = Ball(0.16f, Tint, 0.65f, "planet");
            _moon = Ball(0.045f, PrismPalette.Gold, 0.95f, "moon");
            _orbit = Curve(PrismPalette.Lavender, 0.007f, 3f);
        }

        Vector3 At(float th)
        {
            float r = 0.52f / (1f + _ecc * Mathf.Cos(th));
            return new Vector3(Mathf.Cos(th) * r, 0f, Mathf.Sin(th) * r);
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _ecc = Mathf.Clamp(Mathf.InverseLerp(-0.7f, 0.7f, hand.y) * 0.85f, 0f, 0.85f);

            if (Mathf.Abs(_ecc - _builtEcc) > 0.002f)
            {
                _builtEcc = _ecc;
                _orbitPts.Clear();
                for (int i = 0; i < 80; i++)
                    _orbitPts.Add(At(i / 79f * Mathf.PI * 2f));
                _orbit.Set(_orbitPts);
            }

            float r = 0.52f / (1f + _ecc * Mathf.Cos(_theta));
            _theta += (0.16f / (r * r)) * dt;
            _moon.localPosition = At(_theta);
        }
    }
}
