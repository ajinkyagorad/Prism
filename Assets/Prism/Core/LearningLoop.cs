using System;
using System.Collections.Generic;
using UnityEngine;

namespace Prism.Core
{
    /// <summary>
    /// The eight stages every PRISM module moves through, in order. Formalize comes fourth,
    /// not first: terminology, equations and diagrams are withheld until the learner has
    /// already built the intuition they name.
    /// </summary>
    public enum LoopStage
    {
        /// <summary>Encounter an unexplained phenomenon. No instructions, no labels.</summary>
        Wonder = 0,
        /// <summary>Freely manipulate it. Still no instructions.</summary>
        Explore = 1,
        /// <summary>Causal relationships surface through experiment.</summary>
        Discover = 2,
        /// <summary>Only now: names, equations, diagrams.</summary>
        Formalize = 3,
        /// <summary>Solve something practical or imaginative with it.</summary>
        Apply = 4,
        /// <summary>Teach it — to an AI character, another learner, or by prediction.</summary>
        Explain = 5,
        /// <summary>Use it in a construction of your own.</summary>
        Create = 6,
        /// <summary>See how it relates to everything else you know.</summary>
        Connect = 7
    }

    /// <summary>
    /// Everything the learner has been observed to DO. The loop advances on this and on
    /// nothing else — there is no answer to submit and no button that means "I understand".
    ///
    /// Recording evidence is the only thing a world has to do to participate in the loop.
    /// </summary>
    public class Evidence
    {
        readonly Dictionary<string, int>   _counts = new Dictionary<string, int>();
        readonly Dictionary<string, float> _values = new Dictionary<string, float>();

        public event Action Updated;

        /// <summary>The learner did this thing. Again, if they had done it before.</summary>
        public void Record(string key, int times = 1)
        {
            _counts.TryGetValue(key, out var c);
            _counts[key] = c + times;
            Updated?.Invoke();
        }

        /// <summary>The best value the learner has achieved on some continuous measure.</summary>
        public void ObserveBest(string key, float value, bool lowerIsBetter = false)
        {
            if (_values.TryGetValue(key, out var v))
                _values[key] = lowerIsBetter ? Mathf.Min(v, value) : Mathf.Max(v, value);
            else
                _values[key] = value;
            Updated?.Invoke();
        }

        /// <summary>The latest value of something that changes.</summary>
        public void Set(string key, float value)
        {
            _values[key] = value;
            Updated?.Invoke();
        }

        public int   Count(string key) => _counts.TryGetValue(key, out var c) ? c : 0;
        public bool  Has(string key)   => Count(key) > 0;
        public float Value(string key, float fallback = 0f) => _values.TryGetValue(key, out var v) ? v : fallback;

        public void Clear() { _counts.Clear(); _values.Clear(); Updated?.Invoke(); }

        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _counts) sb.Append($"{kv.Key}={kv.Value} ");
            foreach (var kv in _values) sb.Append($"{kv.Key}:{kv.Value:0.###} ");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// The condition for leaving a stage.
    ///
    /// <para><see cref="Missing"/> is not UI text. It is what the companion consults when it
    /// decides whether the learner is genuinely stuck, so it has to describe the gap in terms
    /// of what has not yet been DONE — never in terms of what has not yet been said.</para>
    /// </summary>
    public class LoopGate
    {
        public readonly LoopStage From;
        public readonly string Missing;
        readonly Func<Evidence, bool> _satisfied;

        public LoopGate(LoopStage from, string missing, Func<Evidence, bool> satisfied)
        {
            From = from;
            Missing = missing;
            _satisfied = satisfied;
        }

        public bool IsSatisfied(Evidence e) => _satisfied(e);
    }

    /// <summary>
    /// Drives one concept through the eight stages, on evidence alone.
    ///
    /// Stages never regress. A learner who reaches Formalize and then does something naive has
    /// not become less advanced — they have produced a misconception, which is recorded
    /// separately and shows up as an unstable structure rather than as lost progress.
    /// </summary>
    public class LearningLoop
    {
        public readonly string ConceptId;
        public readonly Evidence Evidence = new Evidence();

        readonly Dictionary<LoopStage, LoopGate> _gates = new Dictionary<LoopStage, LoopGate>();
        readonly KnowledgeState _state;

        public LoopStage Stage { get; private set; }
        public float TimeInStage { get; private set; }

        /// <summary>Raised with the stage just entered. Worlds subscribe to reveal layers.</summary>
        public event Action<LoopStage> StageEntered;

        public LearningLoop(string conceptId, KnowledgeState state)
        {
            ConceptId = conceptId;
            _state = state;
            Stage = state?.Get(conceptId)?.stage ?? LoopStage.Wonder;

            // Evidence only marks the loop dirty; gates are evaluated in Tick. See the note there.
            Evidence.Updated += () => _dirty = true;
        }

        bool _dirty;

        public void AddGate(LoopGate gate) => _gates[gate.From] = gate;

        /// <summary>What is stopping the learner leaving this stage. Empty when the stage is open.</summary>
        public string Missing => _gates.TryGetValue(Stage, out var g) ? g.Missing : "";

        /// <summary>
        /// Advance the clock and, if new evidence has arrived, re-evaluate the gates.
        ///
        /// GATES ARE EVALUATED HERE AND NOWHERE ELSE, and that is deliberate.
        ///
        /// Evidence.Record() used to re-evaluate synchronously, which meant recording a piece of
        /// evidence could satisfy a gate, enter the next stage, and run that stage's setup — all
        /// on the caller's own call stack, in the middle of their method. Any line after the
        /// Record() that touched shared state then silently clobbered the stage that had just
        /// begun. Two independently written challenge scripts hit this exact trap, and in both
        /// cases the symptom was a stage that simply never ran, with no error anywhere.
        ///
        /// Deferring to Tick costs at most one frame of latency, which nobody can perceive, and
        /// makes the transition point a single known place instead of "wherever someone happened
        /// to call Record". Recording evidence is now always safe.
        /// </summary>
        public void Tick(float dt)
        {
            TimeInStage += dt;
            if (!_dirty) return;
            _dirty = false;
            Reevaluate();
        }

        /// <summary>
        /// True when the learner has been in one stage long enough, without satisfying its gate,
        /// that a nudge is warranted. The companion should not lecture — it watches, and this is
        /// how it knows the difference between exploring and floundering.
        /// </summary>
        public bool LooksStuck(float patienceSeconds = 75f) =>
            TimeInStage > patienceSeconds && _gates.ContainsKey(Stage);

        void Reevaluate()
        {
            // Advance as far as the evidence supports in one pass: a learner who does something
            // sophisticated early should not be walked through stages they have already cleared.
            int guard = 0;
            while (guard++ < 16 && _gates.TryGetValue(Stage, out var gate) && gate.IsSatisfied(Evidence))
                Enter(Stage + 1);
        }

        void Enter(LoopStage stage)
        {
            if (stage <= Stage) return;
            Stage = stage;
            TimeInStage = 0f;
            _state?.ReachStage(ConceptId, stage);

            // Understanding accrues with the depth of engagement, not with time spent.
            _state?.Learn(ConceptId, StageLearnValue(stage));

            Debug.Log($"[PRISM] {ConceptId}: entered {stage}");
            StageEntered?.Invoke(stage);
        }

        /// <summary>
        /// Later stages are worth more because they are harder to fake. Explaining and applying a
        /// concept are the stages that most reliably distinguish understanding from familiarity,
        /// so they move the needle most.
        /// </summary>
        static float StageLearnValue(LoopStage stage)
        {
            switch (stage)
            {
                case LoopStage.Explore:   return 0.05f;
                case LoopStage.Discover:  return 0.15f;
                case LoopStage.Formalize: return 0.18f;
                case LoopStage.Apply:     return 0.30f;
                case LoopStage.Explain:   return 0.35f;
                case LoopStage.Create:    return 0.35f;
                case LoopStage.Connect:   return 0.25f;
                default:                  return 0f;
            }
        }

        /// <summary>Force a stage. Editor and teacher tooling only — never call this from gameplay.</summary>
        public void ForceStage(LoopStage stage)
        {
            if (stage > Stage) Enter(stage);
        }
    }
}
