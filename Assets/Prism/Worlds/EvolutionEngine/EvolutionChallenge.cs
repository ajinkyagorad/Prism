using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Worlds.EvolutionEngine
{
    /// <summary>
    /// Apply, Explain and Create, checked against the simulation rather than against an answer
    /// typed or spoken — mirrors <c>OrbitalChallenge</c>'s shape closely: one mode at a time, one
    /// <see cref="Evaluate"/> dispatcher, and in Explain a wrong guess is recorded as a
    /// misconception rather than as a failure, with an automatic second try.
    ///
    /// Geometry is built once in <see cref="Awake"/>, parented under this component's own
    /// transform (so it needs nothing from <see cref="World"/> yet), and only reparented under the
    /// world anchor and activated when a challenge actually begins — the same deferral Orbital's
    /// challenge uses, because <see cref="World"/> is not assigned until immediately after
    /// <c>AddComponent</c> returns, which is after <c>Awake</c> has already run.
    /// </summary>
    public class EvolutionChallenge : MonoBehaviour
    {
        public EvolutionEngineWorld World;

        enum Mode { Idle, Target, Predict }
        Mode _mode = Mode.Idle;

        // ---- Apply: shape the population to a target trait, by environment alone ----
        Mesh _targetMesh;
        Transform _targetRing;
        Material _targetMat;
        float _targetU;
        float _targetHeldFor;
        const float TargetToleranceU = 0.09f;
        const float TargetHoldSeconds = 9f;

        // ---- Explain: predict which way the mean moves, then watch generations run ----
        Transform _predictMarker;
        bool _markerCommitted;
        bool _awaitingChange;
        float _predictedU;
        float _tempAtCommit;
        bool _predatorAtCommit;
        float _settleTimer;
        const float SettleLeadSeconds = 13f;
        const float TempChangeThreshold = 0.16f;
        const float PredictionToleranceU = 0.11f;

        // ---- Create: two separated, sustained peaks in the living distribution ----
        float _bimodalHeldFor;
        const float BimodalHoldSeconds = 7f;
        const int MinPeakCount = 5;

        void Awake()
        {
            BuildTargetRing();
            BuildPredictMarker();
        }

        void BuildTargetRing()
        {
            _targetMesh = new Mesh { name = "EvolutionTarget" };
            _targetMesh.MarkDynamic();

            var go = new GameObject("Target");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _targetMesh;
            _targetMat = PrismMaterials.ForRelation(Relation.Constrains, 0.9f);
            _targetMat.SetFloat("_Packets", 5f);
            go.AddComponent<MeshRenderer>().sharedMaterial = _targetMat;
            _targetRing = go.transform;
            go.SetActive(false);
        }

        void BuildPredictMarker()
        {
            var mat = PrismMaterials.New(PrismMaterials.Volumetric);
            mat.SetColor("_Tint", PrismPalette.Lavender);
            mat.SetColor("_EdgeTint", PrismPalette.Gold);
            mat.SetFloat("_Density", 0.75f);

            var go = new GameObject("PredictionMarker");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * 0.02f;
            _predictMarker = go.transform;
            go.SetActive(false);
        }

        // -----------------------------------------------------------------

        /// <summary>
        /// Apply: hold the population's mean trait inside a target band, using only the
        /// environment, for a sustained duration. The target alternates between a "large" and a
        /// "small" band so the learner cannot solve every visit the same way.
        /// </summary>
        public void BeginTargetChallenge()
        {
            _mode = Mode.Target;
            _targetHeldFor = 0f;
            _targetU = Random.value < 0.5f ? 0.76f : 0.24f;

            var anchor = World.WorldAnchor;
            var hist = World.Histogram;
            float x = EvolutionHistogram.TraitToLocalX(EvolutionSim.U01ToTrait(_targetU));
            float y = EvolutionHistogram.MaxBarHeight * 0.55f;
            Vector3 centre = hist.AxisLocalCentre + new Vector3(x, y, -0.03f);

            var pts = new List<Vector3>(33);
            for (int i = 0; i <= 32; i++)
            {
                float a = (float)i / 32f * Mathf.PI * 2f;
                pts.Add(centre + new Vector3(Mathf.Cos(a) * 0.035f, Mathf.Sin(a) * 0.035f, 0f));
            }
            PrismMesh.Tube(pts, 0.0016f, 6, _targetMesh);

            _targetRing.SetParent(anchor, false);
            _targetRing.localPosition = Vector3.zero;
            _targetRing.localRotation = Quaternion.identity;
            _targetRing.gameObject.SetActive(true);

            Debug.Log($"[PRISM] Apply: hold the population toward trait ~" +
                      $"{EvolutionSim.U01ToTrait(_targetU):0.00} using the environment alone.");
        }

        /// <summary>
        /// Explain: place a marker where the population's trait will settle, then make a change
        /// and let it play out. The countdown to checking the guess does not start until an actual
        /// change is detected, so predicting "nothing happens" can never trivially score — the
        /// learner has to test the prediction, not just state one.
        /// </summary>
        public void BeginPrediction()
        {
            _mode = Mode.Predict;
            _markerCommitted = false;
            _awaitingChange = false;
            _settleTimer = 0f;

            var anchor = World.WorldAnchor;
            var hist = World.Histogram;
            float x = EvolutionHistogram.TraitToLocalX(World.Sim.MeanTrait);
            Vector3 pos = hist.AxisLocalCentre + new Vector3(x, EvolutionHistogram.MaxBarHeight * 0.75f, 0.04f);

            _predictMarker.SetParent(anchor, false);
            _predictMarker.localPosition = pos;
            _predictMarker.gameObject.SetActive(true);

            Debug.Log("[PRISM] Explain: place the marker where the trait will settle, then change something.");
        }

        public void Evaluate(float dt)
        {
            switch (_mode)
            {
                case Mode.Target:  EvaluateTarget(dt);  break;
                case Mode.Predict: EvaluatePredict(dt); break;
                default:           EvaluateCreate(dt);  break;
            }
        }

        void EvaluateTarget(float dt)
        {
            float u = EvolutionSim.TraitToU01(World.Sim.MeanTrait);
            bool inBand = World.Sim.AliveCount > 6 && Mathf.Abs(u - _targetU) < TargetToleranceU;
            _targetHeldFor = inBand ? _targetHeldFor + dt : Mathf.Max(0f, _targetHeldFor - dt * 0.5f);

            // Brightens as the population holds station, feedback without a progress bar.
            _targetMat.SetFloat("_Pulse", Mathf.Clamp01(_targetHeldFor / TargetHoldSeconds));

            if (_targetHeldFor >= TargetHoldSeconds)
            {
                World.Loop.Evidence.Record(EvolutionEvidence.ApplyDone);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.3f);
                World.Companion?.Voice?.Settle(_targetRing.position, 0.6f);
                _targetRing.gameObject.SetActive(false);
                _mode = Mode.Idle;
                Debug.Log("[PRISM] Apply: held the target trait for the full duration.");
            }
        }

        void EvaluatePredict(float dt)
        {
            var hands = World.Hands;

            if (!_markerCommitted)
            {
                if (hands != null)
                {
                    foreach (var h in new[] { hands.Left, hands.Right })
                    {
                        if (h == null || !h.IsTracked) continue;
                        if (h.Pinch > 0.6f) _predictMarker.position = h.Position;
                        else if (h.PinchUp) { CommitPrediction(); break; }
                    }
                }
                return;
            }

            if (_awaitingChange)
            {
                bool tempChanged = Mathf.Abs(World.Sim.Temperature - _tempAtCommit) > TempChangeThreshold;
                bool predatorChanged = World.Sim.PredatorActive != _predatorAtCommit;
                if (tempChanged || predatorChanged)
                {
                    _awaitingChange = false;
                    _settleTimer = 0f;
                }
                return;
            }

            _settleTimer += dt;
            if (_settleTimer < SettleLeadSeconds) return;

            float actualU = EvolutionSim.TraitToU01(World.Sim.MeanTrait);
            bool good = Mathf.Abs(actualU - _predictedU) < PredictionToleranceU;

            if (good)
            {
                World.Loop.Evidence.Record(EvolutionEvidence.PredictionGood);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.35f);
                World.Companion?.Voice?.Consonance(_predictMarker.position, 0.6f, 1.1f);
                _predictMarker.gameObject.SetActive(false);
                _mode = Mode.Idle;
                Debug.Log("[PRISM] Explain: prediction correct.");
            }
            else
            {
                // Being wrong here is the most informative thing that can happen, and it is
                // recorded as a misconception rather than as a failed attempt — spatial tension,
                // not an error buzzer.
                World.Knowledge?.FlagMisconception(World.PrimaryConceptId, 0.15f);
                World.Companion?.Voice?.Tension(_predictMarker.position, 0.45f);
                Debug.Log("[PRISM] Explain: prediction was off; try again.");
                BeginPrediction();
            }
        }

        void CommitPrediction()
        {
            _markerCommitted = true;
            var anchor = World.WorldAnchor;
            float localX = anchor.InverseTransformPoint(_predictMarker.position).x - World.Histogram.AxisLocalCentre.x;
            _predictedU = EvolutionSim.TraitToU01(EvolutionHistogram.LocalXToTrait(localX));
            _tempAtCommit = World.Sim.Temperature;
            _predatorAtCommit = World.Sim.PredatorActive;
            _awaitingChange = true;
            _settleTimer = 0f;
            Debug.Log($"[PRISM] Explain: prediction locked near trait {EvolutionSim.U01ToTrait(_predictedU):0.00}; " +
                      "waiting for a change.");
        }

        /// <summary>
        /// Create: nothing is prescribed about what the environment should look like — that is
        /// what makes it theirs. This only watches the living distribution for two separated,
        /// sustained peaks, which is what a learner's own disruptive-selection setup produces.
        /// </summary>
        void EvaluateCreate(float dt)
        {
            if (World.Loop.Stage < LoopStage.Create) return;
            if (World.Loop.Evidence.Has(EvolutionEvidence.CreateBimodal)) return;

            bool bimodal = HasTwoSeparatedPeaks(World.Sim.Bins, World.Sim.AliveCount);
            _bimodalHeldFor = bimodal ? _bimodalHeldFor + dt : 0f;

            if (_bimodalHeldFor >= BimodalHoldSeconds)
            {
                World.Loop.Evidence.Record(EvolutionEvidence.CreateBimodal);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.4f);
                if (World.Head != null)
                    World.Companion?.Voice?.Consonance(World.Head.transform.position, 0.5f, 0.9f);
                Debug.Log("[PRISM] Create: two separated, sustained forms in the living population.");
            }
        }

        static bool HasTwoSeparatedPeaks(IReadOnlyList<int> bins, int totalAlive)
        {
            if (totalAlive < 16) return false;
            int n = bins.Count;
            int minCount = Mathf.Max(MinPeakCount, Mathf.RoundToInt(totalAlive * 0.09f));

            var isPeak = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (bins[i] < minCount) continue;
                int left = i > 0 ? bins[i - 1] : -1;
                int right = i < n - 1 ? bins[i + 1] : -1;
                if (bins[i] >= left && bins[i] >= right) isPeak[i] = true;
            }

            int firstPeak = -1;
            for (int i = 0; i < n; i++)
            {
                if (!isPeak[i]) continue;
                if (firstPeak < 0) { firstPeak = i; continue; }
                if (i - firstPeak < 2) continue;   // adjacent maxima are one form, not two

                int valley = int.MaxValue;
                for (int j = firstPeak + 1; j < i; j++) valley = Mathf.Min(valley, bins[j]);

                int smaller = Mathf.Min(bins[firstPeak], bins[i]);
                if (valley <= smaller / 2) return true;

                firstPeak = i;   // keep scanning in case a later pair separates more cleanly
            }
            return false;
        }
    }
}
