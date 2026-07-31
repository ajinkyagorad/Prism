using System.Collections.Generic;
using UnityEngine;

namespace Prism.Worlds.EvolutionEngine
{
    /// <summary>
    /// One organism. A plain data record; the array that holds these is the entire population.
    ///
    /// <see cref="Trait"/> never changes after birth. That is deliberate and load-bearing: it is
    /// what makes this an honest simulation of selection rather than a simulation of learning.
    /// Nothing in this struct ever nudges itself toward a better value. The only two ways a trait
    /// value enters the population are the initial spread and mutation at birth; the only way the
    /// POPULATION's trait distribution moves is that individuals with some values leave more
    /// living offspring than individuals with other values. See <see cref="EvolutionSim.Step"/>.
    /// </summary>
    public struct Individual
    {
        public bool Occupied;     // slot in use at all (alive, or a corpse still fading out)
        public bool Alive;        // biologically alive: counts toward population, can reproduce, can die
        public bool Dying;        // true for the short cosmetic fade after biological death
        public float Trait;       // fixed at birth; never modified
        public Vector3 Pos;       // local space, relative to the world anchor
        public Vector3 Vel;
        public float Age;         // seconds of simulated time since birth
        public float FadeT;       // 0..1 visual opacity: ramps up at birth, down while Dying
    }

    /// <summary>
    /// The population. Plain C#, no MonoBehaviour and no rendering — exactly the separation
    /// <c>OrbitalSim</c> uses, for the same reason: the biology has to be correct independent of
    /// anything Unity does with a frame, and it has to be readable by a renderer, a challenge and
    /// the world's gates without any of them being able to write into it directly.
    ///
    /// THE ONE RULE THIS CLASS EXISTS TO ENFORCE: nothing here ever sets an individual's Trait
    /// after it is born, and nothing here ever assigns MeanTrait. The population mean is read out
    /// of the living individuals every step; it is a CONSEQUENCE of who survived and who
    /// reproduced, never a target this class steers toward. If a future edit ever writes to
    /// MeanTrait or to Trait on a live individual, the simulation has stopped being honest.
    ///
    /// Three environment knobs are exposed as plain numbers (<see cref="Temperature"/>,
    /// <see cref="PredatorActive"/>, <see cref="ResourceRichness01"/>) and nothing else reaches
    /// in. The learner's hands move objects; the world converts those object positions into these
    /// numbers; this class never touches a hand, a transform hierarchy or a material.
    ///
    /// Fitness, precisely: an individual's EXPECTED offspring count per second is
    /// <c>BaseFecundity * ThermalMatch(trait, Temperature) * CrowdingFactor(N, K)</c> while it is
    /// alive, and it survives each step with probability <c>exp(-hazard*dt)</c>. Fitness is not
    /// tracked as a separate number anywhere — it does not need to be, because it is nothing more
    /// than the birth rate and death rate this class already computes. That absence is the point:
    /// fitness is a description of what the environment does to a trait value, not a property
    /// stamped on an organism.
    /// </summary>
    public class EvolutionSim
    {
        // ---- trait range -----------------------------------------------------
        public const float TraitMin = 0.15f;
        public const float TraitMax = 1.00f;
        public static float TraitToU01(float trait) => Mathf.InverseLerp(TraitMin, TraitMax, trait);
        public static float U01ToTrait(float u) => Mathf.Lerp(TraitMin, TraitMax, Mathf.Clamp01(u));

        // ---- population bookkeeping -------------------------------------------
        public const int Capacity = 96;
        // Deliberately kept BELOW CarryingCapacityLow (see below): starting the population above
        // the resource's baseline capacity would clamp the crowding factor to zero and suppress
        // every birth until enough deaths brought the count back down, which would make Wonder's
        // very first seconds show nothing but deaths and would look like the population dying
        // out before anyone had touched anything.
        public int InitialPopulation = 32;

        readonly Individual[] _pop = new Individual[Capacity];
        readonly int[] _free = new int[Capacity];
        int _freeCount;

        /// <summary>Direct read access for the renderer and the challenge. Do not write through this.</summary>
        public Individual[] Pop => _pop;

        // ---- terrain geometry, local to the world anchor -----------------------
        public static readonly Vector3 TerrainCentre = Vector3.zero;
        public const float TerrainRadius = 0.15f;

        // ---- environment, set by the world from hand-moved objects ------------
        /// <summary>0 = coldest, 1 = hottest. Set by the temperature dial.</summary>
        public float Temperature = 0.5f;
        /// <summary>Whether a predator currently sits inside the terrain and is hunting.</summary>
        public bool PredatorActive;
        /// <summary>Predator position, local space. Only meaningful while <see cref="PredatorActive"/>.</summary>
        public Vector3 PredatorLocalPos;
        /// <summary>0 = poorest supported carrying capacity, 1 = richest. Set from the resource token's position.</summary>
        public float ResourceRichness01;
        /// <summary>Multiplies simulated time. 1 normally; held above 1 while the learner grasps the time token.</summary>
        public float TimeScale = 1f;

        // ---- tuned constants ----------------------------------------------------
        // All rates are per SECOND of simulated time, so halving FixedStep changes resolution, not
        // outcome. Numbers were chosen to make a generation's worth of turnover visible within
        // roughly ten to twenty real seconds of sustained pressure at 1x — fast enough to hold
        // attention in a headset, slow enough that the shift reads as births-and-deaths rather
        // than a slider being animated. They are ordinary fields, not consts, so they are easy to
        // retune from the inspector without touching the logic that uses them.
        public const float FixedStep = 1f / 20f;

        public float MaturityAge      = 3.0f;
        public float BaseHazard       = 0.012f;
        public float AgeHazardSoft    = 32f;     // age at which old-age risk starts to matter
        public float AgeHazardSpan    = 22f;
        public float AgeHazardScale   = 0.55f;
        public float CrowdHazardScale = 0.12f;

        public float BaseFecundity    = 0.11f;
        public float ThermalSigma     = 0.16f;   // trait units; narrower = harsher selection
        public float MutationSigma    = 0.032f;  // trait units, applied once, at birth

        public float CarryingCapacityLow  = 44f;
        public float CarryingCapacityHigh = 86f;

        public float PredatorStrength   = 0.55f;
        public float PredatorSigma      = 0.13f; // trait units
        public float PredatorHuntRadius = 0.055f;

        public float OptimalTraitCold = 0.90f;   // the trait Temperature=0 favours reproductively
        public float OptimalTraitHot  = 0.20f;   // the trait Temperature=1 favours reproductively

        public float WanderJitter = 0.05f;
        public float WanderMaxSpeed = 0.035f;

        public float BirthFadeSeconds = 0.5f;
        public float DeathFadeSeconds = 0.4f;

        // ---- histogram ------------------------------------------------------
        public const int BinCount = 14;
        readonly int[] _bins = new int[BinCount];
        public IReadOnlyList<int> Bins => _bins;
        public float BinCentreTrait(int bin) => U01ToTrait((bin + 0.5f) / BinCount);

        // ---- aggregate stats, recomputed every fixed step -----------------------
        public int AliveCount { get; private set; }
        public float MeanTrait { get; private set; }
        public long TotalBirths { get; private set; }
        public long TotalDeaths { get; private set; }
        public long PredationDeaths { get; private set; }

        float _accumulator;

        public EvolutionSim()
        {
            for (int i = 0; i < Capacity; i++) _free[i] = Capacity - 1 - i;
            _freeCount = Capacity;
            Populate();
        }

        void Populate()
        {
            int n = Mathf.Clamp(InitialPopulation, 0, Capacity);
            for (int i = 0; i < n; i++)
            {
                int slot = TakeFreeSlot();
                if (slot < 0) break;

                float trait = Mathf.Clamp(
                    U01ToTrait(0.5f) + Gaussian(0f, (TraitMax - TraitMin) * 0.14f), TraitMin, TraitMax);

                float r = TerrainRadius * Mathf.Sqrt(Random.value);
                float a = Random.value * Mathf.PI * 2f;
                var pos = TerrainCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                _pop[slot] = new Individual
                {
                    Occupied = true,
                    Alive = true,
                    Dying = false,
                    Trait = trait,
                    Pos = pos,
                    Vel = Vector3.zero,
                    // Staggered ages so the first generation does not mature or die in lockstep.
                    Age = Random.Range(0f, MaturityAge * 3f),
                    FadeT = 1f
                };
            }
            RecomputeStats();
        }

        int TakeFreeSlot() => _freeCount > 0 ? _free[--_freeCount] : -1;
        void ReleaseSlot(int i) { _pop[i].Occupied = false; _free[_freeCount++] = i; }

        // -----------------------------------------------------------------
        // stepping
        // -----------------------------------------------------------------

        /// <summary>Advance by real elapsed time, in fixed biological steps. Mirrors OrbitalSim.Advance.</summary>
        public void Advance(float realDeltaSeconds)
        {
            _accumulator += realDeltaSeconds * Mathf.Max(0f, TimeScale);

            const int maxStepsPerFrame = 8;
            int budget = maxStepsPerFrame;
            while (_accumulator >= FixedStep && budget-- > 0)
            {
                Step(FixedStep);
                _accumulator -= FixedStep;
            }
            if (budget <= 0) _accumulator = 0f;
        }

        float CarryingCapacity() => Mathf.Lerp(CarryingCapacityLow, CarryingCapacityHigh,
                                               Mathf.Clamp01(ResourceRichness01));

        void Step(float dt)
        {
            float K = CarryingCapacity();
            int n = AliveCount;

            // Predation needs the LOCAL mean trait near the predator, computed once up front —
            // never the trait of the individual being judged against itself. This is what makes
            // it apostatic (search-image) predation: whoever sits near the crowd is at risk,
            // whoever is unusual is comparatively safe, regardless of which direction unusual is.
            float predMeanTrait = MeanTrait;
            if (PredatorActive)
            {
                float sum = 0f; int count = 0;
                float hr2 = PredatorHuntRadius * PredatorHuntRadius;
                for (int i = 0; i < Capacity; i++)
                {
                    if (!_pop[i].Occupied || !_pop[i].Alive) continue;
                    Vector3 d = _pop[i].Pos - PredatorLocalPos; d.y = 0f;
                    if (d.sqrMagnitude <= hr2) { sum += _pop[i].Trait; count++; }
                }
                if (count > 0) predMeanTrait = sum / count;
            }

            for (int i = 0; i < Capacity; i++)
            {
                if (!_pop[i].Occupied) continue;

                if (_pop[i].Dying)
                {
                    _pop[i].FadeT -= dt / Mathf.Max(DeathFadeSeconds, 0.01f);
                    if (_pop[i].FadeT <= 0f) ReleaseSlot(i);
                    continue;
                }

                Wander(ref _pop[i], dt);
                _pop[i].Age += dt;
                if (_pop[i].FadeT < 1f)
                    _pop[i].FadeT = Mathf.Min(1f, _pop[i].FadeT + dt / Mathf.Max(BirthFadeSeconds, 0.01f));

                // ---- mortality: background age risk, crowding, predation ----
                float hazard = BaseHazard + AgeHazard(_pop[i].Age) + CrowdHazard(n, K);

                bool predatorInRange = false;
                if (PredatorActive)
                {
                    Vector3 d = _pop[i].Pos - PredatorLocalPos; d.y = 0f;
                    predatorInRange = d.sqrMagnitude <= PredatorHuntRadius * PredatorHuntRadius;
                    if (predatorInRange)
                        hazard += PredatorStrength * GaussianKernel(_pop[i].Trait - predMeanTrait, PredatorSigma);
                }

                float pDeath = 1f - Mathf.Exp(-hazard * dt);
                if (Random.value < pDeath)
                {
                    _pop[i].Alive = false;
                    _pop[i].Dying = true;
                    _pop[i].FadeT = 1f;
                    TotalDeaths++;
                    if (predatorInRange) PredationDeaths++;
                    continue;
                }

                // ---- reproduction: the ONLY channel through which Temperature acts ----
                // Fitness, made concrete: how well this individual's fixed trait matches the
                // current environment sets how often it leaves an offspring. It never sets
                // whether the offspring's trait is "better" — that is decided after the fact, by
                // what happens to the offspring in turn.
                if (_pop[i].Age >= MaturityAge && _freeCount > 0)
                {
                    float match = ThermalMatch(_pop[i].Trait, Temperature);
                    float crowd = Mathf.Clamp01(1f - n / Mathf.Max(K, 1f));
                    float lambda = BaseFecundity * match * crowd;
                    float pBirth = 1f - Mathf.Exp(-lambda * dt);
                    if (Random.value < pBirth) Spawn(i);
                }
            }

            RecomputeStats();
        }

        void Spawn(int parentIndex)
        {
            int slot = TakeFreeSlot();
            if (slot < 0) return;

            Individual parent = _pop[parentIndex];
            float trait = Mathf.Clamp(parent.Trait + Gaussian(0f, MutationSigma), TraitMin, TraitMax);

            Vector3 offset = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * 0.02f;
            Vector3 pos = ClampToTerrain(parent.Pos + offset);

            _pop[slot] = new Individual
            {
                Occupied = true,
                Alive = true,
                Dying = false,
                Trait = trait,
                Pos = pos,
                Vel = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * (WanderMaxSpeed * 0.4f),
                Age = 0f,
                FadeT = 0f
            };
            TotalBirths++;
        }

        void Wander(ref Individual ind, float dt)
        {
            ind.Vel += new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * (WanderJitter * dt);
            float sp2 = ind.Vel.sqrMagnitude;
            if (sp2 > WanderMaxSpeed * WanderMaxSpeed) ind.Vel = ind.Vel * (WanderMaxSpeed / Mathf.Sqrt(sp2));

            ind.Pos += ind.Vel * dt;

            Vector3 off = ind.Pos - TerrainCentre; off.y = 0f;
            float d = off.magnitude;
            if (d > TerrainRadius)
            {
                Vector3 nrm = off / Mathf.Max(d, 1e-5f);
                ind.Pos = TerrainCentre + nrm * TerrainRadius;
                ind.Vel -= 2f * Vector3.Dot(ind.Vel, nrm) * nrm;
                ind.Vel *= 0.5f;
            }
        }

        Vector3 ClampToTerrain(Vector3 p)
        {
            Vector3 off = p - TerrainCentre; off.y = 0f;
            float d = off.magnitude;
            return d <= TerrainRadius ? p : TerrainCentre + off.normalized * TerrainRadius;
        }

        void RecomputeStats()
        {
            int n = 0; float sum = 0f;
            // BinCount, not Capacity. _bins is the histogram (14 entries); Capacity is the
            // population array (96). Clearing to Capacity wrote 82 elements past the end of the
            // array every single frame — 18,401 IndexOutOfRangeExceptions in one capture run.
            for (int i = 0; i < BinCount; i++) _bins[i] = 0;

            for (int i = 0; i < Capacity; i++)
            {
                if (!_pop[i].Occupied || !_pop[i].Alive) continue;
                n++;
                sum += _pop[i].Trait;
                int b = Mathf.Clamp(Mathf.FloorToInt(TraitToU01(_pop[i].Trait) * BinCount), 0, BinCount - 1);
                _bins[b]++;
            }

            AliveCount = n;
            MeanTrait = n > 0 ? sum / n : U01ToTrait(0.5f);
        }

        // ---- fitness-shaping functions, pure math -----------------------------

        public float OptimalTraitAt(float temperature) => Mathf.Lerp(OptimalTraitCold, OptimalTraitHot,
                                                                       Mathf.Clamp01(temperature));

        float ThermalMatch(float trait, float temperature) =>
            GaussianKernel(trait - OptimalTraitAt(temperature), ThermalSigma);

        float AgeHazard(float age)
        {
            float excess = Mathf.Max(0f, age - AgeHazardSoft) / Mathf.Max(AgeHazardSpan, 0.01f);
            return AgeHazardScale * excess * excess;
        }

        float CrowdHazard(int n, float k) => CrowdHazardScale * Mathf.Max(0f, n / Mathf.Max(k, 1f) - 1f);

        static float GaussianKernel(float x, float sigma)
        {
            float s = Mathf.Max(sigma, 1e-4f);
            float t = x / s;
            return Mathf.Exp(-0.5f * t * t);
        }

        /// <summary>Box-Muller normal sample. Used only for the initial spread and for mutation at birth.</summary>
        static float Gaussian(float mean, float sigma)
        {
            float u1 = Mathf.Max(Random.value, 1e-6f);
            float u2 = Random.value;
            float z = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
            return mean + z * sigma;
        }
    }
}
