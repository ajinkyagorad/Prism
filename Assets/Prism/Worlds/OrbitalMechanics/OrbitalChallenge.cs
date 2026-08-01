using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Worlds.Orbital
{
    /// <summary>
    /// The Apply, Explain and Create stages.
    ///
    /// All three are checked against the simulation rather than against an answer the learner
    /// types. The distinction matters most in Explain: the learner is asked to predict where a
    /// moon will be, and the world simply waits and looks. A wrong prediction is recorded as a
    /// misconception on the concept — which shows up as an unstable structure in the atrium — and
    /// then they may try again. Nothing is scored and nothing is lost.
    /// </summary>
    public class OrbitalChallenge : MonoBehaviour
    {
        public OrbitalWorld World;

        enum Mode { Idle, Ring, Prediction, Construction }
        Mode _mode = Mode.Idle;

        // ---- ring challenge ----
        Transform _ringView;
        Material _ringMat;
        float _ringRadius;
        float _ringHeldFor;
        const float RingRadiusTolerance = 0.022f;      // metres
        const float RingEccentricityTolerance = 0.10f;

        // ---- prediction ----
        Transform _markerView;
        Material _markerMat;
        OrbitalWorld.Moon _predictMoon;
        float _predictLead;
        float _predictElapsed;
        bool _markerPlaced;
        Vector3 _markerPoint;
        const float PredictionTolerance = 0.045f;      // metres

        // ---- construction ----
        const float ConstructionPeriods = 3f;

        void Awake()
        {
            BuildRing();
            BuildMarker();
        }

        void BuildRing()
        {
            var go = new GameObject("TargetRing");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = "TargetRing" };
            _ringMat = PrismMaterials.ForRelation(Relation.Constrains, 0.9f);
            _ringMat.SetFloat("_Packets", 6f);
            go.AddComponent<MeshRenderer>().sharedMaterial = _ringMat;
            _ringView = go.transform;
            go.SetActive(false);
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
            go.transform.localScale = Vector3.one * 0.022f;
            _markerView = go.transform;
            go.SetActive(false);
        }

        // -----------------------------------------------------------------

        public void BeginRingChallenge(float radius)
        {
            _mode = Mode.Ring;
            _ringRadius = radius;
            _ringHeldFor = 0f;

            var pts = new List<Vector3>(97);
            for (int i = 0; i <= 96; i++)
            {
                float a = (float)i / 96f * Mathf.PI * 2f;
                pts.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            PrismMesh.Tube(pts, 0.0018f, 6, _ringView.GetComponent<MeshFilter>().sharedMesh);

            _ringView.SetParent(World.Anchor, false);
            _ringView.localPosition = Vector3.zero;
            _ringView.localRotation = Quaternion.identity;
            _ringView.gameObject.SetActive(true);

            Debug.Log($"[PRISM] Apply: park a moon on the {radius * 100f:0} cm ring " +
                      $"(circular speed there is {PrismScale.CircularSpeed(radius):0.000} m/s).");
        }

        public void BeginPrediction()
        {
            _mode = Mode.Prediction;
            _markerPlaced = false;
            _predictMoon = null;
            _predictElapsed = 0f;

            // Prefer an eccentric orbit: on a circle every point looks alike, so a correct
            // prediction would not distinguish understanding from guessing. On an ellipse the
            // learner has to know that the moon moves fastest near the planet.
            float bestScore = -1f;
            foreach (var m in World.Moons)
            {
                if (m.Idle || m.Held || !m.P.Active || !m.Elements.IsBound) continue;
                float score = m.Elements.Eccentricity;
                if (score > bestScore) { bestScore = score; _predictMoon = m; }
            }

            if (_predictMoon == null)
            {
                // Nothing in orbit to predict about. Wait for the learner to make something.
                _mode = Mode.Idle;
                return;
            }

            _predictLead = Mathf.Min(_predictMoon.Elements.Period * 0.45f, 4.5f);
            _markerView.gameObject.SetActive(true);
            Debug.Log($"[PRISM] Explain: place a marker where that moon will be in " +
                      $"{_predictLead:0.0} s (e = {_predictMoon.Elements.Eccentricity:0.00}).");
        }

        public void Evaluate(float dt)
        {
            switch (_mode)
            {
                case Mode.Ring:        EvaluateRing(dt);        break;
                case Mode.Prediction:  EvaluatePrediction(dt);  break;
                default:               EvaluateConstruction(dt); break;
            }
        }

        void EvaluateRing(float dt)
        {
            var target = PrismScale.CircularPeriod(_ringRadius);
            bool any = false;

            foreach (var m in World.Moons)
            {
                if (m.Idle || m.Held || !m.P.Active) continue;
                float r = (m.P.Position - World.PlanetPosition).magnitude;
                bool onRing = Mathf.Abs(r - _ringRadius) < RingRadiusTolerance
                           && m.Elements.IsBound
                           && m.Elements.Eccentricity < RingEccentricityTolerance;
                if (onRing) { any = true; break; }
            }

            _ringHeldFor = any ? _ringHeldFor + dt : 0f;

            // Ring brightens as the moon holds station: feedback without a progress bar.
            _ringMat.SetFloat("_Pulse", Mathf.Clamp01(_ringHeldFor / target));

            if (_ringHeldFor >= target)
            {
                // ORDER MATTERS HERE, and getting it wrong is silent.
                //
                // Evidence.Record() is synchronous: it satisfies the Apply gate, which enters
                // Explain, which calls OnStageEntered -> BeginPrediction() -> _mode = Prediction,
                // all on THIS call stack. Any assignment to _mode after the Record() therefore
                // overwrites the stage that just began, and the Explain stage silently never runs.
                // So everything this mode owns is torn down BEFORE the evidence is recorded.
                // (Found by the Living Cell module, which hit the identical trap.)
                _ringMat.SetFloat("_Pulse", 1f);
                _ringView.gameObject.SetActive(false);
                _mode = Mode.Idle;

                World.Knowledge?.Confirm(World.ConceptId, 0.3f);
                Debug.Log("[PRISM] Apply: ring held for a full period.");
                World.Loop.Evidence.Record(OrbitalEvidence.ChallengeDone);   // may enter Explain
            }
        }

        void EvaluatePrediction(float dt)
        {
            if (_predictMoon == null || !_predictMoon.P.Active) { _mode = Mode.Idle; _markerView.gameObject.SetActive(false); return; }

            var hands = World.Hands;
            if (!_markerPlaced && hands != null)
            {
                // The marker follows whichever hand is pinching, and locks where it is released.
                foreach (var h in new[] { hands.Left, hands.Right })
                {
                    if (h == null || !h.IsTracked) continue;
                    if (h.Pinch > 0.6f) { _markerPoint = h.Position; _markerView.position = _markerPoint; }
                    else if (h.PinchUp && _markerView.position != Vector3.zero)
                    {
                        _markerPlaced = true;
                        _predictElapsed = 0f;
                        Debug.Log("[PRISM] Explain: prediction locked; waiting.");
                    }
                }
                return;
            }

            if (!_markerPlaced) return;

            _predictElapsed += dt;
            if (_predictElapsed < _predictLead) return;

            float err = (_predictMoon.P.Position - _markerPoint).magnitude;
            bool good = err < PredictionTolerance;

            if (good)
            {
                // Same ordering rule as EvaluateRing: tear down first, record last.
                _markerView.gameObject.SetActive(false);
                _mode = Mode.Idle;

                World.Knowledge?.Confirm(World.ConceptId, 0.35f);
                Debug.Log($"[PRISM] Explain: prediction correct, {err * 100f:0.0} cm out.");
                World.Loop.Evidence.Record(OrbitalEvidence.PredictionGood);  // may enter Create
            }
            else
            {
                // Being wrong here is the most informative thing that can happen, and it is
                // recorded as a misconception rather than as a failed attempt: the atrium will
                // show this concept as not yet settled, which is true.
                World.Knowledge?.FlagMisconception(World.ConceptId, 0.15f);
                Debug.Log($"[PRISM] Explain: prediction {err * 100f:0.0} cm out; try again.");
                _markerPlaced = false;
                _predictElapsed = 0f;
                BeginPrediction();
            }
        }

        /// <summary>
        /// Create: a system of the learner's own that survives. Two or more moons simultaneously
        /// bound, each for three of its own periods. Nothing is prescribed about what the system
        /// should look like — that is what makes it theirs.
        /// </summary>
        void EvaluateConstruction(float dt)
        {
            if (World.Loop.Stage < LoopStage.Create) return;
            if (World.Loop.Evidence.Has(OrbitalEvidence.Construction)) return;

            int stable = 0;
            foreach (var m in World.Moons)
            {
                if (m.Idle || m.Held || !m.P.Active || !m.Elements.IsBound) continue;
                if (float.IsInfinity(m.Elements.Period)) continue;
                if (m.BoundFor >= m.Elements.Period * ConstructionPeriods) stable++;
            }

            if (stable >= 2)
            {
                World.Loop.Evidence.Record(OrbitalEvidence.Construction);
                World.Knowledge?.Confirm(World.ConceptId, 0.4f);
                Debug.Log($"[PRISM] Create: {stable} moons holding stable orbits.");
            }
        }
    }
}
