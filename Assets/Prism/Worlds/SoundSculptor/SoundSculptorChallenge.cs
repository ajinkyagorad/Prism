using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.SoundSculptor
{
    /// <summary>
    /// The Apply, Explain and Create stages, all checked against the real simulation rather than
    /// an answer the learner types — the same discipline OrbitalChallenge uses. A wrong prediction
    /// in Explain is recorded as a misconception, not a failure, and the learner simply tries again.
    /// </summary>
    public class SoundSculptorChallenge : MonoBehaviour
    {
        public SoundSculptorWorld World;

        const float GrabRadius = 0.05f;

        static readonly SoundScale.Ratio[] NiceRatios =
        {
            new SoundScale.Ratio(4, 3, "fourth"),
            new SoundScale.Ratio(3, 2, "fifth"),
            new SoundScale.Ratio(2, 1, "octave"),
        };

        enum ChallengeMode { Idle, Tune, Predict }
        ChallengeMode _mode = ChallengeMode.Idle;

        // ---- Apply: tune a string onto a ghost target interval ----
        static readonly Vector3 GhostCentreLocal = new Vector3(-0.06f, 0.10f, -0.10f);
        const float TuneHoldSeconds = 0.6f;

        Transform _ghostA, _ghostB;
        Material _ghostMat;
        SoundScale.Ratio _tuneRatio;
        float _tuneHeld;

        // ---- Explain: predict lock or beat before it sounds ----
        static readonly Vector3 CalmZoneLocal    = new Vector3(-0.20f, -0.05f, 0.08f);
        static readonly Vector3 BeatZoneLocal    = new Vector3(0.20f, -0.05f, 0.08f);
        static readonly Vector3 TokenCradleLocal = new Vector3(0.00f, -0.05f, 0.20f);
        const float ZoneRadius = 0.05f;

        Transform _calmZone, _beatZone;
        Material _calmMat, _beatMat;
        DraggableToken _predictToken;

        enum ExplainPhase { Waiting, Animating, Revealed }
        ExplainPhase _explainPhase;
        float _explainTimer;
        bool _guessedLock;

        enum TrialKind { Lock, Beat }
        TrialKind _trialKind;
        float _trialTargetLength;

        // ---- Create: capture tones into a chord, play it back ----
        const int ChordSlotCount = 5;
        const float RackSpacing = 0.075f;
        const float ChordNoteSeconds = 1.8f;
        // Up to five of these can ring together (see UpdatePlayback), so each is quieter still
        // than a single sustained string — see the gain comment on SoundSculptorWorld.VoiceGain.
        const float ChordNoteGain = 0.08f;
        static readonly Vector3 RackCentreLocal = new Vector3(0.00f, -0.14f, 0.12f);
        static readonly Vector3 CrystalCradleLocal = new Vector3(0.00f, -0.14f, 0.24f);
        static readonly Vector3 PlayMoteLocal = new Vector3(0.00f, -0.21f, 0.12f);

        struct ChordSlot
        {
            public bool Filled;
            public float Frequency;
            public Transform View;
            public Material Mat;
            public SoundSynth.ToneVoice Voice;
            public float PlayDelay;
        }
        ChordSlot[] _slots;
        int _filledCount;
        float _pendingCapture;
        float _playElapsed = -1f;

        DraggableToken _captureCrystal;
        Material _crystalMat;
        Transform _playMote;

        // -----------------------------------------------------------------
        // construction — no reference to World here; it is not assigned until after AddComponent
        // returns, exactly the same discipline OrbitalChallenge.Awake follows.
        // -----------------------------------------------------------------

        void Awake()
        {
            BuildTuneGhost();
            BuildPredictZones();
            BuildChordRig();
        }

        void BuildTuneGhost()
        {
            _ghostMat = PrismMaterials.New(PrismMaterials.Volumetric);
            _ghostMat.SetColor("_Tint", PrismPalette.Lavender);
            _ghostMat.SetColor("_EdgeTint", PrismPalette.Gold);
            _ghostMat.SetFloat("_Density", 0.5f);

            _ghostA = MakeBall("GhostA", _ghostMat, 0.012f);
            _ghostB = MakeBall("GhostB", _ghostMat, 0.012f);
        }

        void BuildPredictZones()
        {
            _calmMat = PrismMaterials.CeramicBody(PrismPalette.Cyan, 0.35f);
            _beatMat = PrismMaterials.CeramicBody(PrismPalette.Coral, 0.35f);

            _calmZone = MakeDisc("CalmZone", _calmMat);
            _beatZone = MakeDisc("BeatZone", _beatMat);

            var tokenMat = PrismMaterials.New(PrismMaterials.Seed);
            tokenMat.SetColor("_Tint", PrismPalette.Warm);
            tokenMat.SetFloat("_Growth", 1f);
            var tokenView = MakeBall("PredictToken", tokenMat, 0.012f);
            _predictToken = new DraggableToken { View = tokenView, HomeLocal = TokenCradleLocal };
        }

        void BuildChordRig()
        {
            _slots = new ChordSlot[ChordSlotCount];
            for (int i = 0; i < ChordSlotCount; i++)
            {
                var mat = PrismMaterials.New(PrismMaterials.Seed);
                mat.SetColor("_Tint", PrismPalette.Warm);
                mat.SetFloat("_Growth", 0.75f);
                var view = MakeBall("ChordSlot" + i, mat, 0.016f);
                _slots[i] = new ChordSlot { View = view, Mat = mat };
            }

            _crystalMat = PrismMaterials.New(PrismMaterials.Seed);
            _crystalMat.SetColor("_Tint", PrismPalette.Warm);
            _crystalMat.SetFloat("_Growth", 0.4f);
            var crystalView = MakeBall("CaptureCrystal", _crystalMat, 0.013f);
            _captureCrystal = new DraggableToken { View = crystalView, HomeLocal = CrystalCradleLocal };

            var playMat = PrismMaterials.CeramicBody(PrismPalette.Mint, 0.4f);
            _playMote = MakeBall("PlayMote", playMat, 0.015f);
        }

        Transform MakeBall(string name, Material mat, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(1);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * radius;
            go.SetActive(false);
            return go.transform;
        }

        Transform MakeDisc(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Disc(24, 4);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * 0.05f;
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>A small object the learner picks up and drops somewhere meaningful — the same
        /// interaction shape used to summon the second string, reused here for the prediction
        /// token and the capture crystal so the learner only has to learn it once.</summary>
        class DraggableToken
        {
            public Transform View;
            public Vector3 HomeLocal;
            public bool Held;
            public PrismHands.Hand HoldingHand;

            public void TryGrab(PrismHands.Hand hand, float grabRadius)
            {
                if (Held || hand == null || !hand.IsTracked || !hand.IsGrasping) return;
                if ((PrismHands.PointOf(hand) - View.position).sqrMagnitude > grabRadius * grabRadius) return;
                Held = true;
                HoldingHand = hand;
            }

            /// <returns>The world point it was just released at, or null while still held or resting.</returns>
            public Vector3? Drag(Transform anchor, float dt)
            {
                if (!Held)
                {
                    Vector3 home = anchor.TransformPoint(HomeLocal);
                    View.position = Vector3.Lerp(View.position, home, 1f - Mathf.Exp(-dt * 8f));
                    return null;
                }
                if (HoldingHand == null || !HoldingHand.IsTracked || !HoldingHand.IsGrasping)
                {
                    Held = false;
                    Vector3 at = View.position;
                    HoldingHand = null;
                    return at;
                }
                View.position = PrismHands.PointOf(HoldingHand);
                return null;
            }
        }

        // -----------------------------------------------------------------
        // entry points, called from SoundSculptorWorld
        // -----------------------------------------------------------------

        public void BeginTune()
        {
            _mode = ChallengeMode.Tune;
            _tuneHeld = 0f;
            _tuneRatio = NiceRatios[Random.Range(0, NiceRatios.Length)];
            _ghostA.gameObject.SetActive(true);
            _ghostB.gameObject.SetActive(true);
        }

        public void BeginPredict()
        {
            _mode = ChallengeMode.Predict;
            _explainPhase = ExplainPhase.Waiting;
            _predictToken.Held = false;
            _predictToken.View.position = World.WorldAnchor.TransformPoint(TokenCradleLocal);
            _calmZone.gameObject.SetActive(true);
            _beatZone.gameObject.SetActive(true);
            _predictToken.View.gameObject.SetActive(true);
            PickTrial();
        }

        public void BeginCreate()
        {
            _mode = ChallengeMode.Idle;
            HideAllChallengeVisuals();
            _captureCrystal.View.position = World.WorldAnchor.TransformPoint(CrystalCradleLocal);
            _captureCrystal.View.gameObject.SetActive(true);
            _playMote.gameObject.SetActive(true);
        }

        void HideAllChallengeVisuals()
        {
            _ghostA.gameObject.SetActive(false);
            _ghostB.gameObject.SetActive(false);
            _calmZone.gameObject.SetActive(false);
            _beatZone.gameObject.SetActive(false);
            _predictToken.View.gameObject.SetActive(false);
        }

        /// <summary>
        /// Called once per entry (first time or a return visit) after the world's own spatial
        /// layout has been reset. Re-derives which challenge should be active from the CURRENT
        /// loop stage rather than assuming — a learner who left mid-Apply and comes back gets a
        /// fresh target rather than a world stuck showing nothing.
        /// </summary>
        public void ResetChallenges()
        {
            _explainPhase = ExplainPhase.Waiting;
            _predictToken.Held = false;
            _pendingCapture = 0f;
            _captureCrystal.Held = false;
            _playElapsed = -1f;

            HideAllChallengeVisuals();

            var stage = World.Loop.Stage;
            if (stage == LoopStage.Apply) BeginTune();
            else if (stage == LoopStage.Explain) BeginPredict();
            else _mode = ChallengeMode.Idle;

            bool createReached = stage >= LoopStage.Create;
            _captureCrystal.View.gameObject.SetActive(createReached);
            _playMote.gameObject.SetActive(createReached);
            if (createReached) _captureCrystal.View.position = World.WorldAnchor.TransformPoint(CrystalCradleLocal);

            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].Filled) continue;
                _slots[i].View.position = World.WorldAnchor.TransformPoint(SlotLocal(i));
            }
        }

        static Vector3 SlotLocal(int index) =>
            RackCentreLocal + new Vector3((index - (ChordSlotCount - 1) * 0.5f) * RackSpacing, 0f, 0f);

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        public void Tick(float dt)
        {
            switch (_mode)
            {
                case ChallengeMode.Tune:    EvaluateTune(dt);    break;
                case ChallengeMode.Predict: EvaluatePredict(dt); break;
            }
            EvaluateCreate(dt);
        }

        /// <summary>
        /// Apply. A ghost pair of markers shows exactly where String1 would need to reach for a
        /// randomly chosen simple ratio against String0's CURRENT length — recomputed every frame,
        /// so moving String0 moves the target honestly rather than leaving a stale one behind.
        /// </summary>
        void EvaluateTune(float dt)
        {
            float targetLen = Mathf.Clamp(World.String0.Length / _tuneRatio.Value,
                                          SoundScale.MinLength, SoundScale.MaxLength);
            Vector3 centre = World.WorldAnchor.TransformPoint(GhostCentreLocal);
            Vector3 axis = World.WorldAnchor.right;
            _ghostA.position = centre - axis * (targetLen * 0.5f);
            _ghostB.position = centre + axis * (targetLen * 0.5f);

            bool close = World.String1.Summoned
                       && Mathf.Abs(World.String1.Length - targetLen) < Mathf.Max(0.01f, targetLen * 0.03f);
            _tuneHeld = close ? _tuneHeld + dt : 0f;

            float glow = Mathf.Clamp01(_tuneHeld / TuneHoldSeconds);
            _ghostMat.SetFloat("_Density", Mathf.Lerp(0.5f, 1.15f, glow));

            if (_tuneHeld >= TuneHoldSeconds)
            {
                World.Loop.Evidence.Record(SoundEvidence.TuneDone);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.3f);
                _ghostA.gameObject.SetActive(false);
                _ghostB.gameObject.SetActive(false);
                _mode = ChallengeMode.Idle;
            }
        }

        /// <summary>
        /// Explain. A token carried into the "calm" or "beating" zone commits a prediction; only
        /// then does the world actually move String1 to the trial length and let it sound, so the
        /// prediction is genuinely made before the evidence exists, not after.
        /// </summary>
        void EvaluatePredict(float dt)
        {
            _calmZone.position = World.WorldAnchor.TransformPoint(CalmZoneLocal);
            _beatZone.position = World.WorldAnchor.TransformPoint(BeatZoneLocal);

            if (World.Hands != null)
            {
                _predictToken.TryGrab(World.Hands.Left, GrabRadius);
                _predictToken.TryGrab(World.Hands.Right, GrabRadius);
            }
            var released = _predictToken.Drag(World.WorldAnchor, dt);

            switch (_explainPhase)
            {
                case ExplainPhase.Waiting:
                    if (released.HasValue)
                    {
                        float dCalm = Vector3.Distance(released.Value, _calmZone.position);
                        float dBeat = Vector3.Distance(released.Value, _beatZone.position);
                        if (dCalm < ZoneRadius || dBeat < ZoneRadius)
                        {
                            _guessedLock = dCalm < dBeat;
                            _explainPhase = ExplainPhase.Animating;
                        }
                    }
                    break;

                case ExplainPhase.Animating:
                    AnimateString1Toward(_trialTargetLength, dt);
                    if (Mathf.Abs(World.String1.Length - _trialTargetLength) < 0.004f)
                    {
                        ResolveTrial();
                        _explainPhase = ExplainPhase.Revealed;
                        _explainTimer = 0f;
                    }
                    break;

                case ExplainPhase.Revealed:
                    _explainTimer += dt;
                    if (_explainTimer > 1.8f)
                    {
                        PickTrial();
                        _explainPhase = ExplainPhase.Waiting;
                    }
                    break;
            }
        }

        void AnimateString1Toward(float targetLength, float dt)
        {
            var s = World.String1;
            Vector3 mid = s.Midpoint;
            Vector3 axis = (s.EndB - s.EndA);
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : World.WorldAnchor.right;

            float rate = 1f - Mathf.Exp(-dt * 6f);
            float newLen = Mathf.Lerp(s.Length, targetLength, rate);
            s.EndA = mid - axis * (newLen * 0.5f);
            s.EndB = mid + axis * (newLen * 0.5f);
        }

        /// <summary>
        /// Rejection-samples a trial length until it genuinely produces the intended outcome,
        /// verified against the SAME coincidence function that gates the rest of the world — never
        /// hand-derived, always checked, so a trial cannot silently be wrong about its own answer.
        /// </summary>
        void PickTrial()
        {
            _trialKind = Random.value < 0.5f ? TrialKind.Lock : TrialKind.Beat;
            float baseLen = World.String0.Length;
            float f0 = SoundScale.FrequencyOf(baseLen);

            for (int tries = 0; tries < 12; tries++)
            {
                var r = NiceRatios[Random.Range(0, NiceRatios.Length)];
                float candidate = baseLen / r.Value;
                if (_trialKind == TrialKind.Beat)
                {
                    float sign = Random.value < 0.5f ? 1f : -1f;
                    candidate *= 1f + sign * Random.Range(0.05f, 0.09f);
                }
                candidate = Mathf.Clamp(candidate, SoundScale.MinLength, SoundScale.MaxLength);

                float f1 = SoundScale.FrequencyOf(candidate);
                var c = SoundScale.NearestCoincidence(f0, f1);
                bool ok = _trialKind == TrialKind.Lock ? SoundScale.IsLocked(c.BeatHz) : SoundScale.IsBeating(c.BeatHz);
                if (ok) { _trialTargetLength = candidate; return; }
            }

            _trialTargetLength = Mathf.Clamp(_trialKind == TrialKind.Lock ? baseLen * 0.5f : baseLen * 1.31f,
                                             SoundScale.MinLength, SoundScale.MaxLength);
        }

        void ResolveTrial()
        {
            var c = SoundScale.NearestCoincidence(World.String0.Frequency, World.String1.Frequency);
            bool actuallyLocked = SoundScale.IsLocked(c.BeatHz);
            bool correct = _guessedLock == actuallyLocked;

            World.Loop.Evidence.Record(SoundEvidence.PredictionAttempt);
            if (correct)
            {
                World.Loop.Evidence.Record(SoundEvidence.PredictionGood);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.3f);
            }
            else
            {
                // Being wrong here is informative, not a failure: it shows up in the atrium as an
                // unsettled structure rather than as lost progress.
                World.Knowledge?.FlagMisconception(World.PrimaryConceptId, 0.15f);
            }
        }

        /// <summary>
        /// Create. A crystal carried near a sounding string samples its frequency; released away
        /// from the strings, it commits into the next empty slot. Two or more slots let the play
        /// mote stagger them in, then hold them ringing together — a phrase becoming a chord.
        /// </summary>
        void EvaluateCreate(float dt)
        {
            if (World.Loop.Stage < LoopStage.Create) { UpdatePlayback(dt); return; }

            if (World.Hands != null)
            {
                _captureCrystal.TryGrab(World.Hands.Left, GrabRadius);
                _captureCrystal.TryGrab(World.Hands.Right, GrabRadius);
            }

            if (_captureCrystal.Held)
            {
                float near0 = Vector3.Distance(_captureCrystal.View.position, World.String0.Midpoint);
                if (near0 < 0.05f) _pendingCapture = World.String0.Frequency;
                else if (World.String1.Summoned &&
                         Vector3.Distance(_captureCrystal.View.position, World.String1.Midpoint) < 0.05f)
                    _pendingCapture = World.String1.Frequency;

                if (_pendingCapture > 0f)
                    _crystalMat.SetColor("_Tint", PrismPalette.Spectral(SoundScale.PitchClass(_pendingCapture)));
            }

            var released = _captureCrystal.Drag(World.WorldAnchor, dt);
            if (released.HasValue)
            {
                if (_pendingCapture > 0f && _filledCount < ChordSlotCount) CommitCapture(_pendingCapture);
                _pendingCapture = 0f;
                _crystalMat.SetColor("_Tint", PrismPalette.Warm);
            }

            _playMote.position = World.WorldAnchor.TransformPoint(PlayMoteLocal);
            if (_filledCount >= 2 && TouchedPlayMote()) TriggerPlayback();

            UpdatePlayback(dt);
        }

        void CommitCapture(float freq)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Filled) continue;
                _slots[i].Filled = true;
                _slots[i].Frequency = freq;
                _slots[i].View.position = World.WorldAnchor.TransformPoint(SlotLocal(i));
                _slots[i].View.gameObject.SetActive(true);
                _slots[i].Mat.SetColor("_Tint", PrismPalette.Spectral(SoundScale.PitchClass(freq)));
                _filledCount++;
                World.Loop.Evidence.Record(SoundEvidence.ChordNote);
                return;
            }
        }

        bool TouchedPlayMote()
        {
            var hands = World.Hands;
            if (hands == null) return false;
            if (hands.Left != null && hands.Left.IsTracked && hands.Left.PinchDown &&
                (PrismHands.PointOf(hands.Left) - _playMote.position).sqrMagnitude < 0.0025f) return true;
            if (hands.Right != null && hands.Right.IsTracked && hands.Right.PinchDown &&
                (PrismHands.PointOf(hands.Right) - _playMote.position).sqrMagnitude < 0.0025f) return true;
            return false;
        }

        void TriggerPlayback()
        {
            float t = 0f;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].Filled) continue;
                if (_slots[i].Voice == null)
                    _slots[i].Voice = new SoundSynth.ToneVoice(_slots[i].View.gameObject, World.SharedClip,
                                                                SoundScale.ReferenceFrequency);
                _slots[i].PlayDelay = t;
                t += 0.16f;
            }
            _playElapsed = 0f;
            World.Loop.Evidence.Record(SoundEvidence.ChordPlayed);
            World.Knowledge?.Confirm(World.PrimaryConceptId, 0.35f);
        }

        void UpdatePlayback(float dt)
        {
            if (_playElapsed < 0f) return;
            _playElapsed += dt;

            bool anyRinging = false;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].Filled || _slots[i].Voice == null) continue;

                bool ringing = _playElapsed >= _slots[i].PlayDelay
                             && (_playElapsed - _slots[i].PlayDelay) < ChordNoteSeconds;

                _slots[i].Voice.Source.transform.position = _slots[i].View.position;
                _slots[i].Voice.SetFrequency(_slots[i].Frequency);
                _slots[i].Voice.SetTargetVolume(ringing ? ChordNoteGain : 0f);
                _slots[i].Voice.Tick(dt);

                if (ringing) anyRinging = true;
            }

            if (!anyRinging && _playElapsed > 0.25f) _playElapsed = -1f;
        }
    }
}
