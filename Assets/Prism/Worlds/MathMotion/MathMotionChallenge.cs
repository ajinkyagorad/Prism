using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.MathMotion
{
    /// <summary>
    /// Apply, Explain and Create. All three are checked against the live CurveField, never
    /// against an answer the learner types - see OrbitalChallenge for the pattern this follows.
    ///
    /// None of the geometry here needs the per-vertex sign-of-derivative colouring the curves
    /// use, so none of it uses Prism/MathMotionCurve: target markers reuse Ceramic and Volumetric
    /// bodies and a Flow/Constrains tube, exactly as the orbital world's ring challenge does.
    /// </summary>
    public class MathMotionChallenge : MonoBehaviour
    {
        public MathMotionWorld World;

        // ---- Apply: two independent targets, evaluated concurrently ----
        float _targetMax;
        float _targetArea;
        float _maxHoldFor, _areaHoldFor;
        const float TargetHoldDuration = 1.2f;
        const float MaxTolerance = 0.05f;
        const float AreaTolerance = 0.06f;

        Mesh _maxBandMesh;
        Transform _maxBandView;
        Material _maxBandMat;
        Transform _areaMarkerTarget, _areaMarkerCurrent;
        Material _areaMarkerTargetMat, _areaMarkerCurrentMat;

        // ---- Explain: predict a zero crossing, then deform before trying again ----
        Transform _markerView;
        Material _markerMat;
        int _predictTargetIndex = -1;
        bool _markerPlaced;
        bool _markerTouched;
        float _markerX;
        bool _hadWrongAttempt;
        int _deformCountAtLastAttempt = -1;
        const float PredictionTolerance = 0.09f;   // abstract x-units

        // ---- Create: hold a hump and a dip of the learner's own making ----
        float _stableFor;
        const float StableDuration = 4f;

        // Lazy rather than event-only: a learner resuming a saved session can arrive at Apply or
        // Explain with no LoopStage transition ever firing (see MathMotionWorld.UpdateVisibility
        // for the same reasoning applied to the sweep/trace views), so each challenge starts
        // itself the first time its own Evaluate sees the right stage, live transition or not.
        bool _targetsStarted;
        bool _predictionStarted;

        void Awake()
        {
            BuildApplyVisuals();
            BuildMarker();
        }

        void BuildApplyVisuals()
        {
            _maxBandMat = PrismMaterials.ForRelation(Relation.Constrains, 0.85f);
            _maxBandMesh = new Mesh { name = "TargetMaxBand" };
            _maxBandMesh.MarkDynamic();
            var mgo = new GameObject("TargetMaxBand");
            mgo.transform.SetParent(transform, false);
            mgo.AddComponent<MeshFilter>().sharedMesh = _maxBandMesh;
            mgo.AddComponent<MeshRenderer>().sharedMaterial = _maxBandMat;
            _maxBandView = mgo.transform;
            mgo.SetActive(false);

            _areaMarkerTargetMat  = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.5f);
            _areaMarkerCurrentMat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.4f);

            var tgo = new GameObject("AreaTargetMarker");
            tgo.transform.SetParent(transform, false);
            tgo.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            tgo.AddComponent<MeshRenderer>().sharedMaterial = _areaMarkerTargetMat;
            _areaMarkerTarget = tgo.transform;
            tgo.SetActive(false);

            var cgo = new GameObject("AreaCurrentMarker");
            cgo.transform.SetParent(transform, false);
            cgo.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            cgo.AddComponent<MeshRenderer>().sharedMaterial = _areaMarkerCurrentMat;
            _areaMarkerCurrent = cgo.transform;
            cgo.SetActive(false);
        }

        void BuildMarker()
        {
            var mat = PrismMaterials.New(PrismMaterials.Volumetric);
            mat.SetColor("_Tint", PrismPalette.Lavender);
            mat.SetColor("_EdgeTint", PrismPalette.Gold);
            mat.SetFloat("_Density", 0.7f);
            _markerMat = mat;

            var go = new GameObject("ZeroCrossingMarker");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * 0.016f;
            _markerView = go.transform;
            go.SetActive(false);
        }

        // -----------------------------------------------------------------

        public void BeginTargets()
        {
            var curve = World.Curve;

            // Built under `transform` in Awake (before World was wired); moved under the real
            // Anchor now, exactly as OrbitalChallenge re-parents its ring at BeginRingChallenge.
            _maxBandView.SetParent(World.WorldAnchor, false);
            _areaMarkerTarget.SetParent(World.WorldAnchor, false);
            _areaMarkerCurrent.SetParent(World.WorldAnchor, false);

            // A maximum target is always positive - "make a hump this tall" is the natural,
            // uncontrived reading of "a specific maximum".
            _targetMax = Random.Range(0.4f, 0.9f);
            if (Mathf.Abs(_targetMax - curve.MaxValue()) < MaxTolerance * 2f)
                _targetMax = curve.MaxValue() > 0.65f ? Random.Range(0.4f, 0.55f) : Random.Range(0.7f, 0.9f);

            _targetArea = Random.Range(0.2f, 0.5f) * (Random.value < 0.5f ? 1f : -1f);
            if (Mathf.Abs(_targetArea - curve.TotalArea()) < AreaTolerance * 2f)
                _targetArea = -_targetArea;

            _maxHoldFor = 0f;
            _areaHoldFor = 0f;

            BuildMaxBand();
            PositionAreaMarkers();

            _maxBandView.gameObject.SetActive(true);
            _areaMarkerTarget.gameObject.SetActive(true);
            _areaMarkerCurrent.gameObject.SetActive(true);

            Debug.Log($"[PRISM] Apply: reach a maximum of {_targetMax:0.00} and a total area of {_targetArea:0.00}.");
        }

        void BuildMaxBand()
        {
            var pts = new List<Vector3>
            {
                World.FunctionPoint(CurveField.DomainMin, _targetMax),
                World.FunctionPoint(CurveField.DomainMax, _targetMax)
            };
            PrismMesh.Tube(pts, 0.0026f, 6, _maxBandMesh);
        }

        void PositionAreaMarkers()
        {
            float h = Mathf.Max(0.006f, Mathf.Abs(_targetArea) * MathMotionWorld.FnYScale);
            float centreY = MathMotionWorld.FnBaselineY + Mathf.Sign(_targetArea) * h * 0.5f;
            _areaMarkerTarget.localPosition = new Vector3(MathMotionWorld.XScale + 0.09f, centreY, MathMotionWorld.CurveZ);
            _areaMarkerTarget.localScale = new Vector3(0.014f, h, 0.014f);
        }

        void UpdateAreaMarkerCurrent(float currentArea)
        {
            float h = Mathf.Max(0.006f, Mathf.Abs(currentArea) * MathMotionWorld.FnYScale);
            float centreY = Mathf.Abs(currentArea) < 1e-4f
                ? MathMotionWorld.FnBaselineY
                : MathMotionWorld.FnBaselineY + Mathf.Sign(currentArea) * h * 0.5f;
            _areaMarkerCurrent.localPosition = new Vector3(MathMotionWorld.XScale + 0.125f, centreY, MathMotionWorld.CurveZ);
            _areaMarkerCurrent.localScale = new Vector3(0.014f, h, 0.014f);
            _areaMarkerCurrentMat.SetColor("_Tint", currentArea >= 0f ? PrismPalette.Mint : PrismPalette.Coral);
        }

        public void BeginPrediction()
        {
            _markerView.SetParent(World.WorldAnchor, false);
            _markerPlaced = false;
            _markerTouched = false;

            var curve = World.Curve;
            bool hasMax = curve.BestMaximum(out int maxIdx, 0.05f);
            bool hasMin = curve.BestMinimum(out int minIdx, 0.05f);

            if (!hasMax && !hasMin)
            {
                // Nothing to predict yet - EvaluatePrediction will keep asking until the curve
                // has a feature, exactly as the orbital world waits for something to be in orbit.
                _predictTargetIndex = -1;
                _markerView.gameObject.SetActive(false);
                return;
            }

            _predictTargetIndex = (hasMax && hasMin)
                ? (Mathf.Abs(curve.X[maxIdx]) < Mathf.Abs(curve.X[minIdx]) ? maxIdx : minIdx)
                : (hasMax ? maxIdx : minIdx);

            _markerView.gameObject.SetActive(true);
            Debug.Log("[PRISM] Explain: mark where the derivative crosses zero.");
        }

        // -----------------------------------------------------------------

        public void Evaluate(float dt)
        {
            EvaluateTargets(dt);
            EvaluatePrediction(dt);
            EvaluateConstruction(dt);
        }

        void EvaluateTargets(float dt)
        {
            if (World.Loop.Stage < LoopStage.Apply) return;
            if (!_targetsStarted) { _targetsStarted = true; BeginTargets(); }

            bool maxDone = World.Loop.Evidence.Has(MathMotionEvidence.AppliedMax);
            bool areaDone = World.Loop.Evidence.Has(MathMotionEvidence.AppliedArea);
            if (maxDone && areaDone) return;

            var curve = World.Curve;

            if (!maxDone)
            {
                bool onTarget = Mathf.Abs(curve.MaxValue() - _targetMax) < MaxTolerance;
                _maxHoldFor = onTarget ? _maxHoldFor + dt : 0f;
                if (_maxHoldFor >= TargetHoldDuration)
                {
                    World.Loop.Evidence.Record(MathMotionEvidence.AppliedMax);
                    World.Knowledge?.Confirm(World.PrimaryConceptId, 0.25f);
                    _maxBandView.gameObject.SetActive(false);
                    Debug.Log("[PRISM] Apply: target maximum reached and held.");
                }
            }

            if (!areaDone)
            {
                float area = curve.TotalArea();
                UpdateAreaMarkerCurrent(area);
                bool onTarget = Mathf.Abs(area - _targetArea) < AreaTolerance;
                _areaHoldFor = onTarget ? _areaHoldFor + dt : 0f;
                if (_areaHoldFor >= TargetHoldDuration)
                {
                    World.Loop.Evidence.Record(MathMotionEvidence.AppliedArea);
                    World.Knowledge?.Confirm(World.PrimaryConceptId, 0.25f);
                    _areaMarkerTarget.gameObject.SetActive(false);
                    _areaMarkerCurrent.gameObject.SetActive(false);
                    Debug.Log("[PRISM] Apply: target area reached and held.");
                }
            }
        }

        void EvaluatePrediction(float dt)
        {
            if (World.Loop.Stage < LoopStage.Explain) return;
            if (World.Loop.Evidence.Has(MathMotionEvidence.PredictionGood)) return;
            if (!_predictionStarted) { _predictionStarted = true; BeginPrediction(); }

            if (_predictTargetIndex < 0) { BeginPrediction(); return; }

            if (_hadWrongAttempt)
            {
                // "Then deform": a fresh attempt is only offered once the curve has genuinely
                // changed since the last wrong guess, so the marker cannot be brute-forced onto a
                // static target.
                if (World.Loop.Evidence.Count(MathMotionEvidence.Deform) <= _deformCountAtLastAttempt) return;
                _hadWrongAttempt = false;
                BeginPrediction();
                return;
            }

            if (!_markerPlaced)
            {
                var hands = World.Hands;
                if (hands == null) return;
                if (!TryTrackMarker(hands.Left)) TryTrackMarker(hands.Right);
                return;
            }

            float err = Mathf.Abs(_markerX - World.Curve.X[_predictTargetIndex]);
            bool good = err < PredictionTolerance;

            if (good)
            {
                World.Loop.Evidence.Record(MathMotionEvidence.PredictionGood);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.35f);
                _markerView.gameObject.SetActive(false);
                Debug.Log($"[PRISM] Explain: prediction correct, {err:0.00} out.");
            }
            else
            {
                World.Knowledge?.FlagMisconception(World.PrimaryConceptId, 0.15f);
                Debug.Log($"[PRISM] Explain: prediction {err:0.00} out; deform the curve, then try again.");
                _markerPlaced = false;
                _hadWrongAttempt = true;
                _deformCountAtLastAttempt = World.Loop.Evidence.Count(MathMotionEvidence.Deform);
            }
        }

        bool TryTrackMarker(PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked) return false;

            if (h.Pinch > 0.6f)
            {
                Vector3 local = World.WorldAnchor.InverseTransformPoint(PrismHands.PointOf(h));

                // Gated to the derivative's own plane, exactly like TrySweepGrab - both the bead
                // and the curve itself remain grabbable this late in the loop, and without this a
                // pinch meant for sculpting the function curve would also yank the marker across
                // the derivative baseline underneath it.
                if (!_markerTouched)
                {
                    if (local.z < MathMotionWorld.CurveZ - 0.12f || local.z > MathMotionWorld.CurveZ + 0.12f) return false;
                    if (local.y < MathMotionWorld.DerivBaselineY - 0.14f || local.y > MathMotionWorld.DerivBaselineY + 0.14f) return false;
                }

                _markerX = Mathf.Clamp(local.x / MathMotionWorld.XScale, CurveField.DomainMin, CurveField.DomainMax);
                _markerView.localPosition = new Vector3(_markerX * MathMotionWorld.XScale,
                                                        MathMotionWorld.DerivBaselineY, MathMotionWorld.CurveZ);
                _markerTouched = true;
                return true;
            }
            if (h.PinchUp && _markerTouched)
            {
                _markerPlaced = true;
                _markerTouched = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Create: a curve of the learner's own, with both a hump and a dip, held without active
        /// editing for a few seconds so it is clearly a finished choice rather than a mid-drag
        /// transient. Nothing is prescribed about shape or position - that is what makes it theirs.
        /// </summary>
        void EvaluateConstruction(float dt)
        {
            if (World.Loop.Stage < LoopStage.Create) return;
            if (World.Loop.Evidence.Has(MathMotionEvidence.Constructed)) return;

            var curve = World.Curve;
            bool hasMax = curve.BestMaximum(out _, 0.08f);
            bool hasMin = curve.BestMinimum(out _, 0.08f);
            bool qualifies = hasMax && hasMin && !World.IsBeingDeformed;

            _stableFor = qualifies ? _stableFor + dt : 0f;
            if (_stableFor >= StableDuration)
            {
                World.Loop.Evidence.Record(MathMotionEvidence.Constructed);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.4f);
                Debug.Log("[PRISM] Create: a curve of the learner's own, held stable.");
            }
        }
    }
}
