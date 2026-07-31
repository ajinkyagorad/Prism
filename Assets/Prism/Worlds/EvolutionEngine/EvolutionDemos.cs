using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using UnityEngine;

namespace Prism.Worlds.EvolutionEngine
{
    /// <summary>
    /// Small math helpers shared by the four demonstrations below, so the Box-Muller draw and the
    /// Gaussian fitness kernel are written once. Identical in spirit to the constants used in
    /// EvolutionSim — these demos are a hand-held miniature of the same honest rules, not a
    /// separate, simplified retelling of them.
    /// </summary>
    static class EvoDemoMath
    {
        public static float Gaussian01()
        {
            float u1 = Mathf.Max(Random.value, 1e-6f);
            float u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        public static float GaussianKernel(float x, float sigma)
        {
            float s = Mathf.Max(sigma, 1e-4f);
            float t = x / s;
            return Mathf.Exp(-0.5f * t * t);
        }
    }

    /// <summary>
    /// VARIATION — a population that already differs, before anything is measured.
    ///
    /// Fourteen individuals are given a fixed trait each, once, at Build. Nothing in OnTick ever
    /// changes one: the free hand only READS them. Pinching near an individual logs its existing
    /// value into a bin, exactly like ProbabilityDemo's cloud — the difference is the whole
    /// point: ProbabilityDemo draws a NEW random sample every pinch, because it demonstrates
    /// probability. This demonstrates variation, so every pinch reveals a value that was already
    /// there and will be there again the next time, no matter how many times it is checked.
    /// </summary>
    [ConceptDemoFor("variation")]
    public class VariationDemo : ConceptDemo
    {
        const int N = 14;
        const int Bins = 8;

        readonly Transform[] _dots = new Transform[N];
        readonly Vector3[] _basePos = new Vector3[N];
        readonly float[] _trait = new float[N];
        readonly float[] _pulse = new float[N];

        readonly Transform[] _bins = new Transform[Bins];
        readonly int[] _counts = new int[Bins];

        bool _wasPinching;

        public override void Build()
        {
            for (int i = 0; i < N; i++)
            {
                float trait = Mathf.Clamp01(0.5f + EvoDemoMath.Gaussian01() * 0.22f);
                _trait[i] = trait;
                _basePos[i] = Random.insideUnitSphere * 0.5f + Vector3.up * 0.2f;

                var mat = PrismMaterials.CeramicBody(PrismPalette.Spectral(trait), 0.55f);
                float r = Mathf.Lerp(0.028f, 0.075f, trait);
                _dots[i] = Body(PrismMesh.Icosphere(2), r, mat, $"individual{i}");
                _dots[i].localPosition = _basePos[i];
            }

            for (int i = 0; i < Bins; i++)
            {
                _bins[i] = Body(PrismMesh.Icosphere(1), 1f,
                                 FlatMaterial(PrismPalette.Spectral(i / (float)(Bins - 1)), 0.65f), $"bin{i}");
                _bins[i].localPosition = new Vector3(Mathf.Lerp(-0.8f, 0.8f, i / (float)(Bins - 1)), -0.55f, -0.35f);
                _bins[i].localScale = new Vector3(0.05f, 0.004f, 0.05f);
            }
        }

        protected override void OnTick(float dt)
        {
            bool pinching = FreePinch > 0.6f;
            if (pinching && !_wasPinching && TryFreeLocal(out var hand))
            {
                int nearest = -1;
                float bestD = 0.16f * 0.16f;
                for (int i = 0; i < N; i++)
                {
                    float d = (_basePos[i] - hand).sqrMagnitude;
                    if (d < bestD) { bestD = d; nearest = i; }
                }
                if (nearest >= 0)
                {
                    _pulse[nearest] = 1f;
                    int bin = Mathf.Clamp(Mathf.FloorToInt(_trait[nearest] * Bins), 0, Bins - 1);
                    _counts[bin]++;
                    Voice?.Resonance(_dots[nearest].position, 0.28f, 1f + _trait[nearest]);
                }
            }
            _wasPinching = pinching;

            for (int i = 0; i < N; i++)
            {
                _pulse[i] = Mathf.MoveTowards(_pulse[i], 0f, dt * 1.4f);
                float bob = Mathf.Sin(Age * 0.8f + i * 1.7f) * 0.012f;
                _dots[i].localPosition = _basePos[i] + Vector3.up * bob;
                float baseR = Mathf.Lerp(0.028f, 0.075f, _trait[i]);
                _dots[i].localScale = Vector3.one * (baseR * (1f + _pulse[i] * 0.6f));
            }

            int peak = 1;
            for (int i = 0; i < Bins; i++) peak = Mathf.Max(peak, _counts[i]);
            for (int i = 0; i < Bins; i++)
            {
                float full = Mathf.Clamp01(_counts[i] / (float)peak) * 0.5f;
                float half = Mathf.Max(full, 0.004f) * 0.5f;
                var p = _bins[i].localPosition;
                _bins[i].localPosition = new Vector3(p.x, -0.55f + half, p.z);
                _bins[i].localScale = new Vector3(0.05f, half, 0.05f);
            }
        }
    }

    /// <summary>
    /// HERITABILITY — how closely offspring track a parent's own value.
    ///
    /// Two parents at fixed, well-separated trait values sit still; they are not touched, and
    /// nothing the learner does ever changes what THEY are. Offspring appear automatically, on a
    /// timer, alternating parent — the free hand never reaches into either parent to trigger a
    /// birth. What the free hand controls is scatter itself: hand height sets how tightly a new
    /// offspring's value is drawn around its parent's, from a near-copy to a wide spread. High
    /// heritability is a tight cloud that stays near its parent; low heritability is a cloud that
    /// drifts into the middle and starts to overlap the other parent's.
    /// </summary>
    [ConceptDemoFor("heritability")]
    public class HeritabilityDemo : ConceptDemo
    {
        const float ParentATrait = 0.20f;
        const float ParentBTrait = 0.80f;
        const int Capacity = 22;
        const float SpawnInterval = 0.5f;

        Transform _parentA, _parentB;
        readonly Transform[] _offspring = new Transform[Capacity];
        readonly Material[] _offspringMat = new Material[Capacity];
        int _nextSlot;
        float _accum;
        float _scatter01 = 0.5f;   // 0 = tight (high heritability), 1 = loose (low heritability)

        public override void Build()
        {
            var matA = PrismMaterials.CeramicBody(PrismPalette.Spectral(ParentATrait), 0.6f);
            _parentA = Body(PrismMesh.Icosphere(2), 0.08f, matA, "parentA");
            _parentA.localPosition = new Vector3(Mathf.Lerp(-0.8f, 0.8f, ParentATrait), 0.5f, 0f);

            var matB = PrismMaterials.CeramicBody(PrismPalette.Spectral(ParentBTrait), 0.6f);
            _parentB = Body(PrismMesh.Icosphere(2), 0.08f, matB, "parentB");
            _parentB.localPosition = new Vector3(Mathf.Lerp(-0.8f, 0.8f, ParentBTrait), 0.5f, 0f);

            for (int i = 0; i < Capacity; i++)
            {
                _offspringMat[i] = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.4f);
                _offspring[i] = Body(PrismMesh.Icosphere(1), 0.028f, _offspringMat[i], $"offspring{i}");
                _offspring[i].gameObject.SetActive(false);
            }
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _scatter01 = 1f - Mathf.Clamp01(Mathf.InverseLerp(-0.6f, 0.6f, hand.y));

            float sigma = Mathf.Lerp(0.02f, 0.16f, _scatter01);

            _accum += dt;
            while (_accum >= SpawnInterval)
            {
                _accum -= SpawnInterval;
                SpawnOffspring(sigma);
            }

            float breathe = 1f + Mathf.Sin(Age * 0.6f) * 0.03f;
            _parentA.localScale = Vector3.one * (0.08f * breathe);
            _parentB.localScale = Vector3.one * (0.08f * breathe);
        }

        void SpawnOffspring(float sigma)
        {
            int slot = _nextSlot;
            bool fromA = (slot % 2) == 0;
            _nextSlot = (_nextSlot + 1) % Capacity;

            float parentTrait = fromA ? ParentATrait : ParentBTrait;
            float trait = Mathf.Clamp01(parentTrait + EvoDemoMath.Gaussian01() * sigma);

            float x = Mathf.Lerp(-0.8f, 0.8f, trait);
            float z = (Random.value - 0.5f) * 0.55f;
            float y = 0.12f + Random.value * 0.28f;

            _offspring[slot].localPosition = new Vector3(x, y, z);
            _offspringMat[slot].SetColor("_Tint", PrismPalette.Spectral(trait));
            _offspring[slot].gameObject.SetActive(true);

            Voice?.Resonance((fromA ? _parentA : _parentB).position, 0.18f, 1f + trait);
        }
    }

    /// <summary>
    /// SELECTION — the same twelve individuals, generation after generation, under a filter the
    /// free hand sets.
    ///
    /// Every half second one individual is replaced: WHO is replaced is a weighted random draw
    /// favouring a poor match to the current environment, and WHO the replacement is born from is
    /// a weighted random draw favouring a good match — never a guaranteed "kill the worst," which
    /// would be an optimizer, not a filter. Held at one setting for a few seconds, the ring of
    /// colours visibly leans toward whatever the environment currently favours; that lean is
    /// EARNED by the accumulated draws, not applied to the population directly.
    /// </summary>
    [ConceptDemoFor("selection")]
    public class SelectionDemo : ConceptDemo
    {
        const int N = 12;
        const float Sigma = 0.22f;
        const float MutationSigma = 0.05f;
        const float StepInterval = 0.5f;

        readonly Transform[] _dots = new Transform[N];
        readonly Material[] _mat = new Material[N];
        readonly Vector3[] _basePos = new Vector3[N];
        readonly float[] _trait = new float[N];

        Transform _envToken;
        Material _envMat;
        float _temperature = 0.5f;
        float _accum;

        public override void Build()
        {
            for (int i = 0; i < N; i++)
            {
                _trait[i] = Random.value;
                float a = i / (float)N * Mathf.PI * 2f;
                _basePos[i] = new Vector3(Mathf.Cos(a) * 0.55f, 0f, Mathf.Sin(a) * 0.55f);

                _mat[i] = PrismMaterials.CeramicBody(PrismPalette.Spectral(_trait[i]), 0.55f);
                _dots[i] = Body(PrismMesh.Icosphere(2), 0.052f, _mat[i], $"individual{i}");
                _dots[i].localPosition = _basePos[i];
            }

            _envMat = PrismMaterials.CeramicBody(PrismPalette.Spectral(0.5f), 0.7f);
            _envToken = Body(PrismMesh.Icosphere(2), 0.045f, _envMat, "environment");
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _temperature = Mathf.Clamp01(Mathf.InverseLerp(-0.6f, 0.6f, hand.y));

            float optimum = Mathf.Lerp(0.85f, 0.15f, _temperature);
            _envMat.SetColor("_Tint", PrismPalette.Spectral(optimum));
            _envToken.localPosition = new Vector3(0f, Mathf.Lerp(-0.15f, 0.75f, _temperature), 0f);

            _accum += dt;
            while (_accum >= StepInterval)
            {
                _accum -= StepInterval;
                StepGeneration(optimum);
            }

            for (int i = 0; i < N; i++)
            {
                float bob = Mathf.Sin(Age * 1.1f + i * 2.1f) * 0.01f;
                _dots[i].localPosition = _basePos[i] + Vector3.up * bob;
            }
        }

        void StepGeneration(float optimum)
        {
            int dieIndex = WeightedPick(optimum, invert: true);
            int parentIndex = WeightedPick(optimum, invert: false);
            if (dieIndex < 0 || parentIndex < 0 || dieIndex == parentIndex) return;

            float childTrait = Mathf.Clamp01(_trait[parentIndex] + EvoDemoMath.Gaussian01() * MutationSigma);
            _trait[dieIndex] = childTrait;
            _mat[dieIndex].SetColor("_Tint", PrismPalette.Spectral(childTrait));
            Voice?.Settle(_dots[dieIndex].position, 0.18f, 1f + childTrait);
        }

        /// <summary>Roulette-wheel selection over the current population's match to the
        /// environment. <paramref name="invert"/> favours a POOR match (who is replaced);
        /// otherwise it favours a GOOD match (who the replacement is born from).</summary>
        int WeightedPick(float optimum, bool invert)
        {
            float total = 0f;
            for (int i = 0; i < N; i++)
            {
                float m = EvoDemoMath.GaussianKernel(_trait[i] - optimum, Sigma);
                total += (invert ? 1f - m : m) + 0.05f;
            }
            if (total <= 1e-5f) return -1;

            float r = Random.value * total;
            float acc = 0f;
            for (int i = 0; i < N; i++)
            {
                float m = EvoDemoMath.GaussianKernel(_trait[i] - optimum, Sigma);
                acc += (invert ? 1f - m : m) + 0.05f;
                if (r <= acc) return i;
            }
            return N - 1;
        }
    }

    /// <summary>
    /// FITNESS — nine individuals, permanent and untouched, each carrying a bar that reads how
    /// many offspring its OWN fixed trait would be expected to leave RIGHT NOW.
    ///
    /// This demo deliberately never lets a single individual's bar be the tallest for long: as
    /// the free hand moves, the environment's favoured value sweeps across the whole spread, and
    /// the tallest bar — the "fittest" one — visibly changes which individual it belongs to. No
    /// individual ever changes. Only the number over its head does, because that number was never
    /// a property of the individual alone — it is what THIS environment currently pays this trait
    /// value in offspring, and it would swap to the other end if the environment did.
    /// </summary>
    [ConceptDemoFor("fitness")]
    public class FitnessDemo : ConceptDemo
    {
        const int N = 9;
        const float Sigma = 0.22f;
        const float MaxBarHeight = 0.55f;

        readonly Transform[] _dots = new Transform[N];
        readonly Transform[] _bars = new Transform[N];
        readonly Material[] _barMat = new Material[N];
        readonly float[] _trait = new float[N];
        readonly float[] _x = new float[N];

        Transform _envToken;
        Material _envMat;
        float _temperature = 0.5f;

        public override void Build()
        {
            for (int i = 0; i < N; i++)
            {
                float trait = (i + 0.5f) / N;
                _trait[i] = trait;
                _x[i] = Mathf.Lerp(-0.8f, 0.8f, trait);

                var bodyMat = PrismMaterials.CeramicBody(PrismPalette.Spectral(trait), 0.5f);
                _dots[i] = Body(PrismMesh.Icosphere(2), 0.045f, bodyMat, $"individual{i}");
                _dots[i].localPosition = new Vector3(_x[i], -0.4f, 0f);

                _barMat[i] = PrismMaterials.CeramicBody(PrismPalette.Spectral(trait), 0.6f);
                _bars[i] = Body(PrismMesh.Icosphere(1), 1f, _barMat[i], $"bar{i}");
                _bars[i].localPosition = new Vector3(_x[i], -0.3f, 0f);
            }

            _envMat = PrismMaterials.CeramicBody(PrismPalette.Spectral(0.5f), 0.75f);
            _envToken = Body(PrismMesh.Icosphere(2), 0.05f, _envMat, "environment");
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _temperature = Mathf.Clamp01(Mathf.InverseLerp(-0.6f, 0.6f, hand.y));

            float optimum = Mathf.Lerp(0.85f, 0.15f, _temperature);
            _envMat.SetColor("_Tint", PrismPalette.Spectral(optimum));
            _envToken.localPosition = new Vector3(Mathf.Lerp(-0.8f, 0.8f, optimum), 0.55f, 0f);

            int best = -1;
            float bestFit = -1f;
            for (int i = 0; i < N; i++)
            {
                float fit = EvoDemoMath.GaussianKernel(_trait[i] - optimum, Sigma);
                if (fit > bestFit) { bestFit = fit; best = i; }

                float full = Mathf.Max(fit, 0.03f) * MaxBarHeight;
                float half = full * 0.5f;
                _bars[i].localPosition = new Vector3(_x[i], -0.3f + half, 0f);
                _bars[i].localScale = new Vector3(0.045f, half, 0.045f);
            }

            for (int i = 0; i < N; i++)
            {
                bool isBest = i == best;
                float pulse = isBest ? (0.5f + 0.5f * Mathf.Sin(Age * 5f)) : 0f;
                _dots[i].localScale = Vector3.one * (0.045f * (1f + pulse * 0.4f));
            }
        }
    }
}
