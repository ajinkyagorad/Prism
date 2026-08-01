using UnityEngine;

namespace Prism.Interaction
{
    /// <summary>
    /// Both hands, straight off OVRInput, with no input-action asset in between.
    ///
    /// The indirection an action asset would add has to be wired correctly before anything works
    /// at all, and every control here is a physical button on a named hand. Reading the device
    /// directly means a scene generated from code is complete the moment it is generated.
    ///
    /// Controllers and hand tracking are read together and merged, because the learner may put a
    /// controller down mid-lesson and the world should not notice. This requires
    /// <c>launchSimultaneousHandsControllersOnStartup</c> on OVRManager — without it the runtime
    /// picks one modality at startup and the other stays frozen forever.
    /// </summary>
    public class PrismHands : MonoBehaviour
    {
        /// <summary>
        /// Assigned by the scene builder to OVRCameraRig's leftHandAnchor / rightHandAnchor.
        /// These are the transforms the rig actually drives at runtime; LeftControllerAnchor and
        /// RightControllerAnchor look equivalent and are never written to.
        /// </summary>
        public Transform LeftAnchor;
        public Transform RightAnchor;
        public Transform Head;

        /// <summary>
        /// Hand-tracking sources, assigned by PrismSceneBuilder.
        ///
        /// These are NOT optional extras. Hand pinch is not exposed through
        /// OVRInput.Axis1D — reading PrimaryIndexTrigger on Controller.LHand returns 0 forever,
        /// which is why an earlier build could not be interacted with at all when no controllers
        /// were held. GetFingerPinchStrength on OVRHand is the only real source.
        /// </summary>
        public OVRHand LeftHandTracking;
        public OVRHand RightHandTracking;
        public OVRSkeleton LeftSkeleton;
        public OVRSkeleton RightSkeleton;

        /// <summary>
        /// OVRCameraRig's trackingSpace. OVRInput.GetLocalControllerVelocity reports in tracking
        /// space, so without this the throw velocity is expressed in the wrong frame the moment
        /// the rig is moved or rotated.
        /// </summary>
        public Transform TrackingSpace;

        [Header("Shader coupling")]
        [Tooltip("Metres over which materials feel a hand. Published to every shader as _PrismHandRange.")]
        public float HandInfluenceRange = 0.35f;

        public Hand Left  { get; private set; }
        public Hand Right { get; private set; }

        public class Hand
        {
            public Transform Anchor;
            public bool IsTracked;
            public Vector3 Position;
            public Quaternion Rotation;

            /// <summary>0..1. Whole-hand grasp: controller grip, or a hand-tracked fist.</summary>
            public float Grip;
            /// <summary>0..1. Index trigger, or an index-thumb pinch.</summary>
            public float Pinch;

            public bool GripDown { get; internal set; }
            public bool GripUp   { get; internal set; }
            public bool PinchDown { get; internal set; }
            public bool PinchUp   { get; internal set; }

            /// <summary>Smoothed world velocity, m/s. This is what a throw is made of.</summary>
            public Vector3 Velocity;

            /// <summary>Index fingertip in world space, when a hand skeleton is available.</summary>
            public Vector3 IndexTip;
            /// <summary>Thumb tip in world space, when a hand skeleton is available.</summary>
            public Vector3 ThumbTip;
            public bool TipsValid;

            /// <summary>
            /// Where the hand is POINTING. For a tracked hand this is the runtime's own pointer
            /// pose (which is wrist/shoulder derived and far steadier than a finger direction);
            /// for a controller it is the controller's forward axis.
            /// </summary>
            public Ray Reach;
            public bool ReachValid;

            /// <summary>True when hand tracking rather than a controller is driving this hand.</summary>
            public bool UsingHandTracking;

            internal OVRHand _ovrHand;
            internal OVRSkeleton _skeleton;
            internal int _indexTipBone = -1, _thumbTipBone = -1;

            internal bool  _prevGrip, _prevPinch;
            internal readonly VelocityEstimator _estimator = new VelocityEstimator();
            internal OVRInput.Controller _touch, _hand;
            internal float _hapticUntil;
            internal float _hapticAmp;
            internal int   _pulsesLeft;
            internal float _nextPulseAt;

            public Vector3 Forward => Rotation * Vector3.forward;
            public bool IsGrasping => Grip > 0.6f || Pinch > 0.7f;
        }

        /// <summary>
        /// Least-squares velocity over a short window.
        ///
        /// A single-frame difference is far too noisy at the speeds that matter here — a moon
        /// enters orbit at about 21 cm/s, so tracking jitter is a large fraction of the signal and
        /// an unfiltered release would scatter identical throws across every conic section. Six
        /// samples is roughly 80 ms at 72 Hz: long enough to reject jitter, short enough that the
        /// release still belongs to the gesture the learner just made.
        /// </summary>
        internal class VelocityEstimator
        {
            const int N = 6;
            readonly Vector3[] _p = new Vector3[N];
            readonly float[] _t = new float[N];
            int _count;

            public void Push(Vector3 p, float t)
            {
                for (int i = N - 1; i > 0; i--) { _p[i] = _p[i - 1]; _t[i] = _t[i - 1]; }
                _p[0] = p; _t[0] = t;
                if (_count < N) _count++;
            }

            public void Reset() => _count = 0;

            public Vector3 Estimate()
            {
                if (_count < 2) return Vector3.zero;

                // Fit p(t) = a + b*t and return b. Time is re-centred on the window mean so the
                // normal equations stay well conditioned at large Time.time values.
                float tMean = 0f;
                for (int i = 0; i < _count; i++) tMean += _t[i];
                tMean /= _count;

                Vector3 num = Vector3.zero;
                float den = 0f;
                for (int i = 0; i < _count; i++)
                {
                    float dt = _t[i] - tMean;
                    num += _p[i] * dt;
                    den += dt * dt;
                }
                return den < 1e-9f ? Vector3.zero : num / den;
            }
        }

        void Awake()
        {
            Left  = new Hand { Anchor = LeftAnchor,  _touch = OVRInput.Controller.LTouch, _hand = OVRInput.Controller.LHand,
                               _ovrHand = LeftHandTracking,  _skeleton = LeftSkeleton };
            Right = new Hand { Anchor = RightAnchor, _touch = OVRInput.Controller.RTouch, _hand = OVRInput.Controller.RHand,
                               _ovrHand = RightHandTracking, _skeleton = RightSkeleton };
        }

        void Update()
        {
            if (Left.Anchor == null)  Left.Anchor  = LeftAnchor;
            if (Right.Anchor == null) Right.Anchor = RightAnchor;

            UpdateHand(Left);
            UpdateHand(Right);
            PublishToShaders();
        }

        void UpdateHand(Hand h)
        {
            bool touchActive = OVRInput.IsControllerConnected(h._touch);
            bool handTracked = h._ovrHand != null && h._ovrHand.IsTracked;

            h.UsingHandTracking = handTracked && !touchActive;
            h.IsTracked = (touchActive || handTracked) && h.Anchor != null;

            if (!h.IsTracked)
            {
                // An untracked hand must not report a stale pose. Left at its last position it
                // would sit inside whatever the learner is looking at.
                h.Grip = h.Pinch = 0f;
                h.Velocity = Vector3.zero;
                h.TipsValid = false;
                h.ReachValid = false;
                h._estimator.Reset();
                h.GripDown = h.GripUp = h.PinchDown = h.PinchUp = false;
                h._prevGrip = h._prevPinch = false;
                return;
            }

            h.Position = h.Anchor.position;
            h.Rotation = h.Anchor.rotation;

            // --- grasp ---------------------------------------------------------------
            // Controllers report through OVRInput. Hand tracking does NOT: Axis1D on
            // Controller.LHand/RHand returns 0, which is the bug that made an earlier build
            // completely uninteractable for anyone not holding controllers. Pinch strength has
            // to come from OVRHand, and a hand-tracked "grip" is the middle-finger pinch.
            float ctrlGrip  = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger,  h._touch);
            float ctrlPinch = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, h._touch);

            float handPinch = 0f, handGrip = 0f;
            if (handTracked && !h._ovrHand.IsSystemGestureInProgress)
            {
                handPinch = h._ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                handGrip  = Mathf.Max(h._ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle),
                                      h._ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Ring));
            }

            // Merge modalities rather than choosing one, so putting a controller down mid-lesson
            // is invisible to everything downstream.
            h.Grip  = Mathf.Max(ctrlGrip,  handGrip);
            h.Pinch = Mathf.Max(ctrlPinch, handPinch);

            bool grip = h.Grip > 0.55f;
            bool pinch = h.Pinch > 0.6f;
            h.GripDown  = grip && !h._prevGrip;
            h.GripUp    = !grip && h._prevGrip;
            h.PinchDown = pinch && !h._prevPinch;
            h.PinchUp   = !pinch && h._prevPinch;
            h._prevGrip = grip;
            h._prevPinch = pinch;

            // --- fingertips ----------------------------------------------------------
            UpdateFingertips(h);

            // --- where the hand points -----------------------------------------------
            // A tracked hand's pointer pose is wrist and shoulder derived, and is far steadier
            // than any direction computed from finger joints. Fall back to the anchor forward.
            if (handTracked && h._ovrHand.IsPointerPoseValid && h._ovrHand.PointerPose != null)
            {
                h.Reach = new Ray(h._ovrHand.PointerPose.position, h._ovrHand.PointerPose.forward);
                h.ReachValid = true;
            }
            else if (touchActive)
            {
                h.Reach = new Ray(h.Position, h.Rotation * Vector3.forward);
                h.ReachValid = true;
            }
            else if (h.TipsValid)
            {
                // Last resort: along the index finger.
                var dir = (h.IndexTip - h.Position);
                h.Reach = new Ray(h.IndexTip, dir.sqrMagnitude > 1e-6f ? dir.normalized : h.Forward);
                h.ReachValid = true;
            }
            else h.ReachValid = false;

            // --- velocity ------------------------------------------------------------
            h._estimator.Push(h.Position, Time.time);
            Vector3 reported = touchActive ? OVRInput.GetLocalControllerVelocity(h._touch) : Vector3.zero;
            if (reported.sqrMagnitude > 1e-8f && TrackingSpace != null)
                reported = TrackingSpace.TransformVector(reported);

            h.Velocity = reported.sqrMagnitude > 1e-8f ? reported : h._estimator.Estimate();

            ServiceHaptics(h);
        }

        /// <summary>
        /// Index and thumb tips from the hand skeleton.
        ///
        /// Bone ids are matched by NAME suffix rather than by enum value, because the OpenXR
        /// skeleton reports XRHand_IndexTip while the native one reports Hand_IndexTip. Matching
        /// on the value of one of them silently finds nothing under the other.
        /// </summary>
        void UpdateFingertips(Hand h)
        {
            h.TipsValid = false;
            var sk = h._skeleton;
            if (sk == null || !sk.IsInitialized || !sk.IsDataValid || sk.Bones == null) return;

            if (h._indexTipBone < 0 || h._thumbTipBone < 0)
            {
                for (int i = 0; i < sk.Bones.Count; i++)
                {
                    var id = sk.Bones[i].Id.ToString();
                    if (id.EndsWith("IndexTip")) h._indexTipBone = i;
                    else if (id.EndsWith("ThumbTip")) h._thumbTipBone = i;
                }
                if (h._indexTipBone < 0 || h._thumbTipBone < 0) return;
            }

            if (h._indexTipBone >= sk.Bones.Count || h._thumbTipBone >= sk.Bones.Count) return;
            var it = sk.Bones[h._indexTipBone].Transform;
            var tt = sk.Bones[h._thumbTipBone].Transform;
            if (it == null || tt == null) return;

            h.IndexTip = it.position;
            h.ThumbTip = tt.position;
            h.TipsValid = true;
        }

        /// <summary>
        /// Publish hand state to every shader.
        ///
        /// One SetGlobalVector per hand replaces any need for materials to hold references to the
        /// interaction layer, which is what lets a concept's own matter respond to being reached
        /// toward. w = 0 for an untracked hand so it contributes nothing rather than making the
        /// world origin glow.
        /// </summary>
        void PublishToShaders()
        {
            // For hand tracking the fingertip is what the learner aims with; for a controller the
            // grip position is. Using the tip when available makes proximity feel accurate.
            Vector3 l = Left.TipsValid ? Left.IndexTip : Left.Position;
            Vector3 r = Right.TipsValid ? Right.IndexTip : Right.Position;

            Shader.SetGlobalVector("_PrismHandL", new Vector4(l.x, l.y, l.z, Left.IsTracked ? 1f : 0f));
            Shader.SetGlobalVector("_PrismHandR", new Vector4(r.x, r.y, r.z, Right.IsTracked ? 1f : 0f));
            Shader.SetGlobalFloat("_PrismHandRange", Mathf.Max(0.02f, HandInfluenceRange));
        }

        /// <summary>The point this hand interacts with: the fingertip if we have it, else the grip.</summary>
        public static Vector3 PointOf(Hand h) => h.TipsValid ? h.IndexTip : h.Position;

        // ---- haptics ---------------------------------------------------------

        /// <summary>
        /// A single buzz reads as a notification. Two short pulses separated by a gap read as a
        /// physical seat — the double pulse is what makes a thing feel like it clunked into place
        /// rather than beeped at you.
        /// </summary>
        public void Clunk(Hand h, float amplitude = 0.6f)
        {
            h._pulsesLeft = 2;
            h._hapticAmp = amplitude;
            h._nextPulseAt = Time.time;
            h._hapticUntil = 0f;
        }

        public void Buzz(Hand h, float amplitude, float seconds)
        {
            h._pulsesLeft = 0;
            h._hapticAmp = amplitude;
            h._hapticUntil = Time.time + seconds;
            OVRInput.SetControllerVibration(0.5f, amplitude, h._touch);
        }

        void ServiceHaptics(Hand h)
        {
            const float pulse = 0.035f;
            const float gap = 0.055f;

            if (h._pulsesLeft > 0 && Time.time >= h._nextPulseAt)
            {
                OVRInput.SetControllerVibration(0.6f, h._hapticAmp, h._touch);
                h._hapticUntil = Time.time + pulse;
                h._nextPulseAt = Time.time + pulse + gap;
                h._pulsesLeft--;
                return;
            }

            if (h._hapticUntil > 0f && Time.time >= h._hapticUntil)
            {
                OVRInput.SetControllerVibration(0f, 0f, h._touch);
                h._hapticUntil = 0f;
            }
        }

        void OnDisable()
        {
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }

        /// <summary>Whichever hand is nearer to a world point, preferring a tracked one.</summary>
        public Hand Nearest(Vector3 worldPoint)
        {
            if (!Left.IsTracked)  return Right;
            if (!Right.IsTracked) return Left;
            return (Left.Position - worldPoint).sqrMagnitude <= (Right.Position - worldPoint).sqrMagnitude
                 ? Left : Right;
        }
    }
}
