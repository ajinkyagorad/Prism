using Prism.Core;
using UnityEngine;

namespace Prism.Worlds.PlanetGuardian
{
    /// <summary>
    /// The Apply, Explain and Create stages: all three are checked against the running
    /// simulation rather than against an answer the learner types.
    ///
    /// The disturbance (Create) is evaluated regardless of <see cref="_mode"/> — a learner ought
    /// to be able to retest their planet's stability at any point once it has been revealed, not
    /// only in a single gated attempt.
    /// </summary>
    public class PlanetGuardianChallenge : MonoBehaviour
    {
        public PlanetGuardianWorld World;

        enum Mode { Idle, Target, Prediction }
        Mode _mode = Mode.Idle;

        // ---- Apply: hold the planet on a target temperature ----
        float _targetT;
        float _targetHeldFor;
        const float TargetTolerance = 2.5f;      // K
        const float TargetHoldSeconds = 6f;      // continuous

        // ---- Explain: mark where it will settle, then let it run ----
        bool _predictArmed;      // true once an unsettled reading has been seen this attempt
        bool _predictPlaced;
        float _predictValue;
        bool _wasSettled;
        const float PredictionTolerance = 6f;    // K

        // ---- Create: a volcanic-style transient forcing pulse ----
        /// <summary>W/m^2, subtracted from absorbed while a pulse is running. Read by the world
        /// every frame and fed into the simulation — the world owns the sim, this only says how
        /// hard to push it right now.</summary>
        public float DisturbanceForcing { get; private set; }

        bool _disturbanceRunning;
        float _disturbanceElapsed;
        float _preDisturbanceT;
        float _postCloseFor;
        float _cooldown;
        bool _stableRecorded;

        const float PulseRamp = 1f;              // seconds to ramp in/out — nothing here has a sharp attack
        const float PulseHold = 4f;
        const float PulsePeak = 45f;              // W/m^2 at the centre of the pulse
        const float PostSettleTolerance = 4f;    // K, "back where it was"
        const float PostSettleHoldSeconds = 2f;
        const float PostSettleWindow = 50f;      // how long we keep watching after the pulse ends

        /// <summary>
        /// Seconds after the pulse ends before proximity is trusted at all. Thermal inertia
        /// means a design that is actually TIPPING still reads as "close" for the first stretch
        /// after the pulse fades, simply because it has not had time to drift yet — checked
        /// against the stepper directly: a marginal configuration sat inside tolerance for the
        /// first ~30 s and only then pulled away for good. Without this grace period the check
        /// would reward exactly the failure this stage exists to catch.
        /// </summary>
        const float PostSettleGrace = 30f;

        // -----------------------------------------------------------------

        public void BeginTargetChallenge()
        {
            _mode = Mode.Target;
            // Two verified-holdable zones, not one span across the tipping band between them.
            // Checked against the stepper directly (see NOTES.md): a target anywhere in roughly
            // 215-285 K cannot actually be HELD by incremental control no matter how careful,
            // because it sits inside the unstable region between the two real equilibria — you
            // are always either still falling toward one branch or the other. That is not a
            // hard target, it is an impossible one, which is a worse and different thing than
            // what this stage is for. Both zones below are hard for the honest reason: the
            // delay means overshoot is easy and correcting it takes real patience.
            bool warm = Random.value < 0.5f;
            _targetT = warm ? Random.Range(292f, 308f) : Random.Range(198f, 213f);
            _targetHeldFor = 0f;
            World.ShowThermTarget(_targetT);
        }

        public void BeginPrediction()
        {
            _mode = Mode.Prediction;
            _predictArmed = false;
            _predictPlaced = false;
            World.ShowThermPredict(true);
        }

        public void Evaluate(float dt)
        {
            EvaluateDisturbance(dt);

            switch (_mode)
            {
                case Mode.Target:     EvaluateTarget(dt);     break;
                case Mode.Prediction: EvaluatePrediction(dt); break;
            }
        }

        void EvaluateTarget(float dt)
        {
            bool onTarget = Mathf.Abs(World.Sim.Temperature - _targetT) < TargetTolerance;
            _targetHeldFor = onTarget ? _targetHeldFor + dt : 0f;

            if (_targetHeldFor >= TargetHoldSeconds)
            {
                World.Loop.Evidence.Record(PlanetGuardianEvidence.TargetHeld);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.3f);
                World.Companion?.Voice?.Settle(World.PlanetPosition, 0.6f);
                World.HideThermTarget();
                _mode = Mode.Idle;
            }
        }

        void EvaluatePrediction(float dt)
        {
            bool settled = World.IsSettled;
            // Arm on the FIRST unsettled reading: a prediction only counts once something has
            // actually been disturbed, never against a planet that was already at rest.
            if (!settled) _predictArmed = true;

            if (World.TryConsumePredictDrag(out float predictedT))
            {
                _predictPlaced = true;
                _predictValue = predictedT;
            }

            // The moment of comparison: armed, a mark is placed, and the planet has just settled.
            if (_predictArmed && _predictPlaced && settled && !_wasSettled)
            {
                float err = Mathf.Abs(World.Sim.Temperature - _predictValue);
                if (err < PredictionTolerance)
                {
                    World.Loop.Evidence.Record(PlanetGuardianEvidence.PredictionGood);
                    World.Knowledge?.Confirm(World.PrimaryConceptId, 0.35f);
                    World.Companion?.Voice?.Consonance(World.PlanetPosition, 0.6f);
                    World.ShowThermPredict(false);
                    _mode = Mode.Idle;
                }
                else
                {
                    // Wrong is the most informative outcome here, and it is recorded as a
                    // misconception rather than a failure — the atrium will show this concept as
                    // not yet settled, which is true. They may simply try again.
                    World.Knowledge?.FlagMisconception(World.PrimaryConceptId, 0.15f);
                    _predictPlaced = false;
                    _predictArmed = false;
                }
            }
            _wasSettled = settled;
        }

        /// <summary>
        /// Design a planet, then pinch the disturbance to test it. What is being tested is the
        /// LEVER SETTINGS the learner chose beforehand, not their reflexes during the pulse —
        /// nothing prevents them from adjusting controls while it runs, but nothing rewards it
        /// either. The evidence only cares whether the temperature that comes out the other side
        /// is close to the temperature that went in.
        /// </summary>
        void EvaluateDisturbance(float dt)
        {
            if (World.Loop.Stage < LoopStage.Create) return;

            if (_cooldown > 0f) _cooldown -= dt;

            if (!_disturbanceRunning)
            {
                if (_cooldown <= 0f && World.TryConsumeMeteorTrigger())
                {
                    _disturbanceRunning = true;
                    _disturbanceElapsed = 0f;
                    _preDisturbanceT = World.Sim.Temperature;
                    _postCloseFor = 0f;
                    _stableRecorded = false;
                    World.Loop.Evidence.Record(PlanetGuardianEvidence.DisturbanceTriggered);
                }
                return;
            }

            _disturbanceElapsed += dt;
            DisturbanceForcing = PulseEnvelope(_disturbanceElapsed) * PulsePeak;

            const float pulseTotal = PulseRamp * 2f + PulseHold;
            if (_disturbanceElapsed >= pulseTotal)
            {
                DisturbanceForcing = 0f;

                if (!_stableRecorded && _disturbanceElapsed >= pulseTotal + PostSettleGrace)
                {
                    bool close = Mathf.Abs(World.Sim.Temperature - _preDisturbanceT) < PostSettleTolerance;
                    _postCloseFor = close ? _postCloseFor + dt : 0f;
                    if (_postCloseFor >= PostSettleHoldSeconds)
                    {
                        World.Loop.Evidence.Record(PlanetGuardianEvidence.ConstructionStable);
                        World.Knowledge?.Confirm(World.PrimaryConceptId, 0.4f);
                        World.Companion?.Voice?.Consonance(World.PlanetPosition, 0.6f);
                        _stableRecorded = true;
                    }
                }

                if (_disturbanceElapsed >= pulseTotal + PostSettleWindow)
                {
                    _disturbanceRunning = false;
                    _cooldown = 2f;
                }
            }
        }

        /// <summary>Raised-cosine ramp up, hold, ramp down — nothing in PRISM has a sharp attack.</summary>
        static float PulseEnvelope(float t)
        {
            if (t < PulseRamp) return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t / PulseRamp);
            if (t < PulseRamp + PulseHold) return 1f;
            float tail = t - PulseRamp - PulseHold;
            if (tail < PulseRamp) return 0.5f + 0.5f * Mathf.Cos(Mathf.PI * tail / PulseRamp);
            return 0f;
        }
    }
}
