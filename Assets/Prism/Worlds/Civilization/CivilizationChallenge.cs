using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.Civilization
{
    /// <summary>
    /// The Apply, Explain and Create stages.
    ///
    /// All three are checked against the real simulation rather than against an answer the
    /// learner types — the same discipline OrbitalChallenge uses. Nothing here owns geometry of
    /// its own: it reads CivilizationWorld's settlements and routes, and writes back only a
    /// couple of int fields (HighlightA/B, PredictedIndex) that the world's own view code turns
    /// into a gentle pulse. That keeps the risk of this file entirely in logic, not in meshes.
    /// </summary>
    public class CivilizationChallenge : MonoBehaviour
    {
        public CivilizationWorld World;

        enum Mode { Idle, Sufficiency, Prediction, Resilience }
        Mode _mode = Mode.Idle;

        // ---- Apply: network sufficiency ----
        float _sufficiencySustain;
        const float SufficiencyFloor = 0.55f;
        const float SufficiencySustainSeconds = 4f;

        // ---- Explain: prediction ----
        Settlement _candA, _candB;
        int _markedIndex = -1;
        bool _waitStarted;
        float _predictWaitElapsed;
        float _p0A, _p0B;
        float _researchTimer;
        const float MarkRadius = 0.06f;
        const float PredictionWindow = 6f;
        const float ResearchInterval = 2.5f;

        // ---- Create: resilience ----
        bool _armed;
        bool _watchingPostCut;
        float _redundantSustain;
        float _postCutSustain;
        const float RedundantSustainSeconds = 3f;
        const float PostCutSustainSeconds = 5f;
        const float ResilienceFloor = 0.45f;
        const float ResilienceMinSettlementFloor = 0.15f;

        // -----------------------------------------------------------------

        public void BeginSufficiency()
        {
            _mode = Mode.Sufficiency;
            _sufficiencySustain = 0f;
        }

        public void BeginPrediction()
        {
            _mode = Mode.Prediction;
            _markedIndex = -1;
            _waitStarted = false;
            _predictWaitElapsed = 0f;
            _researchTimer = 0f;
            FindPredictionCandidates();
        }

        public void BeginResilience()
        {
            _mode = Mode.Resilience;
            _armed = false;
            _watchingPostCut = false;
            _redundantSustain = 0f;
            _postCutSustain = 0f;
        }

        /// <summary>Called by the world the moment a route is severed, so resilience can react to
        /// the actual event rather than polling for it.</summary>
        public void NotifyRouteCut()
        {
            if (_mode != Mode.Resilience || !_armed) return;
            _watchingPostCut = true;
            _postCutSustain = 0f;
        }

        public void Evaluate(float dt)
        {
            switch (_mode)
            {
                case Mode.Sufficiency: EvaluateSufficiency(dt); break;
                case Mode.Prediction:  EvaluatePrediction(dt);  break;
                case Mode.Resilience:  EvaluateResilience(dt);  break;
            }
        }

        // -----------------------------------------------------------------
        // Apply: connect the network so every settlement has what it needs.
        // -----------------------------------------------------------------

        void EvaluateSufficiency(float dt)
        {
            var sim = World.Sim;
            bool everyoneTouched = true;
            for (int i = 0; i < sim.Settlements.Count; i++)
            {
                if (!HasFlourishingRoute(sim.Settlements[i])) { everyoneTouched = false; break; }
            }

            bool avgOk = sim.NetworkAvgSatisfaction() >= SufficiencyFloor;
            bool ok = everyoneTouched && avgOk;

            _sufficiencySustain = ok ? _sufficiencySustain + dt : 0f;
            if (_sufficiencySustain < SufficiencySustainSeconds) return;

            World.Loop.Evidence.Record(CivilizationEvidence.NetworkSufficient);
            World.Knowledge?.Confirm(World.PrimaryConceptId, 0.3f);
            Debug.Log("[PRISM] Apply: every settlement is reached by a working route.");
            _mode = Mode.Idle;
        }

        bool HasFlourishingRoute(Settlement s)
        {
            var routes = World.Sim.Routes;
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if ((r.A == s || r.B == s) && r.IsFlourishing) return true;
            }
            return false;
        }

        // -----------------------------------------------------------------
        // Explain: predict which settlement grows fastest after a new route.
        // -----------------------------------------------------------------

        void FindPredictionCandidates()
        {
            _candA = _candB = null;
            var sim = World.Sim;
            float best = float.NegativeInfinity;

            for (int i = 0; i < sim.Settlements.Count; i++)
            {
                for (int j = i + 1; j < sim.Settlements.Count; j++)
                {
                    var a = sim.Settlements[i];
                    var b = sim.Settlements[j];
                    if (sim.FindRoute(a, b) != null) continue; // already connected

                    float dist = Vector3.Distance(a.LocalPosition, b.LocalPosition);
                    float estCost = TradeSim.TransportCostPerMetre * dist;

                    for (int g = 0; g < Goods.Count; g++)
                    {
                        float score = Mathf.Abs(a.Price[g] - b.Price[g]) - estCost;
                        if (score > best) { best = score; _candA = a; _candB = b; }
                    }
                }
            }

            if (_candA != null && best > 0f)
            {
                World.HighlightA = _candA.Index;
                World.HighlightB = _candB.Index;
                _waitStarted = false;
                Debug.Log($"[PRISM] Explain: mark which of these two grows faster once connected.");
            }
            else
            {
                _candA = _candB = null;
                World.HighlightA = World.HighlightB = -1;
            }
        }

        void EvaluatePrediction(float dt)
        {
            if (_candA == null || _candB == null)
            {
                _researchTimer += dt;
                if (_researchTimer >= ResearchInterval) { _researchTimer = 0f; FindPredictionCandidates(); }
                return;
            }

            if (_markedIndex < 0)
            {
                TryMark(World.Hands?.Left);
                TryMark(World.Hands?.Right);
                return;
            }

            var route = World.Sim.FindRoute(_candA, _candB);
            if (route == null) return; // waiting for the learner to actually draw it

            if (!_waitStarted)
            {
                // The instant the route appears: snapshot the baseline to compare against, rather
                // than the settlements' current (already-drifted) prosperity.
                _waitStarted = true;
                _predictWaitElapsed = 0f;
                _p0A = _candA.Prosperity;
                _p0B = _candB.Prosperity;
                return;
            }

            _predictWaitElapsed += dt;
            if (_predictWaitElapsed < PredictionWindow) return;

            float growA = _candA.Prosperity - _p0A;
            float growB = _candB.Prosperity - _p0B;
            bool predictedA = _markedIndex == _candA.Index;
            bool correct = predictedA ? growA >= growB : growB >= growA;

            if (correct)
            {
                World.Loop.Evidence.Record(CivilizationEvidence.PredictionCorrect);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.35f);
                Debug.Log("[PRISM] Explain: prediction correct.");
                World.HighlightA = World.HighlightB = -1;
                World.PredictedIndex = -1;
                _mode = Mode.Idle;
            }
            else
            {
                World.Knowledge?.FlagMisconception(World.PrimaryConceptId, 0.15f);
                Debug.Log("[PRISM] Explain: prediction did not hold; try another pair.");
                _markedIndex = -1;
                World.PredictedIndex = -1;
                _predictWaitElapsed = 0f;
                FindPredictionCandidates();
            }
        }

        void TryMark(PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked || !h.PinchDown) return;
            Vector3 p = PrismHands.PointOf(h);

            float dA = Vector3.Distance(p, World.SettlementWorldPos(_candA.Index));
            float dB = Vector3.Distance(p, World.SettlementWorldPos(_candB.Index));

            if (dA < MarkRadius && dA <= dB) Mark(_candA, h);
            else if (dB < MarkRadius && dB < dA) Mark(_candB, h);
        }

        void Mark(Settlement s, PrismHands.Hand h)
        {
            _markedIndex = s.Index;
            World.PredictedIndex = s.Index;
            World.Loop.Evidence.Record(CivilizationEvidence.PredictionMarked);
            World.Hands.Buzz(h, 0.3f, 0.05f);
            Debug.Log($"[PRISM] Explain: marked settlement {s.Id} to grow fastest.");
        }

        // -----------------------------------------------------------------
        // Create: a network resilient to one route being cut.
        // -----------------------------------------------------------------

        void EvaluateResilience(float dt)
        {
            var sim = World.Sim;

            if (!_armed)
            {
                bool allRedundant = sim.Settlements.Count > 0;
                for (int i = 0; i < sim.Settlements.Count; i++)
                    if (sim.RouteDegree(sim.Settlements[i]) < 2) { allRedundant = false; break; }

                _redundantSustain = allRedundant ? _redundantSustain + dt : 0f;
                if (_redundantSustain >= RedundantSustainSeconds)
                {
                    _armed = true;
                    World.Loop.Evidence.Record(CivilizationEvidence.ResilienceRedundant);
                    Debug.Log("[PRISM] Create: every settlement has a second route. Cut one to prove it holds.");
                }
                return;
            }

            if (!_watchingPostCut) return;

            float minSat = float.PositiveInfinity;
            for (int i = 0; i < sim.Settlements.Count; i++)
                minSat = Mathf.Min(minSat, sim.Settlements[i].AvgSatisfaction);

            bool ok = sim.NetworkAvgSatisfaction() >= ResilienceFloor && minSat > ResilienceMinSettlementFloor;
            _postCutSustain = ok ? _postCutSustain + dt : 0f;

            if (_postCutSustain >= PostCutSustainSeconds)
            {
                World.Loop.Evidence.Record(CivilizationEvidence.ResiliencePassed);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.4f);
                Debug.Log("[PRISM] Create: the network held after a route was cut.");
                _watchingPostCut = false;
                _mode = Mode.Idle;
            }
        }
    }
}
