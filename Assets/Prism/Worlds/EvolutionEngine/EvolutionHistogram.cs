using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Worlds.EvolutionEngine
{
    /// <summary>
    /// The trait histogram: <see cref="EvolutionSim.BinCount"/> small bars, one per bin, each
    /// coloured exactly like the organisms that fall in it — <c>PrismPalette.Spectral</c> at the
    /// bin's own trait value, fixed once at creation. A learner who has been watching the
    /// population's colour recognises these colours immediately; this is deliberately the SAME
    /// encoding as <see cref="EvolutionOrganismField"/>, not a second one.
    ///
    /// Not revealed until Discover — see EvolutionEngineWorld — because the whole pedagogical
    /// point is that the learner produces the shift with their own hands BEFORE being shown a
    /// chart of it. The world only calls <see cref="UpdateBars"/> from Discover onward, so before
    /// that every bar simply sits at its construction-time height of zero.
    ///
    /// Bars are stretched icospheres, not boxes — PRISM has no box primitive, and the aesthetic
    /// law explicitly rules out rectangular panels anyway. A slender rounded column reads as a
    /// small crystal, which fits a product that otherwise has no dashboard chrome in it at all.
    /// Height is shown as RELATIVE FREQUENCY (this bin's share of the currently living
    /// population), not a raw count, so the shape of the distribution reads independently of how
    /// large the population happens to be at the moment — carrying capacity changes the total
    /// count for reasons that have nothing to do with the trait, and the histogram should not
    /// wobble because of that.
    /// </summary>
    public class EvolutionHistogram
    {
        public const float AxisWidth    = 0.24f;   // metres, bin 0 centre to last bin centre
        public const float MaxBarHeight = 0.13f;   // metres
        public const float MinBarHeight = 0.0015f; // never fully collapses, reads as "zero" not "gone"
        public const float BarThickness = 0.013f;
        const float FullHeightFraction  = 0.42f;   // a bin holding this share of the population maxes out
        const float HeightRatePerSecond = 0.6f;    // metres/second the displayed height chases the target

        readonly Transform[] _bars;
        readonly float[] _shown;
        readonly Transform _root;

        /// <summary>This histogram's own local position within the world anchor's space.</summary>
        public Vector3 AxisLocalCentre { get; }

        public Transform Root => _root;

        public EvolutionHistogram(Transform parent, Vector3 localCentre)
        {
            AxisLocalCentre = localCentre;

            var rootGo = new GameObject("Histogram");
            _root = rootGo.transform;
            _root.SetParent(parent, false);
            _root.localPosition = localCentre;

            int n = EvolutionSim.BinCount;
            _bars = new Transform[n];
            _shown = new float[n];
            var mesh = PrismMesh.Icosphere(1);

            for (int i = 0; i < n; i++)
            {
                float u = (i + 0.5f) / n;
                var mat = PrismMaterials.CeramicBody(PrismPalette.Spectral(u), 0.38f, 260f + i * 9f);

                var go = new GameObject($"Bar{i}");
                go.transform.SetParent(_root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;

                float x = BinLocalX(i);
                float half = MinBarHeight * 0.5f;
                go.transform.localPosition = new Vector3(x, half, 0f);
                go.transform.localScale = new Vector3(BarThickness, half, BarThickness);
                _bars[i] = go.transform;
            }
        }

        public static float BinLocalX(int bin) =>
            Mathf.Lerp(-AxisWidth * 0.5f, AxisWidth * 0.5f, (bin + 0.5f) / EvolutionSim.BinCount);

        /// <summary>Where a raw trait value sits along the axis, in this histogram's local X.</summary>
        public static float TraitToLocalX(float trait) =>
            Mathf.Lerp(-AxisWidth * 0.5f, AxisWidth * 0.5f, EvolutionSim.TraitToU01(trait));

        public static float LocalXToTrait(float x) =>
            EvolutionSim.U01ToTrait(Mathf.InverseLerp(-AxisWidth * 0.5f, AxisWidth * 0.5f, x));

        public void Show(bool on) => _root.gameObject.SetActive(on);
        public bool Visible => _root.gameObject.activeSelf;

        /// <summary>Recompute bar heights from the sim's current bins. Only meaningful, and only
        /// called by the world, once the histogram has been revealed at Discover.</summary>
        public void UpdateBars(EvolutionSim sim, float dt)
        {
            int total = sim.AliveCount;
            var bins = sim.Bins;
            float maxStep = HeightRatePerSecond * dt;

            for (int i = 0; i < _bars.Length; i++)
            {
                float frac = total > 0 ? bins[i] / (float)total : 0f;
                float target = Mathf.Clamp(frac / FullHeightFraction, 0f, 1f) * MaxBarHeight;
                target = Mathf.Max(target, MinBarHeight);

                _shown[i] = Mathf.MoveTowards(_shown[i], target, maxStep);
                float half = _shown[i] * 0.5f;

                var t = _bars[i];
                var scale = t.localScale; scale.y = half; t.localScale = scale;
                var pos = t.localPosition; pos.y = half; t.localPosition = pos;
            }
        }
    }
}
