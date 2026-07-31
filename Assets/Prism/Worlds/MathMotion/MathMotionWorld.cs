using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using Prism.Worlds;
using UnityEngine;

namespace Prism.Worlds.MathMotion
{
    /// <summary>Evidence keys. Named constants because the gates and the world must agree exactly.</summary>
    public static class MathMotionEvidence
    {
        public const string Deform         = "deform";
        public const string MadeMaximum    = "made.maximum";
        public const string MadeSteep      = "made.steep";
        public const string TangentRising  = "tangent.rising";
        public const string TangentFalling = "tangent.falling";
        public const string Swept          = "swept";
        public const string SweptFull      = "swept.full";
        public const string AppliedMax     = "applied.max";
        public const string AppliedArea    = "applied.area";
        public const string PredictionGood = "prediction.good";
        public const string Constructed    = "constructed";
    }

    /// <summary>
    /// Mathematics of Motion: a curve the learner can take hold of, and a second curve that
    /// answers back.
    ///
    /// What the learner is shown, and - more importantly - what they are NOT shown yet:
    ///
    ///   Wonder     A curve hangs in the air. A bead rides along it. A second curve is being
    ///              traced out beneath it as the bead goes. Nothing is labelled and nothing says
    ///              what the second curve is.
    ///   Explore    The curve can be grabbed and reshaped with either hand - a real local edit,
    ///              not a canned animation - and the second curve answers instantly, everywhere,
    ///              including where it should not move at all.
    ///   Discover   Once the learner has personally produced both a maximum (where the second
    ///              curve crosses its own zero) and a steep section (where it spikes), twice each,
    ///              a bead becomes draggable and a tangent line appears: a line that only touches
    ///              the curve, whose slope IS the height of the second curve at that point.
    ///   Formalize  Now the words: derivative, slope, rate of change, and f'(x). Then the reverse
    ///              - sweep a hand under the second curve and watch a running total accumulate,
    ///              drawn as a third curve that lands back on the first. That is the Fundamental
    ///              Theorem of Calculus, watched happening rather than stated.
    ///   Apply      Shape the curve to hit a target maximum AND a target total area, both real
    ///              numbers read from the live simulation.
    ///   Explain    Predict where the derivative crosses zero, mark it, and find out - a wrong
    ///              guess requires a fresh deformation before a new attempt is accepted.
    ///   Create     Build a curve of your own, with both a hump and a dip, and hold it still long
    ///              enough that it is clearly a choice and not an accident.
    ///   Connect    Return to the constellation; derivative and integral now sit lit among vectors,
    ///              periodic motion and orbital mechanics - all genuinely downstream of this.
    ///
    /// COLOUR LAW: hue is the SIGN of the derivative - mint where the function rises, coral where
    /// it falls, warm-neutral at a critical point. One rule, applied identically to the function
    /// curve, the derivative curve, the tangent line, the bead, the sweep fill and the
    /// accumulation trace, so the same colour in two places at the same x is the same number,
    /// not a coincidence. See <see cref="ColourForSign"/>.
    /// </summary>
    public class MathMotionWorld : PrismWorldBase
    {
        // -----------------------------------------------------------------
        // layout (metres, in Anchor-local space; abstract curve units convert via these)
        // -----------------------------------------------------------------
        public const float XScale         = 0.24f;   // metres per abstract x unit (domain is -1..1)
        public const float FnBaselineY    = 0.12f;
        public const float FnYScale       = 0.085f;
        public const float DerivBaselineY = -0.10f;
        public const float CurveZ         = 0f;

        const float DerivDisplayClamp = 6f;      // abstract derivative units clamped for DISPLAY only
        const float DerivYScale       = 0.011f;  // -> max display excursion +/- 0.066 m

        const float TubeRadius       = 0.0032f;
        const float TangentRadius    = 0.0045f;
        const float TangentHalfLength = 0.09f;
        const float BeadRadius       = 0.014f;

        const float GrabRadius     = 0.065f;
        const float BeadGrabRadius = 0.05f;
        const float Sigma          = 0.10f;   // abstract x-units, Gaussian bump width

        const float SteepThreshold      = 3.0f;   // abstract derivative units
        const float TangentSlopeEpsilon = 0.15f;
        const float ColourSaturationScale = 2.2f;
        const float AreaSaturationScale   = 0.6f;
        const float DomainEdgeReveal      = 0.85f;

        const float IntroDuration  = 4.5f;   // seconds - the one-time "drawn as it goes" reveal
        const float BeadRideSpeed  = 0.5f;   // domain-widths per second once free-riding
        const float LabelUpdateInterval = 0.15f;

        // -----------------------------------------------------------------
        // runtime
        // -----------------------------------------------------------------
        CurveField _curve;

        Material _curveMat, _derivMat, _tangentMat, _traceMat, _sweepFillMat, _beadMat;

        ColouredStripMesh _curveMesh, _derivMesh, _tangentMesh, _traceMesh, _sweepFillMesh;
        Transform _curveView, _derivView, _tangentView, _traceView, _sweepFillView, _beadView;

        PrismLabel _tangentLabel, _traceLabel;
        MathMotionChallenge _challenge;

        // reused every frame - never reallocated
        readonly Vector3[] _fnPts        = new Vector3[CurveField.SampleCount];
        readonly Vector3[] _derivPts     = new Vector3[CurveField.SampleCount];
        readonly Color[]   _slopeColours = new Color[CurveField.SampleCount];
        readonly float[]   _traceY       = new float[CurveField.SampleCount];
        readonly Vector3[] _tracePts     = new Vector3[CurveField.SampleCount];
        readonly Vector3[] _tangentPts   = new Vector3[2];
        readonly Color[]   _tangentColours = new Color[2];

        class DragState { public bool Active; public bool DidEdit; }
        readonly DragState _dragLeft  = new DragState();
        readonly DragState _dragRight = new DragState();

        float _beadX;
        bool _beadHeld;
        PrismHands.Hand _beadHand;
        bool _everDeformed;
        float _introT;

        float _sweepEnd = CurveField.DomainMin;
        bool _sweepHeld;
        PrismHands.Hand _sweepHand;

        float _labelTimer;

        // -----------------------------------------------------------------
        // identity
        // -----------------------------------------------------------------
        public override string WorldId => "math-motion";

        /// <summary>Mathematics gets a clean abstract room, not a landscape.</summary>
        protected override WorldShellSpec Shell => WorldShellSpec.Manifold;
        public override string PrimaryConceptId => "math-motion";
        public override string DisplayName => "Mathematics of Motion";
        public override string[] Shaders => new[] { "Prism/MathMotionCurve" };

        /// <summary>What MathMotionChallenge needs from outside - kept minimal and public rather
        /// than making it a subclass, matching OrbitalWorld/OrbitalChallenge's separation.</summary>
        public Transform WorldAnchor => Anchor;
        public CurveField Curve => _curve;
        public bool IsBeingDeformed => _dragLeft.Active || _dragRight.Active;
        public Vector3 FunctionPoint(float xAbstract, float valueAbstract) =>
            new Vector3(xAbstract * XScale, FnBaselineY + valueAbstract * FnYScale, CurveZ);

        protected override void Awake()
        {
            base.Awake();
            // Content sits closer and a little higher than the orbital table: this is something
            // held up and worked on with both hands, not a system watched from above.
            EyeToTable = 0.32f;
            Reach = 0.50f;
        }

        // -----------------------------------------------------------------
        // build
        // -----------------------------------------------------------------
        protected override void BuildWorld()
        {
            _curve = new CurveField();

            BuildMaterials();
            BuildCurveViews();
            BuildBaseline();
            BuildBead();
            BuildLabels();

            _challenge = gameObject.AddComponent<MathMotionChallenge>();
            _challenge.World = this;

            _curve.RecomputeDerivative();
            RebuildCurves();
        }

        void BuildMaterials()
        {
            _curveMat     = MakeCurveMaterial(0.9f, 1f);
            _derivMat     = MakeCurveMaterial(0.85f, 1f);
            _tangentMat   = MakeCurveMaterial(1f, 1.7f);
            _traceMat     = MakeCurveMaterial(0.8f, 1f);
            _sweepFillMat = MakeCurveMaterial(0.32f, 0.4f);
            _sweepFillMat.SetFloat("_EdgeSoft", 1f);
        }

        static Material MakeCurveMaterial(float opacity, float coreGain)
        {
            var m = PrismMaterials.New("Prism/MathMotionCurve");
            m.SetFloat("_Opacity", opacity);
            m.SetFloat("_CoreGain", coreGain);
            return m;
        }

        void BuildCurveViews()
        {
            _curveMesh = new ColouredStripMesh("MathMotionFunctionCurve");
            _curveView = MakeMeshHolder("FunctionCurve", _curveMesh.Mesh, _curveMat);

            _derivMesh = new ColouredStripMesh("MathMotionDerivativeCurve");
            _derivView = MakeMeshHolder("DerivativeCurve", _derivMesh.Mesh, _derivMat);

            _tangentMesh = new ColouredStripMesh("MathMotionTangent");
            _tangentView = MakeMeshHolder("Tangent", _tangentMesh.Mesh, _tangentMat);
            _tangentView.gameObject.SetActive(false);

            _traceMesh = new ColouredStripMesh("MathMotionTrace");
            _traceView = MakeMeshHolder("AccumulationTrace", _traceMesh.Mesh, _traceMat);
            _traceView.gameObject.SetActive(false);

            _sweepFillMesh = new ColouredStripMesh("MathMotionSweepFill");
            _sweepFillView = MakeMeshHolder("SweepFill", _sweepFillMesh.Mesh, _sweepFillMat);
            _sweepFillView.gameObject.SetActive(false);
        }

        Transform MakeMeshHolder(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Anchor, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>A faint static reference for the derivative's own zero line - built once, not
        /// touched again, since it never moves.</summary>
        void BuildBaseline()
        {
            var pts = new List<Vector3>
            {
                new Vector3(-XScale, DerivBaselineY, CurveZ),
                new Vector3( XScale, DerivBaselineY, CurveZ)
            };
            var mesh = new Mesh { name = "MathMotionBaseline" };
            PrismMesh.Tube(pts, TubeRadius * 0.5f, 6, mesh);
            var mat = PrismMaterials.ForRelation(Relation.Constrains, 0.25f);
            MakeMeshHolder("DerivativeBaseline", mesh, mat);
        }

        void BuildBead()
        {
            _beadMat = PrismMaterials.New(PrismMaterials.Seed);
            _beadMat.SetColor("_Tint", PrismPalette.Warm);
            _beadMat.SetFloat("_Growth", 1f);
            _beadMat.SetFloat("_Density", 1f);
            _beadMat.SetFloat("_FilmNm", 340f);
            _beadView = Body(PrismMesh.Icosphere(2), BeadRadius, _beadMat, "Bead");
        }

        void BuildLabels()
        {
            _tangentLabel = PrismLabel.Create("DerivativeLabel", Anchor, Head, 0.014f);
            _traceLabel   = PrismLabel.Create("IntegralLabel", Anchor, Head, 0.014f);
        }

        // -----------------------------------------------------------------
        // loop
        // -----------------------------------------------------------------
        protected override void ConfigureLoop(LearningLoop loop)
        {
            loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet taken hold of the curve",
                e => e.Count(MathMotionEvidence.Deform) >= 1));

            // The gate that matters most, mirroring the orbital world's bound/unbound straddle:
            // the learner must have personally made a maximum happen where the second curve
            // crosses zero, AND made a steep section happen where it spikes - each at least
            // twice, so the correspondence is a pattern they produced rather than a fluke.
            loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet made both a maximum and a steep section, twice each",
                e => e.Count(MathMotionEvidence.MadeMaximum) >= 2
                  && e.Count(MathMotionEvidence.MadeSteep) >= 2));

            loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet dragged the bead through both a rising and a falling tangent",
                e => e.Has(MathMotionEvidence.TangentRising) && e.Has(MathMotionEvidence.TangentFalling)));

            loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet swept the area under the derivative all the way across",
                e => e.Has(MathMotionEvidence.SweptFull)));

            loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet met both the target maximum and the target area",
                e => e.Has(MathMotionEvidence.AppliedMax) && e.Has(MathMotionEvidence.AppliedArea)));

            loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet correctly predicted where the derivative crosses zero",
                e => e.Has(MathMotionEvidence.PredictionGood)));

            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet built and held a curve of their own with both a hump and a dip",
                e => e.Has(MathMotionEvidence.Constructed)));
        }

        protected override void OnStageEntered(LoopStage stage)
        {
            base.OnStageEntered(stage);

            switch (stage)
            {
                case LoopStage.Discover:
                    _tangentView.gameObject.SetActive(true);
                    break;

                case LoopStage.Formalize:
                    _sweepFillView.gameObject.SetActive(true);
                    _traceView.gameObject.SetActive(true);
                    break;

                case LoopStage.Connect:
                    Knowledge?.Confirm(PrimaryConceptId, 0.4f);
                    Debug.Log("[PRISM] Mathematics of Motion: Connect reached. The constellation changes.");
                    break;
            }
        }

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------
        protected override void Tick(float dt)
        {
            if (Hands != null)
            {
                HandleCurveDrag(Hands.Left, _dragLeft);
                HandleCurveDrag(Hands.Right, _dragRight);
            }

            // Recomputed AFTER any edit above and BEFORE anything reads it below, so the second
            // curve answers within the same frame the hand moved - "responds live" is a frame
            // budget, not a figure of speech.
            _curve.RecomputeDerivative();

            UpdateBead(dt);

            if (Loop.Stage >= LoopStage.Discover)
            {
                HandleBeadDrag();
                UpdateTangentAndEvidence();
            }

            RebuildCurves();

            if (Loop.Stage == LoopStage.Formalize)
            {
                HandleSweep();
                UpdateSweepVisuals();
            }

            UpdateVisibility();
            UpdateLabels(dt);

            _challenge?.Evaluate(dt);
        }

        // -----------------------------------------------------------------
        // curve deformation - grab and reshape with either hand, independently
        // -----------------------------------------------------------------
        void HandleCurveDrag(PrismHands.Hand h, DragState state)
        {
            if (h == null || !h.IsTracked)
            {
                if (state.Active) EndCurveDrag(state);
                return;
            }

            bool pinching = h.Pinch > 0.6f;

            if (!state.Active)
            {
                if (!pinching) return;
                Vector3 local = Anchor.InverseTransformPoint(PrismHands.PointOf(h));
                if (!NearCurve(local, GrabRadius)) return;
                state.Active = true;
                state.DidEdit = false;
                Hands.Buzz(h, 0.2f, 0.04f);
            }

            if (!pinching) { EndCurveDrag(state); return; }

            Vector3 localHand = Anchor.InverseTransformPoint(PrismHands.PointOf(h));
            float xAbs = Mathf.Clamp(localHand.x / XScale, CurveField.DomainMin, CurveField.DomainMax);
            float desiredY = Mathf.Clamp((localHand.y - FnBaselineY) / FnYScale,
                                         -CurveField.DisplacementLimit, CurveField.DisplacementLimit);
            float delta = desiredY - _curve.ValueAt(xAbs);
            if (Mathf.Abs(delta) > 1e-5f)
            {
                _curve.ApplyGaussianBump(xAbs, delta, Sigma);
                state.DidEdit = true;
            }
        }

        void EndCurveDrag(DragState state)
        {
            if (!state.Active) return;
            state.Active = false;
            if (!state.DidEdit) return;

            // Fresh regardless of where in Tick() this fires - a release can happen on either
            // hand independently of the frame's main recompute.
            _curve.RecomputeDerivative();

            Loop.Evidence.Record(MathMotionEvidence.Deform);
            _everDeformed = true;

            if (_curve.BestMaximum(out _, 0.05f)) Loop.Evidence.Record(MathMotionEvidence.MadeMaximum);

            float steepest = 0f;
            for (int i = 0; i < CurveField.SampleCount; i++)
                steepest = Mathf.Max(steepest, Mathf.Abs(_curve.Derivative[i]));
            if (steepest > SteepThreshold) Loop.Evidence.Record(MathMotionEvidence.MadeSteep);
        }

        bool NearCurve(Vector3 local, float radius)
        {
            float xAbs = Mathf.Clamp(local.x / XScale, CurveField.DomainMin, CurveField.DomainMax);
            float y = FnBaselineY + _curve.ValueAt(xAbs) * FnYScale;
            Vector3 nearest = new Vector3(xAbs * XScale, y, CurveZ);
            return (nearest - local).sqrMagnitude <= radius * radius;
        }

        // -----------------------------------------------------------------
        // bead: free-rides until Discover, then becomes draggable
        // -----------------------------------------------------------------
        void UpdateBead(float dt)
        {
            if (!_everDeformed) _introT += dt;

            if (_beadHeld)
            {
                // position already set this frame by HandleBeadDrag
            }
            else if (!_everDeformed && _introT < IntroDuration)
            {
                // The one-time "drawn as it goes" reveal: the bead rides exactly at the frontier
                // the second curve is being traced out to, so the two are seen to be connected
                // before anything else happens.
                _beadX = Mathf.Lerp(CurveField.DomainMin, CurveField.DomainMax,
                                    Mathf.Clamp01(_introT / IntroDuration));
            }
            else
            {
                float span = CurveField.DomainMax - CurveField.DomainMin;
                float period = 2f * span / BeadRideSpeed;
                float t = Mathf.Repeat(Time.time, period) / period * 2f;
                _beadX = t <= 1f
                    ? Mathf.Lerp(CurveField.DomainMin, CurveField.DomainMax, t)
                    : Mathf.Lerp(CurveField.DomainMax, CurveField.DomainMin, t - 1f);
            }

            float beadY = FnBaselineY + _curve.ValueAt(_beadX) * FnYScale;
            _beadView.localPosition = new Vector3(_beadX * XScale, beadY, CurveZ);
            _beadMat.SetColor("_Tint", ColourForSlope(_curve.DerivativeAt(_beadX)));
        }

        void HandleBeadDrag()
        {
            if (Hands == null) return;

            if (!_beadHeld)
            {
                if (!TryBeadGrab(Hands.Left)) TryBeadGrab(Hands.Right);
                return;
            }

            if (_beadHand == null || !_beadHand.IsTracked || _beadHand.Pinch <= 0.55f)
            {
                _beadHeld = false;
                _beadHand = null;
                return;
            }

            Vector3 lp = Anchor.InverseTransformPoint(PrismHands.PointOf(_beadHand));
            _beadX = Mathf.Clamp(lp.x / XScale, CurveField.DomainMin, CurveField.DomainMax);
        }

        bool TryBeadGrab(PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked || h.Pinch <= 0.6f) return false;
            Vector3 local = Anchor.InverseTransformPoint(PrismHands.PointOf(h));
            Vector3 beadLocal = new Vector3(_beadX * XScale, FnBaselineY + _curve.ValueAt(_beadX) * FnYScale, CurveZ);
            if ((local - beadLocal).sqrMagnitude > BeadGrabRadius * BeadGrabRadius) return false;
            _beadHeld = true;
            _beadHand = h;
            Hands.Buzz(h, 0.2f, 0.03f);
            return true;
        }

        /// <summary>
        /// The tangent line: a segment through the bead whose slope is read directly off the
        /// derivative array (converted from abstract units into the anisotropic display scale, so
        /// it looks tangent to the CURVE AS DRAWN, not to the curve's abstract shape). Evidence
        /// only counts while the learner is actively dragging - an automatic ride past a rising
        /// section proves nothing was done.
        /// </summary>
        void UpdateTangentAndEvidence()
        {
            float slope = _curve.DerivativeAt(_beadX);
            float displaySlope = slope * (FnYScale / XScale);
            Vector3 dir = new Vector3(1f, displaySlope, 0f).normalized;
            Vector3 centre = new Vector3(_beadX * XScale, FnBaselineY + _curve.ValueAt(_beadX) * FnYScale, CurveZ);

            _tangentPts[0] = centre - dir * TangentHalfLength;
            _tangentPts[1] = centre + dir * TangentHalfLength;
            Color c = ColourForSlope(slope);
            _tangentColours[0] = c;
            _tangentColours[1] = c;
            _tangentMesh.BuildTube(_tangentPts, _tangentColours, 2, TangentRadius, 6);

            if (_beadHeld)
            {
                if (slope > TangentSlopeEpsilon && !Loop.Evidence.Has(MathMotionEvidence.TangentRising))
                    Loop.Evidence.Record(MathMotionEvidence.TangentRising);
                else if (slope < -TangentSlopeEpsilon && !Loop.Evidence.Has(MathMotionEvidence.TangentFalling))
                    Loop.Evidence.Record(MathMotionEvidence.TangentFalling);
            }
        }

        // -----------------------------------------------------------------
        // the two curves
        // -----------------------------------------------------------------
        void RebuildCurves()
        {
            for (int i = 0; i < CurveField.SampleCount; i++)
            {
                float x = _curve.X[i];
                float d = _curve.Derivative[i];
                _slopeColours[i] = ColourForSlope(d);
                _fnPts[i] = new Vector3(x * XScale, FnBaselineY + _curve.Y[i] * FnYScale, CurveZ);
                float dDisp = Mathf.Clamp(d, -DerivDisplayClamp, DerivDisplayClamp);
                _derivPts[i] = new Vector3(x * XScale, DerivBaselineY + dDisp * DerivYScale, CurveZ);
            }

            int derivCount = CurveField.SampleCount;
            if (!_everDeformed && _introT < IntroDuration)
            {
                float frac = Mathf.Clamp01(_introT / IntroDuration);
                derivCount = Mathf.Clamp(Mathf.RoundToInt(frac * (CurveField.SampleCount - 1)) + 1,
                                         1, CurveField.SampleCount);
            }

            _curveMesh.BuildTube(_fnPts, _slopeColours, CurveField.SampleCount, TubeRadius, 6);
            _derivMesh.BuildTube(_derivPts, _slopeColours, derivCount, TubeRadius, 6);
        }

        // -----------------------------------------------------------------
        // sweep + accumulate (Formalize only - the visuals stay put afterward as a settled record)
        // -----------------------------------------------------------------
        void HandleSweep()
        {
            if (Hands == null) return;

            if (!_sweepHeld)
            {
                if (!TrySweepGrab(Hands.Left)) TrySweepGrab(Hands.Right);
                return;
            }

            if (_sweepHand == null || !_sweepHand.IsTracked || _sweepHand.Pinch <= 0.55f)
            {
                _sweepHeld = false;
                _sweepHand = null;
                return;
            }

            Vector3 lp = Anchor.InverseTransformPoint(PrismHands.PointOf(_sweepHand));
            _sweepEnd = Mathf.Clamp(lp.x / XScale, CurveField.DomainMin, CurveField.DomainMax);

            if (!Loop.Evidence.Has(MathMotionEvidence.Swept) && _sweepEnd - CurveField.DomainMin > 0.3f)
                Loop.Evidence.Record(MathMotionEvidence.Swept);
            if (!Loop.Evidence.Has(MathMotionEvidence.SweptFull) && _sweepEnd > DomainEdgeReveal)
                Loop.Evidence.Record(MathMotionEvidence.SweptFull);
        }

        bool TrySweepGrab(PrismHands.Hand h)
        {
            if (h == null || !h.IsTracked || h.Pinch <= 0.6f) return false;
            Vector3 local = Anchor.InverseTransformPoint(PrismHands.PointOf(h));
            // A broad gesture, not a pinpoint grab - anywhere near the derivative's plane counts.
            if (local.z < CurveZ - 0.12f || local.z > CurveZ + 0.12f) return false;
            if (local.y < DerivBaselineY - 0.14f || local.y > DerivBaselineY + 0.14f) return false;
            if (local.x < -XScale - 0.06f || local.x > XScale + 0.06f) return false;
            _sweepHeld = true;
            _sweepHand = h;
            return true;
        }

        void UpdateSweepVisuals()
        {
            int uptoIndex = _curve.NearestIndex(_sweepEnd);
            int count = Mathf.Max(2, uptoIndex + 1);

            // Fill exactly as far as `count` will read (count-1), not just `uptoIndex` - near the
            // domain start those differ (count floors to 2 for a renderable sliver while
            // uptoIndex is 0), and AccumulateDerivative must not leave _traceY[1] stale.
            _curve.AccumulateDerivative(count - 1, _traceY);
            for (int i = 0; i < count; i++)
                _tracePts[i] = new Vector3(_curve.X[i] * XScale, FnBaselineY + _traceY[i] * FnYScale, CurveZ + 0.004f);

            _traceMesh.BuildTube(_tracePts, _slopeColours, count, TubeRadius * 0.85f, 5);
            _sweepFillMesh.BuildAreaStrip(_derivPts, _slopeColours, count, DerivBaselineY);
        }

        // -----------------------------------------------------------------
        // visibility + labels
        // -----------------------------------------------------------------
        /// <summary>
        /// Re-derived from the current stage every frame, rather than only on the transition
        /// event - a learner resuming a saved session can enter this world already past Wonder,
        /// with no StageEntered transition ever firing for the stages they are already through,
        /// so anything that matters must be recoverable from Loop.Stage alone.
        /// </summary>
        void UpdateVisibility()
        {
            bool discover = Loop.Stage >= LoopStage.Discover;
            if (_tangentView.gameObject.activeSelf != discover) _tangentView.gameObject.SetActive(discover);

            bool formalize = Loop.Stage >= LoopStage.Formalize;
            if (_sweepFillView.gameObject.activeSelf != formalize) _sweepFillView.gameObject.SetActive(formalize);
            if (_traceView.gameObject.activeSelf != formalize) _traceView.gameObject.SetActive(formalize);
        }

        void UpdateLabels(float dt)
        {
            if (Loop.Stage < LoopStage.Formalize)
            {
                _tangentLabel.Show(false);
                _traceLabel.Show(false);
                return;
            }

            _labelTimer += dt;
            bool refresh = _labelTimer >= LabelUpdateInterval;
            if (refresh) _labelTimer = 0f;

            float slope = _curve.DerivativeAt(_beadX);
            _tangentLabel.PlaceAbove(_tangentPts[1], 0.02f);
            if (refresh) _tangentLabel.SetText("derivative\nf'(x) = " + slope.ToString("0.00"), ColourForSlope(slope));
            _tangentLabel.Show(true);

            bool sweeping = _sweepEnd > CurveField.DomainMin + 0.05f;
            _traceLabel.Show(sweeping);
            if (sweeping)
            {
                int uptoIndex = _curve.NearestIndex(_sweepEnd);
                float area = _traceY[uptoIndex] - _curve.Y[0];
                _traceLabel.PlaceAbove(_tracePts[Mathf.Max(0, uptoIndex)], 0.02f);
                if (refresh)
                    _traceLabel.SetText("integral\naccumulated area = " + area.ToString("0.00"),
                                        ColourForSign(area, AreaSaturationScale));
            }
        }

        // -----------------------------------------------------------------
        // colour law
        // -----------------------------------------------------------------
        static Color ColourForSign(float value, float saturationScale)
        {
            float s = Mathf.Clamp(value / Mathf.Max(saturationScale, 1e-4f), -1f, 1f);
            return s >= 0f ? Color.Lerp(PrismPalette.Warm, PrismPalette.Mint, s)
                           : Color.Lerp(PrismPalette.Warm, PrismPalette.Coral, -s);
        }

        static Color ColourForSlope(float slope) => ColourForSign(slope, ColourSaturationScale);

        // -----------------------------------------------------------------
        // concepts
        // -----------------------------------------------------------------
        static Vector3 WedgeDirection(float azimuthDeg, float elevation)
        {
            // Matches PrismEnvironment's sun az/el convention (x = cos(el)*sin(az), y = sin(el),
            // z = cos(el)*cos(az)), except elevation is taken directly as the y-component rather
            // than as an angle in degrees, since the placement brief specifies it that way.
            float az = azimuthDeg * Mathf.Deg2Rad;
            float y = Mathf.Clamp(elevation, -0.999f, 0.999f);
            float horiz = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            return new Vector3(horiz * Mathf.Sin(az), y, horiz * Mathf.Cos(az));
        }

        public override IEnumerable<ConceptSpec> Concepts
        {
            get
            {
                // The door: kind System, domain Mathematics, as specified. Built out of the four
                // supporting concepts below, and a prerequisite for orbital mechanics broadly -
                // the specific, honest links (a velocity IS a derivative; sweeping area IS how
                // Kepler's second law is stated) live on the concepts that actually carry them.
                yield return new ConceptSpec
                {
                    Id = "math-motion",
                    Title = "Mathematics of Motion",
                    Domain = ConceptDomain.Mathematics,
                    Kind = KnowledgeKind.System,
                    Direction = WedgeDirection(196f, 0.05f),
                    Distance = 3.6f,
                    WorldId = "math-motion",
                    Capability = "Feel the slope of a curve as a number, and watch accumulation build a curve back up.",
                    Formalisation = "Calculus in one picture: a function, the slope that rides along it, " +
                                     "and the area that adds back up to where you started.",
                    Links = new (string, Relation, float)[]
                    {
                        ("derivative",        Relation.Composes,     0.9f),
                        ("integral",          Relation.Composes,     0.9f),
                        ("rate-of-change",    Relation.Composes,     0.8f),
                        ("critical-point",    Relation.Composes,     0.7f),
                        ("orbital-mechanics", Relation.Prerequisite, 0.55f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "derivative",
                    Title = "Derivative",
                    Domain = ConceptDomain.Mathematics,
                    Kind = KnowledgeKind.Equation,
                    Direction = WedgeDirection(184f, 0.30f),
                    Distance = 4.1f,
                    Capability = "Read the steepness of a curve at a point as a signed number, and grab it directly.",
                    Formalisation = "f'(x) = lim(h -> 0) [f(x+h) - f(x)] / h. The slope of the line that " +
                                     "only just touches the curve.",
                    Links = new (string, Relation, float)[]
                    {
                        ("rate-of-change",     Relation.Instantiates, 0.85f),
                        ("integral",           Relation.Prerequisite, 0.7f),
                        // The flagship cross-domain link: a velocity arrow in the orbital world is
                        // literally a derivative, drawn as an arrow instead of a slope.
                        ("vectors",            Relation.Analogy,      0.8f),
                        ("periodic-motion",    Relation.Analogy,      0.65f),
                        ("orbital-mechanics",  Relation.Prerequisite, 0.6f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "integral",
                    Title = "Integral",
                    Domain = ConceptDomain.Mathematics,
                    Kind = KnowledgeKind.Equation,
                    Direction = WedgeDirection(210f, -0.28f),
                    Distance = 4.4f,
                    Capability = "Sweep a hand under a curve and watch the running total of its area accumulate.",
                    Formalisation = "The area between a curve and its axis, built by adding infinitely many " +
                                     "infinitesimally thin slices. Run backwards from a rate, it gives back " +
                                     "the original quantity.",
                    Links = new (string, Relation, float)[]
                    {
                        // Kepler's second law IS an integral: equal areas swept in equal times.
                        ("angular-momentum", Relation.Analogy, 0.7f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "rate-of-change",
                    Title = "Rate of Change",
                    Domain = ConceptDomain.Mathematics,
                    Kind = KnowledgeKind.Process,
                    Direction = WedgeDirection(188f, -0.42f),
                    Distance = 4.9f,
                    Capability = "Feel whether something is growing or shrinking, and how fast, before naming it.",
                    Formalisation = "How much one quantity changes for a small change in another. Every " +
                                     "derivative is a rate of change; not every rate of change needs a " +
                                     "formula to be felt first.",
                    Links = new (string, Relation, float)[]
                    {
                        ("periodic-motion", Relation.Prerequisite, 0.55f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "critical-point",
                    Title = "Critical Point",
                    Domain = ConceptDomain.Mathematics,
                    Kind = KnowledgeKind.Fact,
                    Direction = WedgeDirection(214f, 0.18f),
                    Distance = 5.2f,
                    Capability = "Spot where a curve stops rising or falling, without measuring anything.",
                    Formalisation = "Where f'(x) = 0. The curve pauses turning: a peak, a valley, or a flat shoulder.",
                    Links = new (string, Relation, float)[]
                    {
                        ("derivative",          Relation.Measures, 0.8f),
                        // A critical point of a potential is where the force vanishes: equilibrium
                        // IS a zero derivative, felt as a place the curve pauses turning.
                        ("energy-conservation", Relation.Analogy,  0.6f),
                    }
                };
            }
        }
    }
}
