using System.Collections.Generic;
using UnityEngine;

namespace Prism.Worlds.Civilization
{
    /// <summary>The four generic goods. Order is fixed and doubles as an array index everywhere.</summary>
    public enum Good { Grain = 0, Ore = 1, Cloth = 2, Salt = 3 }

    public static class Goods
    {
        public const int Count = 4;

        public static string Name(Good g)
        {
            switch (g)
            {
                case Good.Grain: return "grain";
                case Good.Ore:   return "ore";
                case Good.Cloth: return "cloth";
                default:         return "salt";
            }
        }
    }

    /// <summary>
    /// One settlement: real stocks, a fixed production and a fixed baseline need, and a price
    /// that emerges from its own stock. There is no population here and no culture — just a
    /// store of four goods with a rate in and a rate out, which is deliberate; see NOTES.md.
    /// </summary>
    public class Settlement
    {
        public string Id;
        public int Index;
        public Vector3 LocalPosition;

        /// <summary>Units/second. Fixed geography — never changed by trade or by the learner.</summary>
        public readonly float[] Production = new float[Goods.Count];
        /// <summary>Units/second desired. Fixed baseline need, the same shape for every settlement.</summary>
        public readonly float[] Consumption = new float[Goods.Count];
        /// <summary>Units on hand right now. The only one of these a route can actually move.</summary>
        public readonly float[] Stock = new float[Goods.Count];
        /// <summary>What was actually consumed last step. <=Consumption; short when stock ran out.</summary>
        public readonly float[] ActualConsumption = new float[Goods.Count];
        /// <summary>Derived from Stock every step. Never set directly.</summary>
        public readonly float[] Price = new float[Goods.Count];

        /// <summary>0..1: how content this one good is. 1 = fully met from stock.</summary>
        public float Satisfaction(Good g) =>
            Mathf.Clamp01(ActualConsumption[(int)g] / Mathf.Max(Consumption[(int)g], 1e-4f));

        public float AvgSatisfaction
        {
            get
            {
                float s = 0f;
                for (int i = 0; i < Goods.Count; i++) s += Satisfaction((Good)i);
                return s / Goods.Count;
            }
        }

        /// <summary>-1 (empty) .. +1 (overflowing) relative to TradeSim.ReferenceStock.</summary>
        public float SurplusOf(Good g) =>
            Mathf.Clamp((Stock[(int)g] - TradeSim.ReferenceStock) / TradeSim.ReferenceStock, -1f, 1f);

        /// <summary>
        /// The single number the settlement's colour is made from: the average, over all four
        /// goods, of how far each stock sits from the reference level. See CivilizationWorld for
        /// the colour law this drives.
        /// </summary>
        public float SurplusIndex
        {
            get
            {
                float s = 0f;
                for (int i = 0; i < Goods.Count; i++) s += SurplusOf((Good)i);
                return s / Goods.Count;
            }
        }

        /// <summary>
        /// An emergent size, 0.3..2.5, integrated from sustained satisfaction alone — never set
        /// directly, never scripted. This is what "grows" in this world.
        /// </summary>
        public float Prosperity = 1f;
    }

    /// <summary>
    /// A trade route: the learner's own drawn path between two settlements. The recorded path IS
    /// the distance cost — a direct line moves goods more cheaply than a wandering one, so the
    /// gesture that creates the route is also the gesture that prices it.
    /// </summary>
    public class Route
    {
        public Settlement A;
        public Settlement B;
        public readonly List<Vector3> Path = new List<Vector3>();
        public float Length;

        /// <summary>Units/second, signed: positive moves A-&gt;B, negative moves B-&gt;A.</summary>
        public readonly float[] Flow = new float[Goods.Count];

        public float Age;
        public float FlourishFor;
        public float StarvedFor;

        public bool IsFlourishing => FlourishFor >= TradeSim.FlourishSustain;
        public bool IsStarved => StarvedFor >= TradeSim.StarvedSustain;

        public float TotalFlowMagnitude
        {
            get
            {
                float m = 0f;
                for (int i = 0; i < Goods.Count; i++) m += Mathf.Abs(Flow[i]);
                return m;
            }
        }

        /// <summary>Replace the path (initial draw, or a redraw over an existing route).</summary>
        public void SetPath(IList<Vector3> points)
        {
            Path.Clear();
            Path.AddRange(points);
            Length = 0f;
            for (int i = 1; i < Path.Count; i++) Length += Vector3.Distance(Path[i - 1], Path[i]);
            Age = 0f;
            FlourishFor = 0f;
            StarvedFor = 0f;
            for (int i = 0; i < Goods.Count; i++) Flow[i] = 0f;
        }
    }

    /// <summary>
    /// The trade network. Real stocks and flows, a transport cost that genuinely rises with
    /// path length, and exchange driven by an actual price differential between two real
    /// settlements. Plain C#, no MonoBehaviour — the economics can be reasoned about, and
    /// eventually tested, apart from any rendering, the same discipline OrbitalSim uses.
    ///
    /// Nothing here allocates once the network is built: Step() only ever writes into arrays and
    /// lists that already exist, so CivilizationWorld.Tick can call Advance() every frame for
    /// free.
    ///
    /// See NOTES.md for the hand-worked check that the production numbers chosen in
    /// CivilizationWorld actually produce comparative advantage rather than merely asserting it.
    /// </summary>
    public class TradeSim
    {
        /// <summary>"Comfortable" stock level. Price equals BasePrice exactly here.</summary>
        public const float ReferenceStock = 8f;
        const float Elasticity = 1f;
        const float BasePrice = 1f;
        const float PriceMin = 0.15f;
        const float PriceMax = 6f;

        /// <summary>Price-units of cost per metre of drawn path. Tuned for legibility at table
        /// scale, not derived — see NOTES.md.</summary>
        public const float TransportCostPerMetre = 4.0f;
        const float TradeRateConstant = 1.2f;
        /// <summary>A route can never move more than this fraction of a settlement's stock in one step.</summary>
        const float MaxFractionPerTick = 0.4f;

        const float ProsperityGrowthRate = 0.05f;
        const float ProsperityThreshold = 0.5f;
        /// <summary>Public so the view layer can normalise Prosperity into a 0..1 growth signal.</summary>
        public const float ProsperityMin = 0.3f;
        public const float ProsperityMax = 2.5f;

        public const float FlourishThreshold = 0.35f;
        public const float StarvedThreshold = 0.05f;
        public const float FlourishSustain = 1.2f;
        public const float StarvedSustain = 1.2f;
        public const float MinAgeForJudgement = 1.5f;

        public const float FixedStep = 1f / 20f;

        public readonly List<Settlement> Settlements = new List<Settlement>();
        public readonly List<Route> Routes = new List<Route>();

        float _accumulator;
        public double Time { get; private set; }

        /// <summary>Advance by real elapsed time. Leftover time carries; a hitch never spends
        /// hundreds of catch-up steps.</summary>
        public void Advance(float realDeltaSeconds)
        {
            _accumulator += realDeltaSeconds;
            const int maxStepsPerFrame = 8;
            int budget = maxStepsPerFrame;
            while (_accumulator >= FixedStep && budget-- > 0)
            {
                Step(FixedStep);
                _accumulator -= FixedStep;
            }
            if (budget <= 0) _accumulator = 0f;
        }

        void Step(float dt)
        {
            // 1. Native production and baseline consumption: the harvest happens regardless of
            // trade, and need is drawn down honestly from whatever stock actually exists.
            for (int i = 0; i < Settlements.Count; i++)
            {
                var s = Settlements[i];
                for (int g = 0; g < Goods.Count; g++)
                {
                    s.Stock[g] += s.Production[g] * dt;
                    float desired = s.Consumption[g] * dt;
                    float actual = Mathf.Min(desired, s.Stock[g]);
                    s.Stock[g] -= actual;
                    s.ActualConsumption[g] = actual / dt;
                }
                RecomputePrices(s);
            }

            // 2. Trade. For every route, for every good independently, goods move from whichever
            // settlement is cheaper toward whichever is dearer — arbitrage, not a rule the
            // learner is told — at a rate set by what is left of the gap once the cost of the
            // drawn path is paid.
            for (int r = 0; r < Routes.Count; r++)
            {
                var route = Routes[r];
                route.Age += dt;
                float transportCost = TransportCostPerMetre * route.Length;

                for (int g = 0; g < Goods.Count; g++)
                {
                    float priceA = route.A.Price[g];
                    float priceB = route.B.Price[g];
                    float gap = Mathf.Abs(priceA - priceB);
                    float effective = gap - transportCost;

                    if (effective <= 0f)
                    {
                        route.Flow[g] = 0f;
                        continue;
                    }

                    bool aIsCheaper = priceA < priceB;
                    Settlement source = aIsCheaper ? route.A : route.B;
                    Settlement dest   = aIsCheaper ? route.B : route.A;

                    float amount = TradeRateConstant * effective * dt;
                    amount = Mathf.Clamp(amount, 0f, source.Stock[g] * MaxFractionPerTick);

                    source.Stock[g] -= amount;
                    dest.Stock[g]   += amount;

                    float signedRate = amount / dt;
                    route.Flow[g] = aIsCheaper ? signedRate : -signedRate; // positive = A->B
                }

                float mag = route.TotalFlowMagnitude;
                route.FlourishFor = mag > FlourishThreshold ? route.FlourishFor + dt : 0f;
                route.StarvedFor  = (mag < StarvedThreshold && route.Age > MinAgeForJudgement)
                                   ? route.StarvedFor + dt : 0f;
            }

            // 3. Prices catch up with whatever trade just did, and prosperity — the one quantity
            // the learner watches grow — integrates from how well-fed each settlement has
            // actually been. This is the only place Prosperity is touched anywhere in the world.
            for (int i = 0; i < Settlements.Count; i++)
            {
                var s = Settlements[i];
                RecomputePrices(s);
                float dProsperity = ProsperityGrowthRate * (s.AvgSatisfaction - ProsperityThreshold) * dt;
                s.Prosperity = Mathf.Clamp(s.Prosperity + dProsperity, ProsperityMin, ProsperityMax);
            }

            Time += dt;
        }

        static void RecomputePrices(Settlement s)
        {
            for (int g = 0; g < Goods.Count; g++)
            {
                float stock = Mathf.Max(s.Stock[g], 0.5f);
                float p = BasePrice * Mathf.Pow(ReferenceStock / stock, Elasticity);
                s.Price[g] = Mathf.Clamp(p, PriceMin, PriceMax);
            }
        }

        public Settlement AddSettlement(string id, Vector3 localPos, float[] production, float[] consumption)
        {
            var s = new Settlement { Id = id, Index = Settlements.Count, LocalPosition = localPos };
            for (int g = 0; g < Goods.Count; g++)
            {
                s.Production[g] = production[g];
                s.Consumption[g] = consumption[g];
                // Seeded as if the imbalance had already been accumulating for a while, so the
                // very first frame already shows real (not zero) surplus and shortage. See NOTES.md.
                s.Stock[g] = Mathf.Clamp(ReferenceStock + 15f * (production[g] - consumption[g]), 1f, 30f);
            }
            RecomputePrices(s);
            Settlements.Add(s);
            return s;
        }

        public Route FindRoute(Settlement a, Settlement b)
        {
            for (int i = 0; i < Routes.Count; i++)
            {
                var r = Routes[i];
                if ((r.A == a && r.B == b) || (r.A == b && r.B == a)) return r;
            }
            return null;
        }

        /// <summary>Network-wide average of every settlement's own AvgSatisfaction.</summary>
        public float NetworkAvgSatisfaction()
        {
            if (Settlements.Count == 0) return 0f;
            float s = 0f;
            for (int i = 0; i < Settlements.Count; i++) s += Settlements[i].AvgSatisfaction;
            return s / Settlements.Count;
        }

        /// <summary>How many distinct routes currently touch this settlement.</summary>
        public int RouteDegree(Settlement s)
        {
            int n = 0;
            for (int i = 0; i < Routes.Count; i++)
                if (Routes[i].A == s || Routes[i].B == s) n++;
            return n;
        }
    }
}
