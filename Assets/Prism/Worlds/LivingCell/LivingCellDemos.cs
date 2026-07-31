using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using UnityEngine;

namespace Prism.Worlds.LivingCell
{
    /// <summary>
    /// DIFFUSION — inject a drop and watch the gradient run downhill.
    ///
    /// A row of beads, each a real concentration cell solving the discretised Fick's second law
    /// (the same equation the oxygen shells use in the full Living Cell world, just in one
    /// dimension instead of a chain). Pinch anywhere along the row to add concentration there; it
    /// spreads to its neighbours and slowly clears on its own, exactly like a drop of dye settling
    /// in still water. Colour follows the world's own law: PrismPalette.Spectral of the local
    /// concentration, so a starved bead and a flooded one are never the same hue.
    /// </summary>
    [ConceptDemoFor("diffusion")]
    public class DiffusionDemo : ConceptDemo
    {
        const int N = 14;
        const float D = 0.9f, Decay = 0.30f;

        Transform[] _bead;
        Material[] _mat;
        float[] _c, _next;
        Transform _dropper;

        public override void Build()
        {
            _bead = new Transform[N];
            _mat = new Material[N];
            _c = new float[N];
            _next = new float[N];
            for (int i = 0; i < N; i++)
            {
                _mat[i] = FlatMaterial(PrismPalette.Cyan, 0.35f);
                _bead[i] = Body(PrismMesh.Icosphere(1), 0.045f, _mat[i], $"cell{i}");
                _bead[i].localPosition = new Vector3(Mathf.Lerp(-0.8f, 0.8f, i / (float)(N - 1)), 0f, 0f);
            }
            _dropper = Ball(0.03f, PrismPalette.Gold, 0.9f, "dropper");
        }

        protected override void OnTick(float dt)
        {
            bool hand = TryFreeLocal(out var h);
            int hi = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(-0.8f, 0.8f, h.x) * (N - 1)), 0, N - 1);

            // One real step of dC/dt = D*d2C/dx2 - decay*C, explicit finite difference. k is clamped
            // well under the 1D stability bound (0.5) regardless of dt, so a frame hitch cannot make
            // this ring or blow up.
            float k = Mathf.Clamp(D * dt, 0f, 0.4f);
            for (int i = 0; i < N; i++)
            {
                float left = _c[Mathf.Max(i - 1, 0)];
                float right = _c[Mathf.Min(i + 1, N - 1)];
                _next[i] = Mathf.Max(0f, _c[i] + k * (left + right - 2f * _c[i]) - Decay * _c[i] * dt);
            }
            var tmp = _c; _c = _next; _next = tmp;

            if (hand && FreePinch > 0.5f)
            {
                if (_c[hi] < 0.05f) Voice?.Settle(transform.position, 0.2f, 1.3f + hi * 0.02f);
                _c[hi] = Mathf.Min(_c[hi] + dt * 2.5f, 1.6f);
            }

            _dropper.gameObject.SetActive(hand);
            if (hand) _dropper.localPosition = new Vector3(Mathf.Clamp(h.x, -0.8f, 0.8f), 0.16f, 0f);

            for (int i = 0; i < N; i++)
            {
                float t = Mathf.Clamp01(_c[i] / 1.2f);
                _mat[i].SetColor("_Tint", PrismPalette.Spectral(t));
                _bead[i].localPosition = new Vector3(Mathf.Lerp(-0.8f, 0.8f, i / (float)(N - 1)), t * 0.10f, 0f);
                _bead[i].localScale = Vector3.one * Mathf.Lerp(0.045f, 0.075f, t);
            }
        }
    }

    /// <summary>
    /// MEMBRANE TRANSPORT — a wall does not stop a gradient, it multiplies it.
    ///
    /// One outside reservoir (large, effectively constant) and one inside pool that fills by real
    /// Fickian flux through a single gate: J = permeability * (C_out - C_in). Bring the free hand
    /// near the gate and permeability falls toward zero — the same "block a channel with a hand"
    /// gesture the full world uses, in miniature. The current between the two chambers speeds up
    /// and brightens exactly when the gate is open and the gradient is steep, and goes still when
    /// either one is not true.
    /// </summary>
    [ConceptDemoFor("membrane-transport")]
    public class MembraneTransportDemo : ConceptDemo
    {
        const float COut = 1f, Baseline = 0.85f, BlockRadius = 0.16f;

        Transform _gate, _inside;
        Material _gateMat, _insideMat;
        CurveView _flux;
        float _cIn;

        public override void Build()
        {
            var wall = Body(PrismMesh.Icosphere(2), 1f, FlatMaterial(PrismPalette.Lavender, 0.3f), "wall");
            wall.localScale = new Vector3(0.028f, 0.55f, 0.55f);

            _gateMat = FlatMaterial(PrismPalette.Gold, 0.5f);
            _gate = Body(PrismMesh.Icosphere(1), 0.05f, _gateMat, "gate");

            Cloud(0.28f, PrismPalette.Cyan, "outside").localPosition = new Vector3(-0.55f, 0f, 0f);

            _insideMat = PrismMaterials.New(PrismMaterials.Volumetric);
            _insideMat.SetColor("_Tint", PrismPalette.Cyan);
            _insideMat.SetColor("_EdgeTint", PrismPalette.Warm);
            _insideMat.SetFloat("_Density", 0.15f);
            _inside = Body(PrismMesh.Icosphere(3), 0.09f, _insideMat, "inside");
            _inside.localPosition = new Vector3(0.55f, 0f, 0f);

            _flux = Curve(PrismPalette.Gold, 0.010f, 4f);
            Segment(_flux, new Vector3(-0.30f, 0f, 0f), new Vector3(0.30f, 0f, 0f));
        }

        protected override void OnTick(float dt)
        {
            float blocked = 0f;
            if (TryFreeLocal(out var hand)) blocked = Mathf.Clamp01(1f - hand.magnitude / BlockRadius);
            float permeability = Baseline * (1f - blocked);

            float flux = permeability * (COut - _cIn);
            _cIn = Mathf.Clamp01(_cIn + flux * dt * 0.7f);

            float openness = permeability / Baseline;
            _gateMat.SetFloat("_Luminance", Mathf.Lerp(0.08f, 0.6f, openness));
            _gate.localScale = Vector3.one * Mathf.Lerp(0.024f, 0.05f, openness);

            _insideMat.SetFloat("_Density", Mathf.Lerp(0.15f, 1.7f, _cIn));
            _inside.localScale = Vector3.one * Mathf.Lerp(0.09f, 0.30f, _cIn);

            float mag = Mathf.Clamp01(Mathf.Abs(flux) * 3f);
            _flux.Mat.SetFloat("_Strength", mag);
            _flux.Mat.SetFloat("_Speed", Mathf.Lerp(0.1f, 1.8f, mag));
            _flux.Mat.SetFloat("_Pulse", mag);
        }
    }

    /// <summary>
    /// ENZYME CATALYSIS — the rate saturates because the enzyme is not consumed, but it is finite.
    ///
    /// Four docking sites orbit the enzyme; free substrate motes drift, hunt for an empty site, and
    /// bind. A bound mote only releases when the site's OWN turnover timer finishes — never sooner,
    /// no matter how much substrate is waiting outside. The free hand's height sets how much
    /// substrate is present. At low concentration the sites sit empty between visits and the rate
    /// tracks concentration almost linearly; push it high and every site is always occupied the
    /// instant it opens, so more substrate stops helping. That ceiling is Michaelis-Menten
    /// saturation, produced by the mechanism rather than read off a graph.
    /// </summary>
    [ConceptDemoFor("enzyme-catalysis")]
    public class EnzymeCatalysisDemo : ConceptDemo
    {
        const int Sites = 4, Motes = 9;
        const float Capture = 0.11f, Turnover = 0.6f;

        Material[] _siteMat;
        Transform[] _siteView;
        float[] _siteTimer;
        Transform[] _mote;
        Vector3[] _motePos;
        int[] _moteSite;
        float[] _motePhase;

        public override void Build()
        {
            Ball(0.15f, Tint, 0.5f, "enzyme");

            _siteMat = new Material[Sites];
            _siteView = new Transform[Sites];
            _siteTimer = new float[Sites];
            for (int i = 0; i < Sites; i++)
            {
                float a = i / (float)Sites * Mathf.PI * 2f;
                _siteMat[i] = FlatMaterial(PrismPalette.Violet, 0.25f);
                _siteView[i] = Body(PrismMesh.Icosphere(1), 0.038f, _siteMat[i], $"site{i}");
                _siteView[i].localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.16f;
            }

            _mote = new Transform[Motes];
            _motePos = new Vector3[Motes];
            _moteSite = new int[Motes];
            _motePhase = new float[Motes];
            for (int i = 0; i < Motes; i++)
            {
                _mote[i] = Ball(0.026f, PrismPalette.Gold, 0.85f, $"substrate{i}");
                _motePhase[i] = i * 0.7f;
                _moteSite[i] = -1;
                _motePos[i] = Shell(i);
            }
        }

        static Vector3 Shell(int i) =>
            new Vector3(Mathf.Cos(i * 2.4f), Mathf.Sin(i * 1.3f) * 0.5f, Mathf.Sin(i * 2.4f)) * 0.55f;

        protected override void OnTick(float dt)
        {
            float s01 = 0.5f;
            if (TryFreeLocal(out var hand)) s01 = Mathf.Clamp01(Mathf.InverseLerp(-0.7f, 0.7f, hand.y));
            int activeCount = Mathf.Clamp(Mathf.RoundToInt(s01 * Motes), 1, Motes);

            for (int i = 0; i < Sites; i++)
            {
                _siteTimer[i] -= dt;
                bool occ = _siteTimer[i] > 0f;
                _siteMat[i].SetColor("_Tint", occ ? PrismPalette.Gold : PrismPalette.Violet);
                _siteMat[i].SetFloat("_Luminance", occ ? 0.75f : 0.22f);
            }

            for (int i = 0; i < Motes; i++)
            {
                bool active = i < activeCount;
                _mote[i].gameObject.SetActive(active);
                if (!active) { _moteSite[i] = -1; continue; }

                if (_moteSite[i] >= 0)
                {
                    _motePos[i] = _siteView[_moteSite[i]].localPosition;
                    if (_siteTimer[_moteSite[i]] <= 0f) { _moteSite[i] = -1; _motePos[i] = Shell(i) * 0.7f; }
                }
                else
                {
                    _motePhase[i] += dt;
                    _motePos[i] += (-_motePos[i].normalized * 0.30f
                                  + new Vector3(Mathf.Sin(_motePhase[i] * 3f), Mathf.Cos(_motePhase[i] * 2f),
                                                Mathf.Sin(_motePhase[i] * 1.6f)) * 0.15f) * dt;
                    for (int s = 0; s < Sites; s++)
                    {
                        if (_siteTimer[s] > 0f) continue;
                        if ((_motePos[i] - _siteView[s].localPosition).sqrMagnitude > Capture * Capture) continue;
                        _moteSite[i] = s;
                        _siteTimer[s] = Turnover;
                        break;
                    }
                }
                _mote[i].localPosition = _motePos[i];
            }
        }
    }

    /// <summary>
    /// METABOLISM — production must track consumption, or the pool empties or floods.
    ///
    /// A ring of ten tokens is the same ATP currency the full Living Cell world counts, and it runs
    /// the identical rule: production saturates in how much ADP is left to spend (Michaelis-Menten,
    /// not a straight line), and consumption saturates in how much ATP is there to draw on. The free
    /// hand sets a production multiplier. Too low and every token goes cold; too high and every
    /// token pins bright and stops responding to anything — flooded, not thriving. A needle above
    /// the hub shows net direction and how hard the pool is being turned over right now, so "empty",
    /// "full and idle", and "full and roaring" all read as visibly different states.
    /// </summary>
    [ConceptDemoFor("metabolism")]
    public class MetabolismDemo : ConceptDemo
    {
        const int N = 10;
        const float VmaxP = 0.9f, VmaxC = 0.55f, Km = 0.35f;
        static readonly Color Spent = new Color(0.30f, 0.27f, 0.36f);

        Transform _hub;
        Material _hubMat;
        Material[] _tokenMat;
        CurveView _net;
        float _atp = 0.5f;

        public override void Build()
        {
            _hubMat = FlatMaterial(Tint, 0.4f);
            _hub = Body(PrismMesh.Icosphere(2), 0.11f, _hubMat, "hub");

            _tokenMat = new Material[N];
            for (int i = 0; i < N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                _tokenMat[i] = FlatMaterial(Spent, 0.3f);
                var t = Body(PrismMesh.Icosphere(1), 0.038f, _tokenMat[i], $"token{i}");
                t.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.62f;
            }

            _net = Curve(PrismPalette.Gold, 0.012f, 3f);
            Segment(_net, Vector3.zero, new Vector3(0f, 0.32f, 0f));
        }

        static float Sat(float c, float km) => Mathf.Max(0f, c) / (km + Mathf.Max(0f, c));

        protected override void OnTick(float dt)
        {
            float mult = 1f;
            if (TryFreeLocal(out var hand)) mult = Mathf.Clamp(Mathf.InverseLerp(-0.7f, 0.7f, hand.y) * 2.2f, 0f, 2.2f);

            // The same rule as the cell that opens the world this concept belongs to: both halves
            // saturate, and it is their balance, not their absolute size, that decides the pool.
            float production = VmaxP * mult * Sat(1f - _atp, Km);
            float consumption = VmaxC * Sat(_atp, Km);
            _atp = Mathf.Clamp01(_atp + (production - consumption) * dt * 0.6f);

            float litF = _atp * N;
            for (int i = 0; i < N; i++)
            {
                float b = Mathf.Clamp01(litF - i);
                _tokenMat[i].SetColor("_Tint", Color.Lerp(Spent, PrismPalette.Gold, b));
                _tokenMat[i].SetFloat("_Luminance", Mathf.Lerp(0.20f, 0.7f, b));
            }

            _hubMat.SetColor("_Tint", PrismPalette.Spectral(_atp));

            float net = production - consumption;
            _net.Mat.SetColor("_Tint", net >= 0f ? PrismPalette.Gold : PrismPalette.Violet);
            _net.Mat.SetFloat("_Strength", Mathf.Clamp01(Mathf.Abs(net) * 1.5f));
            _net.Mat.SetFloat("_Speed", Mathf.Lerp(0.2f, 1.8f, Mathf.Clamp01(Mathf.Abs(net))));
        }
    }
}
