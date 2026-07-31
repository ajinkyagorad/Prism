using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using Prism.Worlds;
using UnityEngine;

namespace Prism.Worlds.SoundSculptor
{
    /// <summary>Evidence keys. Named constants because the gates, the world and the challenge must agree exactly.</summary>
    public static class SoundEvidence
    {
        public const string Stretched         = "stretched";
        public const string StringSummoned    = "string.summoned";
        public const string FoundLocked       = "found.locked";
        public const string FoundBeating      = "found.beating";
        public const string LockedOctave      = "locked.octave";
        public const string LockedFifth       = "locked.fifth";
        public const string DistinctLock      = "locked.distinct";
        public const string TuneDone          = "tune.done";
        public const string PredictionAttempt = "prediction.attempt";
        public const string PredictionGood    = "prediction.good";
        public const string ChordNote         = "chord.note";
        public const string ChordPlayed       = "chord.played";
    }

    /// <summary>
    /// The Sound Sculptor world: harmony is arithmetic you can hear.
    ///
    ///   Wonder     One tone sounds. A glowing spindle hangs in the air with it — the standing
    ///              wave's real envelope, |sin(pi x / L)|, not a decoration. Nothing is labelled.
    ///   Explore    Grab both ends and stretch it: length and pitch are audibly, visibly inverse.
    ///              Pull a second tone out of its cradle and it becomes a string of its own. With
    ///              only the ear to go on, the learner finds a pair that locks and a pair that
    ///              beats — the straddle.
    ///   Discover   A pulsing light appears between the two strings, beating at the literal
    ///              difference of the coinciding harmonics. Now they can SEE it slow to a stop
    ///              exactly when the ear says it has, and go find the octave and the fifth by name.
    ///   Formalize  Only now: a label names the ratio, and the beat is shown for what it always
    ///              was — |q*fHigh - p*fLow|, the difference of two real frequencies.
    ///   Apply      Tune a string onto a ghost target interval, by ear and by hand.
    ///   Explain    Predict, before it sounds, whether a proposed pair will lock or beat.
    ///   Create     Capture tones into a chord of the learner's own, and play it back.
    ///   Connect    Return to the atrium; the constellation has changed.
    /// </summary>
    public class SoundSculptorWorld : PrismWorldBase
    {
        public override string WorldId => "sound-sculptor";

        /// <summary>A resonant hall — standing patterns in the air are the subject.</summary>
        protected override WorldShellSpec Shell => WorldShellSpec.Resonator;
        public override string PrimaryConceptId => "sound-sculptor";
        public override string DisplayName => "Sound Sculptor";

        // This world writes no shaders of its own: every visible thing is built from the existing
        // Prism/Trail (string bodies, coloured by pitch class through its speed channel) and
        // Prism/Ceramic (handles, the confluence light) materials via PrismMaterials.
        public override string[] Shaders => System.Array.Empty<string>();

        const float GrabRadius = 0.05f;
        const float SummonThreshold = 0.055f;

        // Kept deliberately modest: unlike PrismVoice's stingers, these two tones play
        // CONTINUOUSLY for as long as the learner is in the world, so the safety margin that
        // matters is sustained-exposure loudness, not peak. PrismVoice bakes a blanket 0.28 into
        // every clip before its own gain/volume stage; the harmonic gains passed to
        // SoundSynth.LoopingTone below (0.40 + 0.18 + 0.07 = 0.65 peak) times this constant give a
        // comparable or quieter sustained level even with both strings sounding at once.
        const float VoiceGain = 0.13f;
        const float ConfluenceBaseRadius = 0.013f;

        /// <summary>Which grab point on a string a handle represents.</summary>
        class Handle
        {
            public HandleKind Kind;
            public SoundString Owner;
            public Transform View;
        }

        /// <summary>One hand's current grab state. Two of these exist so both hands can hold
        /// different handles at once — stretching a string needs exactly that.</summary>
        class HandGrab
        {
            public Handle Held;
            public Vector3 OffsetA, OffsetB;   // captured at grab time, used only for Mid
        }

        SoundString _str0, _str1;
        readonly List<Handle> _handles = new List<Handle>(6);
        readonly HandGrab _left = new HandGrab();
        readonly HandGrab _right = new HandGrab();

        AudioClip _sharedClip;
        Transform _confluence;
        Material _confluenceMat;
        float _confluencePhase;
        PrismLabel _label;

        SoundSculptorChallenge _challenge;

        readonly HashSet<string> _seenLockNames = new HashSet<string>();
        bool _wasLocked, _wasBeating;
        SoundScale.Coincidence _lastCoincidence;

        // ---- exposed to SoundSculptorChallenge, which is a sibling component rather than a
        // subclass and so cannot see PrismWorldBase's protected Anchor ----
        public SoundString String0 => _str0;
        public SoundString String1 => _str1;
        public Transform WorldAnchor => Anchor;
        public Vector3 ConfluencePosition => _confluence != null ? _confluence.position : Anchor.position;
        public SoundScale.Coincidence LastCoincidence => _lastCoincidence;
        public AudioClip SharedClip => _sharedClip;

        // -----------------------------------------------------------------
        // concepts
        // -----------------------------------------------------------------

        public override IEnumerable<ConceptSpec> Concepts
        {
            get
            {
                yield return new ConceptSpec
                {
                    Id = "sound-sculptor",
                    Title = "Sound Sculptor",
                    Domain = ConceptDomain.ArtAndMusic,
                    Kind = KnowledgeKind.System,
                    Direction = Bearing(270f, 0.05f),
                    Distance = 3.3f,
                    WorldId = WorldId,
                    Capability = "Stretch a tone with both hands, summon a second, and hear when two lengths lock.",
                    Formalisation = "A string's fundamental is f = v/(2L); two tones lock when their ratio " +
                                    "is a small integer fraction and beat otherwise, at |q*fHigh - p*fLow| Hz.",
                    Links = new (string target, Relation relation, float strength)[]
                    {
                        ("standing-waves", Relation.Composes,   0.85f),
                        ("harmonic-ratio", Relation.Constrains, 0.90f),
                        ("timbre",         Relation.Composes,   0.60f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "standing-waves",
                    Title = "Standing Waves",
                    Domain = ConceptDomain.MatterAndEnergy,
                    Kind = KnowledgeKind.System,
                    Direction = Bearing(259f, 0.20f),
                    Distance = 3.9f,
                    Capability = "Read a string's pitch straight off its length and mode number.",
                    Formalisation = "n half-wavelengths fit a fixed-fixed string of length L at frequency " +
                                    "f_n = n*v/(2L); the envelope of motion at each point is A*|sin(n*pi*x/L)|.",
                    Links = new (string target, Relation relation, float strength)[]
                    {
                        ("waves",           Relation.Composes, 0.85f),
                        ("periodic-motion", Relation.Analogy,  0.80f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "harmonic-ratio",
                    Title = "Harmonic Ratio",
                    Domain = ConceptDomain.Mathematics,
                    Kind = KnowledgeKind.Equation,
                    Direction = Bearing(281f, -0.15f),
                    Distance = 4.2f,
                    Capability = "Predict, from the ratio alone, whether two tones will lock or beat.",
                    Formalisation = "Two frequencies with ratio p:q (small integers) share a coinciding " +
                                    "harmonic at q*fHigh = p*fLow; the beat there is |q*fHigh - p*fLow|.",
                    Links = new (string target, Relation relation, float strength)[]
                    {
                        ("rhythm",           Relation.Analogy,   0.70f),
                        ("periodic-motion",  Relation.Constrains,0.55f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "timbre",
                    Title = "Timbre",
                    Domain = ConceptDomain.ArtAndMusic,
                    Kind = KnowledgeKind.Fact,
                    Direction = Bearing(265f, 0.35f),
                    Distance = 4.7f,
                    Capability = "Say why two tones at the same pitch can still sound different.",
                    Formalisation = "Timbre is the relative strength of the harmonics above the fundamental; " +
                                    "changing their balance changes colour without changing pitch.",
                    Links = new (string target, Relation relation, float strength)[]
                    {
                        ("standing-waves", Relation.Composes, 0.65f),
                    }
                };

                yield return new ConceptSpec
                {
                    Id = "octave-equivalence",
                    Title = "Octave Equivalence",
                    Domain = ConceptDomain.ArtAndMusic,
                    Kind = KnowledgeKind.Fact,
                    Direction = Bearing(277f, -0.30f),
                    Distance = 5.1f,
                    Capability = "Recognise the same pitch class a register away, by ear or by hue.",
                    Formalisation = "Frequencies related by a power of two are heard as the same note in a " +
                                    "different register — why pitch class is drawn as a repeating colour here.",
                    Links = new (string target, Relation relation, float strength)[]
                    {
                        ("harmonic-ratio", Relation.Instantiates, 0.80f),
                        ("symmetry",       Relation.Analogy,      0.50f),
                    }
                };
            }
        }

        static Vector3 Bearing(float azimuthDeg, float elevation)
        {
            float a = azimuthDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), elevation, Mathf.Cos(a));
        }

        // -----------------------------------------------------------------
        // construction
        // -----------------------------------------------------------------

        protected override void BuildWorld()
        {
            // One shared, once-built clip: a fundamental plus two soft harmonics, looping with
            // zero phase discontinuity (see SoundSynth). Every voice in this world — both strings
            // and every captured chord note — retunes this SAME clip; nothing is synthesised again
            // after this call, and every AudioSource here just resamples it by changing pitch.
            _sharedClip = SoundSynth.LoopingTone("SoundSculptorTone", SoundScale.ReferenceFrequency,
                                                  new[] { 0.40f, 0.18f, 0.07f });

            _str0 = MakeString("StringA", new Vector3(0.00f, 0.06f, 0.05f));
            _str1 = MakeString("StringB", new Vector3(0.24f, -0.02f, -0.02f));

            MakeHandlesFor(_str0);
            MakeHandlesFor(_str1);

            BuildConfluence();
            _label = PrismLabel.Create("IntervalLabel", Anchor, Head, 0.015f);

            _challenge = gameObject.AddComponent<SoundSculptorChallenge>();
            _challenge.World = this;

            ResetLayout();
        }

        SoundString MakeString(string name, Vector3 cradleLocal)
        {
            var s = new SoundString { Name = name, CradleLocalOffset = cradleLocal };

            var go = new GameObject(name);
            go.transform.SetParent(Anchor, false);
            var mf = go.AddComponent<MeshFilter>();
            s.TubeMesh = new Mesh { name = name + "Tube" };
            s.TubeMesh.MarkDynamic();
            mf.sharedMesh = s.TubeMesh;

            // Prism/Trail, repurposed: its speed channel becomes pitch class (see SoundString and
            // SoundScale.PitchClass), so colour reads the same spectral ramp everything else in
            // PRISM uses, and an octave always returns to the same hue.
            s.Mat = PrismMaterials.TrailMaterial(0f, 1f);
            s.Mat.SetFloat("_Opacity", 0.92f);
            s.Mat.SetFloat("_HeadGain", 1.1f);
            go.AddComponent<MeshRenderer>().sharedMaterial = s.Mat;
            s.View = go.transform;

            var voiceGO = new GameObject(name + "Voice");
            voiceGO.transform.SetParent(Anchor, false);
            s.Voice = new SoundSynth.ToneVoice(voiceGO, _sharedClip, SoundScale.ReferenceFrequency);

            return s;
        }

        void MakeHandlesFor(SoundString s)
        {
            AddHandle(s, HandleKind.EndA, PrismPalette.Cyan, 0.011f);
            AddHandle(s, HandleKind.EndB, PrismPalette.Cyan, 0.011f);
            AddHandle(s, HandleKind.Mid,  PrismPalette.Gold, 0.014f);
        }

        void AddHandle(SoundString s, HandleKind kind, Color tint, float radius)
        {
            var go = new GameObject(s.Name + kind);
            go.transform.SetParent(Anchor, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(1);
            var mat = PrismMaterials.CeramicBody(tint, 0.40f);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * radius;

            _handles.Add(new Handle { Kind = kind, Owner = s, View = go.transform });
        }

        void BuildConfluence()
        {
            var go = new GameObject("Confluence");
            go.transform.SetParent(Anchor, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            _confluenceMat = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.30f);
            go.AddComponent<MeshRenderer>().sharedMaterial = _confluenceMat;
            go.transform.localScale = Vector3.one * ConfluenceBaseRadius;
            _confluence = go.transform;
            _confluence.gameObject.SetActive(false);
        }

        /// <summary>
        /// Spatial layout only — never simulation state. Called on every entry (first or a return
        /// visit), because PlaceInFrontOfUser moves the anchor to wherever the learner is now
        /// standing, and content authored relative to the OLD anchor position would otherwise be
        /// left behind in space. Progress (the loop stage, evidence) lives in Loop and Knowledge,
        /// neither of which this touches.
        /// </summary>
        void ResetLayout()
        {
            Vector3 half = Anchor.right * (SoundScale.ReferenceLength * 0.5f);
            Vector3 centre0 = Anchor.TransformPoint(new Vector3(0.00f, 0.06f, 0.05f));
            _str0.EndA = centre0 - half;
            _str0.EndB = centre0 + half;
            _str0.Summoned = true;

            _str1.Summoned = false;
            Vector3 cradle = Anchor.TransformPoint(_str1.CradleLocalOffset);
            _str1.EndA = cradle;
            _str1.EndB = cradle;

            _left.Held = null;
            _right.Held = null;
            _wasLocked = false;
            _wasBeating = false;
            _seenLockNames.Clear();
            _confluencePhase = 0f;

            if (_confluence != null) _confluence.gameObject.SetActive(false);
            _label?.Show(false);

            _challenge?.ResetChallenges();
        }

        protected override void OnEnter() => ResetLayout();

        // -----------------------------------------------------------------
        // the loop
        // -----------------------------------------------------------------

        protected override void ConfigureLoop(LearningLoop loop)
        {
            loop.AddGate(new LoopGate(LoopStage.Wonder,
                "has not yet taken hold of a string",
                e => e.Count(SoundEvidence.Stretched) >= 1));

            // The exemplar gate: the learner must have summoned a second tone and, going by ear
            // alone (nothing is revealed yet), have personally produced BOTH a pair that locks and
            // a pair that beats. Only having straddled that boundary are they ready to be shown it.
            loop.AddGate(new LoopGate(LoopStage.Explore,
                "has not yet found both a lock and a beat with a second tone",
                e => e.Has(SoundEvidence.StringSummoned)
                  && e.Has(SoundEvidence.FoundLocked)
                  && e.Has(SoundEvidence.FoundBeating)));

            loop.AddGate(new LoopGate(LoopStage.Discover,
                "has not yet found both the octave and the fifth by name",
                e => e.Has(SoundEvidence.LockedOctave) && e.Has(SoundEvidence.LockedFifth)));

            loop.AddGate(new LoopGate(LoopStage.Formalize,
                "has not yet found three different simple ratios",
                e => e.Count(SoundEvidence.DistinctLock) >= 3));

            loop.AddGate(new LoopGate(LoopStage.Apply,
                "has not yet tuned a string onto a target interval",
                e => e.Has(SoundEvidence.TuneDone)));

            loop.AddGate(new LoopGate(LoopStage.Explain,
                "has not yet predicted correctly, twice",
                e => e.Has(SoundEvidence.PredictionGood) && e.Count(SoundEvidence.PredictionAttempt) >= 2));

            loop.AddGate(new LoopGate(LoopStage.Create,
                "has not yet built and played a chord of their own",
                e => e.Count(SoundEvidence.ChordNote) >= 2 && e.Has(SoundEvidence.ChordPlayed)));
        }

        protected override void OnStageEntered(LoopStage stage)
        {
            base.OnStageEntered(stage);
            switch (stage)
            {
                case LoopStage.Apply:
                    _challenge?.BeginTune();
                    break;
                case LoopStage.Explain:
                    _challenge?.BeginPredict();
                    break;
                case LoopStage.Create:
                    _challenge?.BeginCreate();
                    break;
                case LoopStage.Connect:
                    Knowledge?.Confirm(PrimaryConceptId, 0.4f);
                    break;
            }
        }

        // -----------------------------------------------------------------
        // frame
        // -----------------------------------------------------------------

        protected override void Tick(float dt)
        {
            ServiceHand(Hands != null ? Hands.Left : null, _left);
            ServiceHand(Hands != null ? Hands.Right : null, _right);

            UpdateCradleIdle();

            bool drag0 = IsEndDragging(_str0);
            bool drag1 = IsEndDragging(_str1);
            _str0.ApplyLengthConstraint(_str1.Summoned ? _str1.Length : 0f, drag0 && _str1.Summoned, dt);
            if (_str1.Summoned) _str1.ApplyLengthConstraint(_str0.Length, drag1, dt);

            UpdateVoice(_str0, dt);
            UpdateVoice(_str1, dt);

            _str0.RebuildMesh();
            if (_str1.Summoned) _str1.RebuildMesh();
            _str1.View.gameObject.SetActive(_str1.Summoned);

            UpdateHandleViews();
            EvaluateIntervalEvidence();
            UpdateConfluence(dt);
            UpdateLabel();

            _challenge?.Tick(dt);
        }

        // -----------------------------------------------------------------
        // hands
        // -----------------------------------------------------------------

        void ServiceHand(PrismHands.Hand hand, HandGrab grab)
        {
            if (hand == null || !hand.IsTracked) { grab.Held = null; return; }

            if (grab.Held == null)
            {
                if (!hand.IsGrasping) return;
                var h = FindNearestHandle(PrismHands.PointOf(hand), hand);
                if (h == null) return;

                grab.Held = h;
                if (h.Kind == HandleKind.Mid)
                {
                    Vector3 p = PrismHands.PointOf(hand);
                    grab.OffsetA = h.Owner.EndA - p;
                    grab.OffsetB = h.Owner.EndB - p;
                }
                Hands.Buzz(hand, 0.18f, 0.05f);
                return;
            }

            var held = grab.Held;
            Vector3 point = PrismHands.PointOf(hand);
            switch (held.Kind)
            {
                case HandleKind.EndA:
                    held.Owner.EndA = point;
                    break;
                case HandleKind.EndB:
                    held.Owner.EndB = point;
                    break;
                default:
                    held.Owner.EndA = point + grab.OffsetA;
                    held.Owner.EndB = point + grab.OffsetB;
                    break;
            }

            if (!hand.IsGrasping) ReleaseHandle(grab, hand);
        }

        /// <summary>Nearest handle within grab range, falling back to an angular reach cone —
        /// mirrors PrismWorldBase.PointedAt, but per-hand, since two hands may hold two different
        /// handles at once here.</summary>
        Handle FindNearestHandle(Vector3 point, PrismHands.Hand hand)
        {
            Handle best = null;
            float bestD = GrabRadius * GrabRadius;
            for (int i = 0; i < _handles.Count; i++)
            {
                var h = _handles[i];
                if (h.Kind != HandleKind.Mid && !h.Owner.Summoned) continue;   // pre-summon: only the seed
                float d = (HandlePosition(h) - point).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; }
            }

            if (best == null && hand.ReachValid)
            {
                float cos = Mathf.Cos(Mathf.Deg2Rad * 12f);
                float bestScore = -1f;
                for (int i = 0; i < _handles.Count; i++)
                {
                    var h = _handles[i];
                    if (h.Kind != HandleKind.Mid && !h.Owner.Summoned) continue;
                    Vector3 to = HandlePosition(h) - hand.Reach.origin;
                    float dist = to.magnitude;
                    if (dist < 1e-4f || dist > 1.6f) continue;
                    float align = Vector3.Dot(hand.Reach.direction, to / dist);
                    if (align < cos) continue;
                    float score = align - dist * 0.02f;
                    if (score > bestScore) { bestScore = score; best = h; }
                }
            }
            return best;
        }

        static Vector3 HandlePosition(Handle h)
        {
            if (h.Kind == HandleKind.EndA) return h.Owner.EndA;
            if (h.Kind == HandleKind.EndB) return h.Owner.EndB;
            return h.Owner.Midpoint;
        }

        void ReleaseHandle(HandGrab grab, PrismHands.Hand hand)
        {
            var h = grab.Held;
            grab.Held = null;
            if (h == null) return;

            var s = h.Owner;

            if (!s.Summoned)
            {
                // The only handle grabbable before summoning is Mid, standing in for the whole
                // collapsed, silent string. Pulled far enough from its cradle, it opens up into a
                // real string; let go too close and it simply settles back, uncommitted.
                Vector3 cradle = Anchor.TransformPoint(s.CradleLocalOffset);
                if (Vector3.Distance(s.Midpoint, cradle) > SummonThreshold) SummonString(s, hand);
                else { s.EndA = cradle; s.EndB = cradle; }
                return;
            }

            if (h.Kind != HandleKind.Mid) Loop.Evidence.Record(SoundEvidence.Stretched);
        }

        void SummonString(SoundString s, PrismHands.Hand hand)
        {
            Vector3 centre = s.Midpoint;
            Vector3 axis = Anchor.right;
            float half = SoundScale.ReferenceLength * 0.5f;
            s.EndA = centre - axis * half;
            s.EndB = centre + axis * half;
            s.Summoned = true;

            Loop.Evidence.Record(SoundEvidence.StringSummoned);
            if (hand != null) Hands.Clunk(hand, 0.35f);
        }

        bool IsEndDragging(SoundString s)
        {
            if (_left.Held != null && _left.Held.Owner == s && _left.Held.Kind != HandleKind.Mid) return true;
            if (_right.Held != null && _right.Held.Owner == s && _right.Held.Kind != HandleKind.Mid) return true;
            return false;
        }

        void UpdateCradleIdle()
        {
            if (_str1.Summoned) return;
            if (_left.Held != null && _left.Held.Owner == _str1) return;
            if (_right.Held != null && _right.Held.Owner == _str1) return;

            Vector3 target = Anchor.TransformPoint(_str1.CradleLocalOffset);
            float bob = Mathf.Sin(Time.time * 0.8f) * 0.004f;
            Vector3 p = target + Vector3.up * bob;
            _str1.EndA = p;
            _str1.EndB = p;
        }

        // -----------------------------------------------------------------
        // audio + visuals
        // -----------------------------------------------------------------

        void UpdateVoice(SoundString s, float dt)
        {
            if (s.Summoned) s.Voice.SetFrequency(s.Frequency);
            s.Voice.SetTargetVolume(s.Summoned ? VoiceGain : 0f);
            s.Voice.Source.transform.position = s.Midpoint;
            s.Voice.Tick(dt);
            s.Loudness01 = Mathf.Clamp01(s.Voice.CurrentVolume / VoiceGain);
        }

        void UpdateHandleViews()
        {
            for (int i = 0; i < _handles.Count; i++)
            {
                var h = _handles[i];
                bool active = h.Kind == HandleKind.Mid || h.Owner.Summoned;
                if (h.View.gameObject.activeSelf != active) h.View.gameObject.SetActive(active);
                if (!active) continue;

                h.View.position = HandlePosition(h);

                bool held = _left.Held == h || _right.Held == h;
                float baseScale = h.Kind == HandleKind.Mid ? 0.014f : 0.011f;
                h.View.localScale = Vector3.one * (held ? baseScale * 1.35f : baseScale);
            }
        }

        /// <summary>
        /// The light between the two strings, revealed only from Discover onward. Its brightness
        /// pulses at the REAL beat rate computed in SoundScale.NearestCoincidence — a genuine
        /// physical quantity, not an animation standing in for one — so when the learner tunes
        /// onto a simple ratio the pulse visibly, honestly slows to a stop.
        /// </summary>
        void UpdateConfluence(float dt)
        {
            bool show = Loop.Stage >= LoopStage.Discover && _str1.Summoned;
            if (_confluence.gameObject.activeSelf != show) _confluence.gameObject.SetActive(show);
            if (!show) return;

            _confluence.position = (_str0.Midpoint + _str1.Midpoint) * 0.5f + Vector3.up * 0.05f;

            float beatHz = Mathf.Min(_lastCoincidence.BeatHz, 12f);   // capped for visual legibility
            _confluencePhase = Mathf.Repeat(_confluencePhase + beatHz * dt * Mathf.PI * 2f, Mathf.PI * 2f);
            float pulse = 0.5f + 0.5f * Mathf.Cos(_confluencePhase);

            _confluence.localScale = Vector3.one * ConfluenceBaseRadius * Mathf.Lerp(0.6f, 1.3f, pulse);

            float pc = SoundScale.PitchClass((_str0.Frequency + _str1.Frequency) * 0.5f);
            _confluenceMat.SetColor("_Tint", PrismPalette.Spectral(pc));
            _confluenceMat.SetFloat("_Luminance", Mathf.Lerp(0.22f, 0.55f, pulse));
        }

        /// <summary>The one piece of text in this world, and it says nothing until Formalize.</summary>
        void UpdateLabel()
        {
            bool show = Loop.Stage >= LoopStage.Formalize && _str1.Summoned;
            _label.Show(show);
            if (!show) return;

            var c = _lastCoincidence;
            string body = SoundScale.IsLocked(c.BeatHz)
                ? c.Ratio.P + ":" + c.Ratio.Q + "  " + c.Ratio.Name
                : "beat " + c.BeatHz.ToString("0.0") + " Hz";
            body += "\n" + Mathf.RoundToInt(_str0.Frequency) + " Hz : " + Mathf.RoundToInt(_str1.Frequency) + " Hz";

            _label.SetText(body, PrismPalette.Spectral(SoundScale.PitchClass(Mathf.Max(_str0.Frequency, _str1.Frequency))));
            _label.PlaceAbove(_confluence.position, 0.05f);
        }

        /// <summary>
        /// Turns the live physics into the evidence the loop is waiting for. Recorded on the
        /// RISING edge only (locked-and-was-not), so holding a lock for ten seconds counts once,
        /// not ten thousand times — the learner is credited with finding it, not with waiting.
        /// </summary>
        void EvaluateIntervalEvidence()
        {
            if (!_str1.Summoned) { _wasLocked = false; _wasBeating = false; return; }

            var c = SoundScale.NearestCoincidence(_str0.Frequency, _str1.Frequency);
            _lastCoincidence = c;
            bool locked = SoundScale.IsLocked(c.BeatHz);
            bool beating = SoundScale.IsBeating(c.BeatHz);

            if (locked && !_wasLocked)
            {
                bool first = !Loop.Evidence.Has(SoundEvidence.FoundLocked);
                Loop.Evidence.Record(SoundEvidence.FoundLocked);
                if (c.Ratio.P == 2 && c.Ratio.Q == 1) Loop.Evidence.Record(SoundEvidence.LockedOctave);
                if (c.Ratio.P == 3 && c.Ratio.Q == 2) Loop.Evidence.Record(SoundEvidence.LockedFifth);
                if (_seenLockNames.Add(c.Ratio.Name)) Loop.Evidence.Record(SoundEvidence.DistinctLock);
                if (first) Companion?.Voice?.Consonance(_confluence != null ? _confluence.position : _str1.Midpoint, 0.4f, 1f);
            }
            if (beating && !_wasBeating) Loop.Evidence.Record(SoundEvidence.FoundBeating);

            _wasLocked = locked;
            _wasBeating = beating;
        }
    }
}
