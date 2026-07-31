using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Demos
{
    /// <summary>
    /// Which demonstration belongs to which concept.
    ///
    /// Kept as one explicit table rather than reflection or naming conventions, because the mapping
    /// is a pedagogical decision and should be reviewable in one screen. A concept with no entry
    /// gets <see cref="LatentDemo"/>, which responds to the hand but does not pretend to teach —
    /// an honest gap is better than a decorative animation implying a lesson that is not there.
    /// </summary>
    public static class ConceptDemoRegistry
    {
        static readonly Dictionary<string, Type> Map = new Dictionary<string, Type>
        {
            { "orbital-mechanics",  typeof(OrbitalMiniDemo) },
            { "gravity",            typeof(GravityDemo) },
            { "inverse-square",     typeof(InverseSquareDemo) },
            { "conic-sections",     typeof(ConicDemo) },
            { "energy-conservation",typeof(EnergyDemo) },
            { "angular-momentum",   typeof(AngularMomentumDemo) },
            { "kepler-laws",        typeof(KeplerDemo) },
            { "escape-velocity",    typeof(EscapeVelocityDemo) },
            { "vectors",            typeof(VectorsDemo) },
            { "periodic-motion",    typeof(PeriodicMotionDemo) },
            { "why-orbits-persist", typeof(CannonballDemo) },
            { "waves",              typeof(WaveDemo) },
            { "rhythm",             typeof(RhythmDemo) },
            { "tides",              typeof(TidesDemo) },
            { "symmetry",           typeof(SymmetryDemo) },
            { "probability",        typeof(ProbabilityDemo) },
            { "feedback",           typeof(FeedbackDemo) },
        };

        /// <summary>
        /// The core table above, PLUS every demo a world module declared with
        /// [ConceptDemoFor]. Discovered once by reflection so module authors can add
        /// demonstrations inside their own folders without editing this shared file — the same
        /// reason WorldRegistry discovers worlds rather than listing them.
        /// </summary>
        static Dictionary<string, Type> Resolved
        {
            get
            {
                if (_resolved != null) return _resolved;
                _resolved = new Dictionary<string, Type>(Map);

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var n = asm.GetName().Name;
                    if (!n.StartsWith("Assembly-CSharp") && !n.StartsWith("Prism")) continue;

                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                    foreach (var t in types)
                    {
                        if (t == null || t.IsAbstract || !typeof(ConceptDemo).IsAssignableFrom(t)) continue;
                        foreach (ConceptDemoForAttribute a in
                                 t.GetCustomAttributes(typeof(ConceptDemoForAttribute), false))
                        {
                            if (string.IsNullOrEmpty(a.ConceptId)) continue;
                            if (_resolved.ContainsKey(a.ConceptId))
                            {
                                Debug.LogWarning($"[PRISM] Two demonstrations claim concept " +
                                                 $"'{a.ConceptId}'. Keeping the first.");
                                continue;
                            }
                            _resolved[a.ConceptId] = t;
                        }
                    }
                }
                return _resolved;
            }
        }
        static Dictionary<string, Type> _resolved;

        public static bool HasBespokeDemo(string conceptId) =>
            !string.IsNullOrEmpty(conceptId) && Resolved.ContainsKey(conceptId);

        public static int BespokeCount => Resolved.Count;

        public static IEnumerable<string> CoveredConcepts => Resolved.Keys;

        /// <summary>Spawn the demonstration for a concept, parented under <paramref name="parent"/>.</summary>
        public static ConceptDemo Create(ConceptDefinition concept, Transform parent,
                                         Transform carrier, PrismHands hands,
                                         PrismHands.Hand holder, Camera head,
                                         Companion.PrismVoice voice)
        {
            if (concept == null) return null;

            Type t = Resolved.TryGetValue(concept.id, out var found) ? found : typeof(LatentDemo);

            var go = new GameObject($"Demo_{concept.id}");
            go.transform.SetParent(parent, false);

            var demo = (ConceptDemo)go.AddComponent(t);
            demo.Concept = concept;
            demo.Carrier = carrier;
            demo.Hands = hands;
            demo.Holder = holder;
            demo.Head = head;
            demo.Voice = voice;
            return demo;
        }
    }
}
