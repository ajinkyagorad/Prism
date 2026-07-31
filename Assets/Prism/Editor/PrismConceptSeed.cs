using System.Collections.Generic;
using System.IO;
using Prism.Core;
using UnityEditor;
using UnityEngine;

namespace Prism.EditorTools
{
    /// <summary>
    /// Authors the starting constellation as assets.
    ///
    /// This is the neighbourhood around orbital mechanics, plus a handful of deliberately distant
    /// stars in domains this build cannot teach yet. The distant ones are not filler: the product's
    /// central claim is that knowledge is one connected thing, and a learner who finishes orbital
    /// mechanics should be able to see a gold analogy line running off toward rhythm and music and
    /// wonder what it is doing there.
    ///
    /// One node is a Question rather than a fact — "why doesn't the Moon fall down?" — which
    /// renders as an iridescent void and bends the concepts near it out of shape. It is the oldest
    /// and best question in this subject, and it should be visibly unanswered until it is answered.
    ///
    ///   PRISM &gt; 2. Seed Concept Graph
    /// </summary>
    public static class PrismConceptSeed
    {
        const string Dir = "Assets/Prism/Content";
        const string GraphPath = Dir + "/ConceptGraph.asset";

        [MenuItem("PRISM/2. Seed Concept Graph", priority = 110)]
        public static void Seed()
        {
            Directory.CreateDirectory(Dir);

            var defs = new List<ConceptDefinition>();

            // ---- the taught neighbourhood -------------------------------------------------
            var orbital = Make(defs, "orbital-mechanics", "Orbital Mechanics",
                ConceptDomain.SpaceAndTime, KnowledgeKind.System,
                new Vector3(0f, -0.05f, 1f), 0.95f, worldId: "orbital",
                capability: "Put a moon into the orbit you meant to, and say why it stays there.",
                formalisation: "A body in free fall around a mass follows a conic section whose " +
                               "shape is fixed by two conserved quantities: energy and angular momentum.");

            var gravity = Make(defs, "gravity", "Gravity",
                ConceptDomain.MatterAndEnergy, KnowledgeKind.Theory,
                new Vector3(-0.55f, 0.10f, 0.85f), 1.15f,
                capability: "Predict which way something will accelerate, and how strongly.",
                formalisation: "Every mass attracts every other along the line between them, as the " +
                               "inverse square of the distance.");

            var inverseSquare = Make(defs, "inverse-square", "Inverse Square Law",
                ConceptDomain.Mathematics, KnowledgeKind.Equation,
                new Vector3(-0.95f, 0.35f, 0.45f), 1.45f,
                capability: "Recognise the same 1/r^2 shape wherever something spreads out from a point.",
                formalisation: "A quantity radiating from a point spreads over a sphere of area " +
                               "4*pi*r^2, so its intensity falls as 1/r^2.");

            var conics = Make(defs, "conic-sections", "Conic Sections",
                ConceptDomain.Mathematics, KnowledgeKind.Equation,
                new Vector3(0.70f, 0.25f, 0.80f), 1.20f,
                capability: "Name a curve from its eccentricity, and draw it from its focus.",
                formalisation: "r = p / (1 + e*cos(theta)). One equation; circle, ellipse, parabola " +
                               "and hyperbola are just ranges of e.");

            var energy = Make(defs, "energy-conservation", "Conservation of Energy",
                ConceptDomain.MatterAndEnergy, KnowledgeKind.Theory,
                new Vector3(0.35f, 0.55f, 0.75f), 1.30f,
                capability: "Decide whether something can ever get where it is trying to go.",
                formalisation: "Kinetic plus potential energy is constant in a closed system. " +
                               "Its SIGN decides whether a path is bound or unbound.");

            var angular = Make(defs, "angular-momentum", "Angular Momentum",
                ConceptDomain.MatterAndEnergy, KnowledgeKind.Theory,
                new Vector3(0.85f, -0.20f, 0.50f), 1.25f,
                capability: "Explain why something speeds up as it comes closer in.",
                formalisation: "r x v is constant under a central force. Sweeping equal areas in " +
                               "equal times is the same statement.");

            var kepler = Make(defs, "kepler-laws", "Kepler's Laws",
                ConceptDomain.SpaceAndTime, KnowledgeKind.Equation,
                new Vector3(0.30f, -0.45f, 0.85f), 1.10f,
                capability: "Get a period from an orbit's size without simulating it.",
                formalisation: "T^2 proportional to a^3. Kepler found it in data; it falls out of " +
                               "the inverse square law.");

            var escape = Make(defs, "escape-velocity", "Escape Velocity",
                ConceptDomain.SpaceAndTime, KnowledgeKind.Fact,
                new Vector3(-0.30f, -0.50f, 0.80f), 1.00f,
                capability: "Know, before you let go, whether it is coming back.",
                formalisation: "v = sqrt(2*mu/r). The speed at which kinetic energy exactly cancels " +
                               "the potential well.");

            var vectors = Make(defs, "vectors", "Vectors",
                ConceptDomain.Mathematics, KnowledgeKind.Skill,
                new Vector3(-0.80f, -0.30f, 0.55f), 1.05f,
                capability: "Take hold of a quantity that has a direction and change it deliberately.",
                formalisation: "A quantity with magnitude and direction; adds tip to tail.");

            var periodic = Make(defs, "periodic-motion", "Periodic Motion",
                ConceptDomain.MatterAndEnergy, KnowledgeKind.Process,
                new Vector3(0.95f, 0.45f, 0.15f), 1.40f,
                capability: "Spot a thing that will come back around, and say when.",
                formalisation: "Motion that repeats with a fixed period. Orbits, pendulums, springs " +
                               "and waves are the same statement in different materials.");

            // ---- the open question -------------------------------------------------------
            var question = Make(defs, "why-orbits-persist", "Why doesn't the Moon fall down?",
                ConceptDomain.SpaceAndTime, KnowledgeKind.Question,
                new Vector3(-0.15f, 0.60f, 0.75f), 0.85f,
                capability: "",
                formalisation: "It is falling. It keeps missing.");

            // ---- distant domains, not taught in this build --------------------------------
            var waves = Make(defs, "waves", "Waves",
                ConceptDomain.MatterAndEnergy, KnowledgeKind.Process,
                new Vector3(1.0f, 0.15f, -0.55f), 2.30f,
                capability: "See the same shape in light, sound, water and matter.");

            var rhythm = Make(defs, "rhythm", "Rhythm",
                ConceptDomain.ArtAndMusic, KnowledgeKind.Process,
                new Vector3(0.55f, -0.35f, -0.85f), 2.60f,
                capability: "Feel a period rather than count it.");

            var tides = Make(defs, "tides", "Tides",
                ConceptDomain.EarthAndCiv, KnowledgeKind.System,
                new Vector3(-0.65f, -0.15f, -0.80f), 2.45f,
                capability: "Explain why there are two high tides a day, not one.");

            var symmetry = Make(defs, "symmetry", "Symmetry",
                ConceptDomain.Mathematics, KnowledgeKind.Theory,
                new Vector3(-1.0f, 0.55f, -0.35f), 2.75f,
                capability: "Find the thing that does not change, and get a conservation law free.");

            var probability = Make(defs, "probability", "Probability",
                ConceptDomain.Mathematics, KnowledgeKind.Uncertainty,
                new Vector3(-0.35f, -0.70f, -0.70f), 2.55f,
                capability: "Reason about what you cannot pin down.");

            var feedback = Make(defs, "feedback", "Feedback",
                ConceptDomain.Machines, KnowledgeKind.Theory,
                new Vector3(0.15f, 0.75f, -0.80f), 2.85f,
                capability: "Recognise a loop that stabilises, and one that runs away.");

            // ---- relationships -----------------------------------------------------------
            Link(orbital, gravity,       Relation.Causes,       0.95f);
            Link(orbital, conics,        Relation.Constrains,   0.90f);
            Link(orbital, energy,        Relation.Constrains,   0.85f);
            Link(orbital, angular,       Relation.Constrains,   0.85f);
            Link(orbital, kepler,        Relation.Measures,     0.80f);
            Link(orbital, escape,        Relation.Composes,     0.75f);
            Link(orbital, vectors,       Relation.Prerequisite, 0.70f);
            Link(orbital, periodic,      Relation.Instantiates, 0.80f);

            Link(gravity, inverseSquare, Relation.Instantiates, 0.90f);
            Link(kepler,  inverseSquare, Relation.Causes,       0.70f);
            Link(escape,  energy,        Relation.Instantiates, 0.85f);
            Link(angular, symmetry,      Relation.Causes,       0.65f);
            Link(energy,  symmetry,      Relation.Causes,       0.65f);

            // The question hangs off the things that answer it.
            Link(question, orbital,      Relation.Measures,     0.60f);
            Link(question, gravity,      Relation.Measures,     0.60f);

            // The long analogies — the lines a learner should notice and wonder about.
            Link(periodic, waves,        Relation.Analogy,      0.85f);
            Link(periodic, rhythm,       Relation.Analogy,      0.75f);
            Link(gravity,  tides,        Relation.Causes,       0.80f);
            Link(inverseSquare, waves,   Relation.Analogy,      0.60f);
            Link(symmetry, feedback,     Relation.Analogy,      0.45f);
            Link(probability, waves,     Relation.Analogy,      0.40f);

            // ---- concepts contributed by world modules -------------------------------------
            //
            // Ten worlds were built in parallel by agents forbidden from editing this file, so each
            // declares its own concepts as data and they are merged here. A world that declares a
            // concept whose id already exists is skipped with a warning rather than silently
            // overwriting — an id collision between two independently built modules is a real
            // authoring bug and must be visible.
            var byId = new Dictionary<string, ConceptDefinition>();
            foreach (var d in defs) byId[d.id] = d;

            var worldSpecs = Prism.Worlds.WorldRegistry.AllWorldConcepts();
            var pendingLinks = new List<(string from, string target, Relation rel, float strength)>();

            foreach (var spec in worldSpecs)
            {
                if (byId.ContainsKey(spec.Id))
                {
                    Debug.LogWarning($"[PRISM] Concept id collision: '{spec.Id}' declared by a world " +
                                     "module already exists. Keeping the first and skipping the duplicate.");
                    continue;
                }

                var dir = spec.Direction.sqrMagnitude > 1e-6f ? spec.Direction.normalized : Vector3.forward;
                var d = Make(defs, spec.Id, string.IsNullOrEmpty(spec.Title) ? spec.Id : spec.Title,
                             spec.Domain, spec.Kind, dir, Mathf.Max(0.5f, spec.Distance),
                             spec.WorldId ?? "", spec.Capability ?? "", spec.Formalisation ?? "");
                byId[spec.Id] = d;

                if (spec.Links != null)
                    foreach (var l in spec.Links)
                        if (!string.IsNullOrEmpty(l.target))
                            pendingLinks.Add((spec.Id, l.target, l.relation, l.strength));
            }

            // Links are resolved after every concept exists, so a world may link to another world's
            // concept regardless of which was inspected first.
            int linked = 0, dropped = 0;
            foreach (var (from, target, rel, strength) in pendingLinks)
            {
                if (!byId.TryGetValue(from, out var a)) { dropped++; continue; }
                if (!byId.ContainsKey(target))
                {
                    Debug.LogWarning($"[PRISM] '{from}' links to '{target}', which no module declares. Dropped.");
                    dropped++;
                    continue;
                }
                a.links.Add(new ConceptLink(target, rel, Mathf.Clamp01(strength <= 0f ? 0.6f : strength)));
                linked++;
            }

            Debug.Log($"[PRISM] World modules contributed {worldSpecs.Count} concepts, " +
                      $"{linked} links ({dropped} dropped).");

            // ---- write it out ------------------------------------------------------------
            foreach (var d in defs)
            {
                var path = $"{Dir}/{d.id}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<ConceptDefinition>(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(d, existing);
                    EditorUtility.SetDirty(existing);
                }
                else AssetDatabase.CreateAsset(d, path);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Re-resolve to the on-disk instances so the graph references assets, not temporaries.
            var graph = AssetDatabase.LoadAssetAtPath<ConceptGraph>(GraphPath);
            if (graph == null)
            {
                graph = ScriptableObject.CreateInstance<ConceptGraph>();
                AssetDatabase.CreateAsset(graph, GraphPath);
            }
            graph.concepts.Clear();
            foreach (var d in defs)
                graph.concepts.Add(AssetDatabase.LoadAssetAtPath<ConceptDefinition>($"{Dir}/{d.id}.asset"));

            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();

            var problems = graph.Validate();
            if (problems.Count == 0)
                Debug.Log($"[PRISM] Concept graph seeded: {graph.concepts.Count} concepts, validated clean.");
            else
                foreach (var p in problems) Debug.LogError("[PRISM] Graph problem: " + p);
        }

        public static void SeedFromCommandLine()
        {
            Seed();
            EditorApplication.Exit(0);
        }

        // -----------------------------------------------------------------

        static ConceptDefinition Make(List<ConceptDefinition> into, string id, string title,
                                      ConceptDomain domain, KnowledgeKind kind,
                                      Vector3 direction, float distance,
                                      string worldId = "", string capability = "",
                                      string formalisation = "")
        {
            var d = ScriptableObject.CreateInstance<ConceptDefinition>();
            d.id = id;
            d.title = title;
            d.domain = domain;
            d.kind = kind;
            d.direction = direction.normalized;
            d.distance = distance;
            d.worldId = worldId;
            d.capability = capability;
            d.formalisation = formalisation;
            d.links = new List<ConceptLink>();
            into.Add(d);
            return d;
        }

        static void Link(ConceptDefinition from, ConceptDefinition to, Relation relation, float strength)
        {
            from.links.Add(new ConceptLink(to.id, relation, strength));
        }
    }
}
