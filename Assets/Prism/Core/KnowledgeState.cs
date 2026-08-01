using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Prism.Core
{
    /// <summary>What the learner has actually got, for one concept.</summary>
    [Serializable]
    public class ConceptState
    {
        public string id;

        /// <summary>0..1. Drives _Growth in Prism/Seed, so this number is literally visible.</summary>
        public float mastery;

        /// <summary>
        /// How well the understanding has survived being retested. Mastery can be reached in
        /// one lucky session; stability cannot. Low stability with high mastery is exactly the
        /// state a revision system should care about, so the two are tracked apart.
        /// </summary>
        public float stability;

        /// <summary>0..1. Drives _Instability — the facet lattice shivers and will not settle.</summary>
        public float misconception;

        /// <summary>Furthest stage of the loop reached. Never regresses.</summary>
        public LoopStage stage;

        public long lastTouchedUnix;
        public int  sessionsTouched;

        public DateTime LastTouched => DateTimeOffset.FromUnixTimeSeconds(lastTouchedUnix).UtcDateTime;
    }

    [Serializable]
    class KnowledgeSave
    {
        public int version = 1;
        public List<ConceptState> concepts = new List<ConceptState>();
        public long savedUnix;
    }

    /// <summary>
    /// The learner's profile. Local file, no account required; cloud sync is a later seam and
    /// deliberately not a precondition for anything.
    ///
    /// The spec is emphatic that a learner must never pay again to keep access to material they
    /// already bought, and this class is where that promise is kept or broken: nothing here
    /// consults an entitlement, and nothing here can expire.
    /// </summary>
    public class KnowledgeState
    {
        public const string FileName = "prism-learner.json";

        readonly Dictionary<string, ConceptState> _states = new Dictionary<string, ConceptState>();
        readonly ConceptGraph _graph;
        string _path;

        public event Action<ConceptState> Changed;

        public KnowledgeState(ConceptGraph graph, string directory = null)
        {
            _graph = graph;
            var dir = directory ?? Application.persistentDataPath;
            _path = Path.Combine(dir, FileName);
        }

        public ConceptState Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!_states.TryGetValue(id, out var s))
            {
                s = new ConceptState { id = id, stage = LoopStage.Wonder };
                _states[id] = s;
            }
            return s;
        }

        public float MasteryOf(string id) => Get(id).mastery;

        /// <summary>
        /// Advance understanding. Mastery approaches 1 asymptotically rather than accumulating
        /// linearly: repetition of something already understood should feel like diminishing
        /// returns, because it is.
        /// </summary>
        public void Learn(string id, float amount)
        {
            var s = Get(id);
            s.mastery = Mathf.Clamp01(s.mastery + (1f - s.mastery) * Mathf.Clamp01(amount));
            Touch(s);
        }

        /// <summary>
        /// Understanding that survived a fresh test. Stability only ever rises here — which is
        /// why it cannot be farmed by repeating the same successful action in one sitting.
        /// </summary>
        public void Confirm(string id, float amount = 0.25f)
        {
            var s = Get(id);
            s.stability = Mathf.Clamp01(s.stability + (1f - s.stability) * Mathf.Clamp01(amount));
            s.misconception = Mathf.Max(0f, s.misconception - amount * 0.5f);
            Touch(s);
        }

        public void FlagMisconception(string id, float amount = 0.3f)
        {
            var s = Get(id);
            s.misconception = Mathf.Clamp01(s.misconception + amount);
            // A misconception does not erase understanding, but it does destabilise it.
            s.stability = Mathf.Max(0f, s.stability - amount * 0.5f);
            Touch(s);
        }

        public void ReachStage(string id, LoopStage stage)
        {
            var s = Get(id);
            if (stage > s.stage) { s.stage = stage; Touch(s); }
        }

        void Touch(ConceptState s)
        {
            s.lastTouchedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Changed?.Invoke(s);
        }

        /// <summary>
        /// How cut off a concept is from the rest of what the learner knows: 1 means every
        /// neighbour is unknown, 0 means they are all mastered.
        ///
        /// This is what keeps weak and isolated concepts visible in the atrium instead of
        /// letting them quietly vanish once the headline number looks good. Revision becomes a
        /// thing you can see rather than a thing you are nagged about.
        /// </summary>
        public float Isolation(string id)
        {
            if (_graph == null) return 0f;
            int n = 0; float known = 0f;
            foreach (var nb in _graph.Neighbours(id)) { n++; known += MasteryOf(nb.id); }
            return n == 0 ? 1f : 1f - (known / n);
        }

        /// <summary>
        /// Brightness of a concept as drawn in the atrium. Mastery lights it; isolation dims it.
        /// A well understood idea with no connections is not finished, and should not look it.
        /// </summary>
        public float Luminosity(string id)
        {
            var s = Get(id);
            float connected = 1f - Isolation(id);
            return Mathf.Clamp01(s.mastery * (0.55f + 0.45f * connected));
        }

        // ---- persistence ----------------------------------------------------

        public void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var save = JsonUtility.FromJson<KnowledgeSave>(File.ReadAllText(_path));
                if (save?.concepts == null) return;
                _states.Clear();
                foreach (var c in save.concepts)
                    if (!string.IsNullOrEmpty(c.id)) _states[c.id] = c;
                Debug.Log($"[PRISM] Loaded learner profile: {_states.Count} concepts from {_path}");
            }
            catch (Exception e)
            {
                // A corrupt profile must never block entry. Losing progress is bad; being
                // unable to open the app at all is worse.
                Debug.LogWarning($"[PRISM] Could not read learner profile ({e.Message}). Starting fresh.");
            }
        }

        public void Save()
        {
            try
            {
                var save = new KnowledgeSave { savedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                foreach (var kv in _states) save.concepts.Add(kv.Value);
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, JsonUtility.ToJson(save, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PRISM] Could not write learner profile: {e.Message}");
            }
        }

        public IEnumerable<ConceptState> AllStates => _states.Values;
    }
}
