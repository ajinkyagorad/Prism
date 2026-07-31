using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.LivingCell
{
    /// <summary>
    /// The Apply, Explain and Create stages, evaluated against the running simulation rather than
    /// against a typed or spoken answer. Mirrors OrbitalChallenge's split: LivingCellWorld owns the
    /// hands-on geometry (the channel, the motes, the membrane), this owns the three scripted
    /// evaluations that sit on top of it.
    ///
    ///   Apply     keep the cell alive through a demand spike — a scripted surge in how hard the
    ///             cell's "work" is drawing on ATP, survived using the SAME controls (block, feed,
    ///             pinch) the learner already has.
    ///   Explain   predict the ATP token count after a specific, fully-blocked interval, by placing
    ///             a marker along the token row before it happens — then watch.
    ///   Create    tune the cell's own catabolism rate (the one dial the learner is given) and keep
    ///             it alive through one full slow demand cycle, unattended.
    ///
    /// Nothing here fails permanently. A spike or a cycle that is not survived simply keeps running;
    /// a wrong prediction is recorded as a misconception (which shows up in the atrium as an
    /// unstable structure) and offered again. Nothing is scored and nothing is lost.
    /// </summary>
    public class CellChallenge : MonoBehaviour
    {
        public LivingCellWorld World;

        enum Mode { Idle, Spike, Prediction, Cycle }
        Mode _mode = Mode.Idle;

        // ---- Apply: demand spike ----
        const float SpikeRampUp = 2f, SpikeHold = 10f, SpikeRampDown = 3f;
        const float SpikeCycleSeconds = SpikeRampUp + SpikeHold + SpikeRampDown;
        const float SpikePeak = 3.2f;
        const float SpikeSurvivalFloor = 0.10f;
        float _spikeElapsed, _spikeSurvivedFor;

        // ---- Explain: prediction ----
        const float PredictionHold = 7f;
        Transform _markerView;
        Material _markerMat;
        bool _hasPending, _markerPlaced;
        float _pendingU;
        int _markerTokenIndex;
        float _predictElapsed;

        // ---- Create: tuning + one slow cycle ----
        const float CyclePeriodSeconds = 36f;
        const float CycleFloor = 0.08f;
        const float CycleCeiling = 0.95f;
        float _cycleElapsed, _cycleSurvivedFor;

        void Awake()
        {
            BuildMarker();
        }

        void BuildMarker()
        {
            var mat = PrismMaterials.New(PrismMaterials.Volumetric);
            mat.SetColor("_Tint", PrismPalette.Lavender);
            mat.SetColor("_EdgeTint", PrismPalette.Gold);
            mat.SetFloat("_Density", 0.7f);
            _markerMat = mat;

            var go = new GameObject("PredictionMarker");
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * 0.014f;
            _markerView = go.transform;
            go.SetActive(false);
        }

        // -----------------------------------------------------------------

        public void BeginSpike()
        {
            _mode = Mode.Spike;
            _spikeElapsed = 0f;
            _spikeSurvivedFor = 0f;
            Debug.Log("[PRISM] Apply: a demand spike is coming. Keep the ATP charge above 10%.");
        }

        public void BeginPrediction()
        {
            _mode = Mode.Prediction;
            _markerPlaced = false;
            _hasPending = false;
            _predictElapsed = 0f;
            World.ChannelForcedClosed = false;
            if (_markerView != null)
            {
                _markerView.SetParent(World.WorldAnchor, false);
                _markerView.gameObject.SetActive(true);
            }
            Debug.Log("[PRISM] Explain: place a marker on the token row for where the charge will " +
                      $"be after {PredictionHold:0} s with the channel fully blocked.");
        }

        public void BeginCycle()
        {
            _mode = Mode.Cycle;
            _cycleElapsed = 0f;
            _cycleSurvivedFor = 0f;
            Debug.Log("[PRISM] Create: tune the catabolism dial, then survive one full demand cycle.");
        }

        public void Evaluate(float dt)
        {
            switch (_mode)
            {
                case Mode.Spike:      EvaluateSpike(dt);      break;
                case Mode.Prediction: EvaluatePrediction(dt); break;
                case Mode.Cycle:      EvaluateCycle(dt);      break;
            }
        }

        // -----------------------------------------------------------------

        static float SpikeProfile(float tInCycle)
        {
            if (tInCycle < SpikeRampUp) return Mathf.Lerp(1f, SpikePeak, tInCycle / SpikeRampUp);
            if (tInCycle < SpikeRampUp + SpikeHold) return SpikePeak;
            float tDown = tInCycle - SpikeRampUp - SpikeHold;
            if (tDown < SpikeRampDown) return Mathf.Lerp(SpikePeak, 1f, tDown / SpikeRampDown);
            return 1f;
        }

        void EvaluateSpike(float dt)
        {
            _spikeElapsed += dt;
            float tInCycle = _spikeElapsed % SpikeCycleSeconds;
            World.Sim.DemandMultiplier = SpikeProfile(tInCycle);

            bool alive = World.Sim.AtpFraction > SpikeSurvivalFloor;
            _spikeSurvivedFor = alive ? _spikeSurvivedFor + dt : 0f;

            if (_spikeSurvivedFor >= SpikeCycleSeconds)
            {
                // Recording this evidence can synchronously advance the loop to Explain, whose
                // OnStageEntered calls BeginPrediction() and sets _mode = Prediction right here on
                // the call stack. Only fall back to Idle if nothing claimed the mode in the
                // meantime — otherwise this line would clobber that transition right after it happens.
                World.Loop.Evidence.Record(LivingCellEvidence.SpikeSurvived);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.3f);
                World.Sim.DemandMultiplier = 1f;
                if (_mode == Mode.Spike) _mode = Mode.Idle;
                Debug.Log("[PRISM] Apply: the cell held together through the whole spike.");
            }
        }

        // -----------------------------------------------------------------

        void EvaluatePrediction(float dt)
        {
            if (World == null || World.Hands == null) return;

            if (!_markerPlaced)
            {
                ServiceMarkerPlacement();
                return;
            }

            World.ChannelForcedClosed = true;
            _predictElapsed += dt;
            if (_predictElapsed < PredictionHold) return;

            World.ChannelForcedClosed = false;
            int actual = World.AtpLitCount;
            int err = Mathf.Abs(actual - _markerTokenIndex);
            bool good = err <= 1;

            if (good)
            {
                // As in EvaluateSpike: this Record() can synchronously advance the loop to Create,
                // whose OnStageEntered calls BeginCycle() and sets _mode = Cycle on this same call
                // stack. Guard so that line is not undone the instant it happens.
                World.Loop.Evidence.Record(LivingCellEvidence.PredictionGood);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.35f);
                if (_markerView != null) _markerView.gameObject.SetActive(false);
                if (_mode == Mode.Prediction) _mode = Mode.Idle;
                Debug.Log($"[PRISM] Explain: prediction correct ({actual} tokens, {err} out).");
            }
            else
            {
                // Wrong is informative, not final. Recorded as a misconception, then offered again.
                World.Knowledge?.FlagMisconception(World.PrimaryConceptId, 0.15f);
                Debug.Log($"[PRISM] Explain: predicted token {_markerTokenIndex}, landed on {actual}; try again.");
                _markerPlaced = false;
                _hasPending = false;
                _predictElapsed = 0f;
            }
        }

        void ServiceMarkerPlacement()
        {
            TryPlaceWith(World.Hands.Left);
            TryPlaceWith(World.Hands.Right);
        }

        void TryPlaceWith(PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked) return;

            if (h.Pinch > 0.6f)
            {
                float u = ClosestArcU(h.Position);
                _pendingU = u;
                _hasPending = true;
                if (_markerView != null)
                    _markerView.position = World.WorldAnchor.TransformPoint(World.AtpArcLocalPoint(u));
            }
            else if (h.PinchUp && _hasPending)
            {
                _markerTokenIndex = Mathf.RoundToInt(_pendingU * (LivingCellWorld.AtpTokenCount - 1));
                _markerPlaced = true;
                _predictElapsed = 0f;
                _hasPending = false;
                World.Hands?.Buzz(h, 0.2f, 0.05f);
            }
        }

        float ClosestArcU(Vector3 worldPos)
        {
            Vector3 a = World.WorldAnchor.TransformPoint(World.AtpArcLocalPoint(0f));
            Vector3 b = World.WorldAnchor.TransformPoint(World.AtpArcLocalPoint(1f));
            Vector3 ab = b - a;
            float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
            float u = Vector3.Dot(worldPos - a, ab) / len2;
            return Mathf.Clamp01(u);
        }

        // -----------------------------------------------------------------

        void EvaluateCycle(float dt)
        {
            _cycleElapsed += dt;
            float mult = 1f + 0.9f * Mathf.Sin(2f * Mathf.PI * _cycleElapsed / CyclePeriodSeconds);
            World.Sim.DemandMultiplier = Mathf.Max(0.05f, mult);

            float x = World.Sim.AtpFraction;
            bool ok = x > CycleFloor && x < CycleCeiling;
            _cycleSurvivedFor = ok ? _cycleSurvivedFor + dt : 0f;

            if (_cycleSurvivedFor >= CyclePeriodSeconds)
            {
                // Connect has no CellChallenge Begin* callback today, so this Record() cannot
                // reenter and reassign _mode the way the two guards above protect against — but the
                // same guard is applied here too, so this stays correct if that ever changes.
                World.Loop.Evidence.Record(LivingCellEvidence.CycleSurvived);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.4f);
                World.Sim.DemandMultiplier = 1f;
                if (_mode == Mode.Cycle) _mode = Mode.Idle;
                Debug.Log("[PRISM] Create: this cell survived a full cycle on its own tuning.");
            }
        }
    }
}
