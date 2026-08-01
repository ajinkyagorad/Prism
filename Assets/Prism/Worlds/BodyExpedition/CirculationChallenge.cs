using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Worlds.BodyExpedition
{
    /// <summary>
    /// The Apply, Explain and Create stages, checked against the running simulation rather than
    /// against a typed answer — the same principle OrbitalChallenge uses. A wrong prediction here
    /// is recorded as a misconception (it destabilises the concept in the atrium) rather than as a
    /// failure, and the learner simply tries again.
    /// </summary>
    public class CirculationChallenge : MonoBehaviour
    {
        public BodyExpeditionWorld World;

        enum Mode { Idle, Patient, Prediction, Balance }
        Mode _mode = Mode.Idle;

        static readonly int[] PeripheralEdges =
        {
            CirculationSim.EArmVenous, CirculationSim.EHeadVenous,
            CirculationSim.ELegLVenous, CirculationSim.ELegRVenous
        };
        static readonly int[] SingleLeafEdges =
        {
            CirculationSim.EUpperArm, CirculationSim.EUpperHead,
            CirculationSim.ELowerLegL, CirculationSim.ELowerLegR
        };

        // ---- Apply: restore the patient ----
        int _patientEdge = -1;
        float _healthyHeldFor;
        const float HealthyBandTolerance = 1.0f;     // kPa either side of RestingMap
        const float HealthySustainSeconds = 4f;
        const float PathologicalMult = 0.30f;

        // ---- Explain: predict the drop ----
        int _predictEdge = -1;
        int _predictTargetNode = -1;
        bool _markerPlaced;
        Vector3 _markerWorldPoint;
        Transform _markerView;
        Material _markerMat;
        float _predictSettle;
        const float PredictionSettleSeconds = 3f;
        const float PredictionTolerance = 0.055f;    // metres, world space

        // ---- Create: balance the tree ----
        float _balanceHeldFor;
        const float BalanceToleranceFraction = 0.14f;
        const float BalanceSustainSeconds = 5f;

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
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * 0.020f;
            _markerView = go.transform;
            go.SetActive(false);
        }

        // -----------------------------------------------------------------

        /// <summary>Reset all four peripheral vessels to healthy, then make one of them pathological.</summary>
        public void BeginPatient()
        {
            _mode = Mode.Patient;
            _healthyHeldFor = 0f;
            foreach (var idx in PeripheralEdges) World.Sim.Edges[idx].RadiusMult = 1f;
            _patientEdge = PeripheralEdges[Random.Range(0, PeripheralEdges.Length)];
            World.Sim.Edges[_patientEdge].RadiusMult = PathologicalMult;
            Debug.Log("[PRISM] Apply: a vessel has gone pathological. Bring pressure back to healthy.");
        }

        /// <summary>Pick a single-leaf artery that will narrow once the learner commits a prediction.</summary>
        public void BeginPrediction()
        {
            _mode = Mode.Prediction;
            _markerPlaced = false;
            _predictSettle = 0f;
            _predictEdge = SingleLeafEdges[Random.Range(0, SingleLeafEdges.Length)];
            World.Sim.Edges[_predictEdge].RadiusMult = 1f;
            _predictTargetNode = World.Sim.Edges[_predictEdge].To;
            _markerView.gameObject.SetActive(true);
            _markerView.position = World.Head != null
                ? World.Head.transform.position + World.Head.transform.forward * 0.4f
                : World.WorldAnchor.position;
            Debug.Log("[PRISM] Explain: place a marker where flow will drop, then watch.");
        }

        /// <summary>Start the four tissue beds deliberately uneven; the learner levels them by hand.</summary>
        public void BeginBalance()
        {
            _mode = Mode.Balance;
            _balanceHeldFor = 0f;
            World.Sim.Edges[CirculationSim.EArmVenous].RadiusMult = 0.78f;
            World.Sim.Edges[CirculationSim.EHeadVenous].RadiusMult = 1.05f;
            World.Sim.Edges[CirculationSim.ELegLVenous].RadiusMult = 1.20f;
            World.Sim.Edges[CirculationSim.ELegRVenous].RadiusMult = 0.85f;
            Debug.Log("[PRISM] Create: balance flow across all four territories.");
        }

        public void Evaluate(float dt)
        {
            switch (_mode)
            {
                case Mode.Patient:    EvaluatePatient(dt);    break;
                case Mode.Prediction: EvaluatePrediction(dt); break;
                case Mode.Balance:    EvaluateBalance(dt);    break;
            }
        }

        void EvaluatePatient(float dt)
        {
            float map = World.Sim.MeanArterialPressureFiltered;
            bool healthy = Mathf.Abs(map - CirculationSim.RestingMap) < HealthyBandTolerance;
            _healthyHeldFor = healthy ? _healthyHeldFor + dt : 0f;

            if (_healthyHeldFor >= HealthySustainSeconds)
            {
                World.Loop.Evidence.Record(BodyExpeditionEvidence.PatientRestored);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.3f);
                World.Companion?.Voice?.Settle(World.WorldAnchor.position);
                _mode = Mode.Idle;
                Debug.Log("[PRISM] Apply: pressure restored and held.");
            }
        }

        void EvaluatePrediction(float dt)
        {
            if (_predictEdge < 0) return;

            if (!_markerPlaced)
            {
                var hands = World.Hands;
                if (hands == null) return;
                TryDragMarker(hands.Left);
                TryDragMarker(hands.Right);
                return;
            }

            _predictSettle += dt;
            if (_predictSettle < PredictionSettleSeconds) return;

            Vector3 targetWorld = World.WorldAnchor.TransformPoint(BodyExpeditionWorld.NodeLocalPos[_predictTargetNode]);
            float err = Vector3.Distance(targetWorld, _markerWorldPoint);
            bool good = err < PredictionTolerance;

            if (good)
            {
                World.Loop.Evidence.Record(BodyExpeditionEvidence.PredictionGood);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.35f);
                World.Companion?.Voice?.Consonance(_markerWorldPoint, 0.8f);
                _markerView.gameObject.SetActive(false);
                _mode = Mode.Idle;
                Debug.Log($"[PRISM] Explain: correct, {err * 100f:0.0} cm out.");
            }
            else
            {
                World.Knowledge?.FlagMisconception(World.PrimaryConceptId, 0.15f);
                World.Companion?.Voice?.Tension(_markerWorldPoint);
                World.Sim.Edges[_predictEdge].RadiusMult = 1f;
                Debug.Log($"[PRISM] Explain: {err * 100f:0.0} cm out; try again.");
                BeginPrediction();
            }
        }

        void TryDragMarker(Prism.Interaction.PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked) return;
            if (h.Pinch > 0.6f)
            {
                _markerWorldPoint = h.Position;
                _markerView.position = _markerWorldPoint;
            }
            else if (h.PinchUp)
            {
                _markerPlaced = true;
                World.Sim.Edges[_predictEdge].RadiusMult = 0.35f;
                _predictSettle = 0f;
                Debug.Log("[PRISM] Explain: prediction locked; the vessel is narrowing.");
            }
        }

        void EvaluateBalance(float dt)
        {
            float a = World.Sim.Flow(World.Sim.Edges[CirculationSim.EArmVenous]);
            float h = World.Sim.Flow(World.Sim.Edges[CirculationSim.EHeadVenous]);
            float l = World.Sim.Flow(World.Sim.Edges[CirculationSim.ELegLVenous]);
            float r = World.Sim.Flow(World.Sim.Edges[CirculationSim.ELegRVenous]);
            float mean = (a + h + l + r) / 4f;

            if (mean < 1f) { _balanceHeldFor = 0f; return; }   // network too choked to mean anything yet

            float maxDev = Mathf.Max(Mathf.Max(Mathf.Abs(a - mean), Mathf.Abs(h - mean)),
                                     Mathf.Max(Mathf.Abs(l - mean), Mathf.Abs(r - mean)));
            bool balanced = (maxDev / mean) < BalanceToleranceFraction;
            _balanceHeldFor = balanced ? _balanceHeldFor + dt : 0f;

            if (_balanceHeldFor >= BalanceSustainSeconds)
            {
                World.Loop.Evidence.Record(BodyExpeditionEvidence.TreeBalanced);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.4f);
                World.Companion?.Voice?.Settle(World.WorldAnchor.position);
                _mode = Mode.Idle;
                Debug.Log("[PRISM] Create: the tree delivers evenly.");
            }
        }
    }
}
