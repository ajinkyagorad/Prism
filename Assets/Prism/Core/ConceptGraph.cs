using System.Collections.Generic;
using UnityEngine;

namespace Prism.Core
{
    /// <summary>
    /// The whole constellation. An asset rather than a scene, so worlds can be shipped
    /// separately and still find each other.
    /// </summary>
    [CreateAssetMenu(menuName = "PRISM/Concept Graph", fileName = "ConceptGraph")]
    public class ConceptGraph : ScriptableObject
    {
        public List<ConceptDefinition> concepts = new List<ConceptDefinition>();

        Dictionary<string, ConceptDefinition> _byId;

        public void Rebuild()
        {
            _byId = new Dictionary<string, ConceptDefinition>(concepts.Count);
            foreach (var c in concepts)
            {
                if (c == null || string.IsNullOrEmpty(c.id)) continue;
                if (_byId.ContainsKey(c.id))
                {
                    Debug.LogWarning($"[PRISM] Duplicate concept id '{c.id}' in graph '{name}'. " +
                                     "Progress is keyed on id, so duplicates share a learner's mastery.");
                    continue;
                }
                _byId[c.id] = c;
            }
        }

        public ConceptDefinition Find(string id)
        {
            if (_byId == null) Rebuild();
            return (id != null && _byId.TryGetValue(id, out var c)) ? c : null;
        }

        public IReadOnlyList<ConceptDefinition> All
        {
            get { if (_byId == null) Rebuild(); return concepts; }
        }

        /// <summary>
        /// Links are authored one-way but the constellation is undirected: pulling B toward A
        /// must reveal the same relationship as pulling A toward B. This resolves both.
        /// </summary>
        public bool TryGetRelation(string aId, string bId, out ConceptLink link)
        {
            var a = Find(aId);
            if (a != null)
                foreach (var l in a.links)
                    if (l.targetId == bId) { link = l; return true; }

            var b = Find(bId);
            if (b != null)
                foreach (var l in b.links)
                    if (l.targetId == aId) { link = l; return true; }

            link = default;
            return false;
        }

        public IEnumerable<ConceptDefinition> Neighbours(string id)
        {
            var c = Find(id);
            if (c == null) yield break;

            var seen = new HashSet<string>();
            foreach (var l in c.links)
            {
                var t = Find(l.targetId);
                if (t != null && seen.Add(t.id)) yield return t;
            }
            // Inbound links count as neighbours too.
            foreach (var other in All)
            {
                if (other == null || other.id == id || seen.Contains(other.id)) continue;
                foreach (var l in other.links)
                    if (l.targetId == id) { seen.Add(other.id); yield return other; break; }
            }
        }

        /// <summary>
        /// Sanity check for authoring. Returns a human-readable list of problems, empty if clean.
        /// Dangling link targets are the failure that silently produces a constellation with
        /// invisible connections, so this is worth running in CI.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            Rebuild();
            foreach (var c in concepts)
            {
                if (c == null) { problems.Add("Null entry in concepts list."); continue; }
                if (string.IsNullOrEmpty(c.id)) { problems.Add($"'{c.name}' has no id."); continue; }
                foreach (var l in c.links)
                {
                    if (string.IsNullOrEmpty(l.targetId))
                        problems.Add($"'{c.id}' has a link with no target.");
                    else if (Find(l.targetId) == null)
                        problems.Add($"'{c.id}' links to '{l.targetId}', which is not in this graph.");
                }
            }
            return problems;
        }
    }
}
