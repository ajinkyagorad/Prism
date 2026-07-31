using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using UnityEngine;

namespace Prism.Worlds.SoundSculptor
{
    /// <summary>
    /// STANDING WAVES — pick the mode with your hand, and hear it as well as see it.
    ///
    /// The free hand's height chooses an integer mode 1-5. The glowing outline is the real
    /// envelope of a fixed-fixed string, A*|sin(mode*pi*x/L)| — n lobes for mode n, exactly the
    /// shape (and, via SoundScale.FrequencyOf on the SAME reference length the full world uses,
    /// exactly the pitch) a real string would have. Small marks under the string count the mode
    /// number redundantly, so a learner can check the lobes against the marks against the pitch —
    /// three readings of the same one number.
    /// </summary>
    [ConceptDemoFor("standing-waves")]
    public class StandingWavesDemo : ConceptDemo
    {
        const int Samples = 96;
        const int MaxMode = 5;
        const float BaseGain = 0.11f;

        CurveView _string;
        Transform _core;
        Material _coreMat;
        Transform[] _modeMarks;
        SoundSynth.ToneVoice _voice;
        readonly List<Vector3> _pts = new List<Vector3>(Samples);

        public override void Build()
        {
            _string = Curve(Tint, 0.010f);

            _coreMat = PrismMaterials.CeramicBody(Tint, 0.6f);
            _core = Body(PrismMesh.Icosphere(2), 0.045f, _coreMat, "core");
            _core.localPosition = new Vector3(0f, -0.74f, 0f);

            _modeMarks = new Transform[MaxMode];
            for (int i = 0; i < MaxMode; i++)
            {
                _modeMarks[i] = Ball(0.014f, PrismPalette.Warm, 0.4f, "mark" + i);
                _modeMarks[i].localPosition =
                    new Vector3(Mathf.Lerp(-0.4f, 0.4f, i / (float)(MaxMode - 1)), -0.55f, 0f);
            }

            var clip = SoundSynth.LoopingTone("StandingWaveTone", SoundScale.ReferenceFrequency,
                                              new[] { 0.55f, 0.20f });
            _voice = new SoundSynth.ToneVoice(_core.gameObject, clip, SoundScale.ReferenceFrequency);
        }

        protected override void OnTick(float dt)
        {
            bool hand = TryFreeLocal(out var h);
            int mode = hand
                ? Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, MaxMode, Mathf.InverseLerp(-0.6f, 0.6f, h.y))), 1, MaxMode)
                : 1;

            // The same function the full Sound Sculptor world uses, at its own reference length —
            // pick this concept up after visiting that world and the numbers will feel familiar.
            float freq = SoundScale.FrequencyOf(SoundScale.ReferenceLength, mode);
            Color col = PrismPalette.Spectral(SoundScale.PitchClass(freq));

            _pts.Clear();
            for (int i = 0; i < Samples; i++)
            {
                float t = i / (float)(Samples - 1);
                // Trace the lens outline: forward along the top envelope, back along the bottom —
                // the honest picture of what a point on the string sweeps, not one instant of it
                // (see SoundString.RebuildMesh in the full world for why an instant would lie).
                bool upper = t < 0.5f;
                float x01 = upper ? t * 2f : 1f - (t - 0.5f) * 2f;
                float x = Mathf.Lerp(-0.8f, 0.8f, x01);
                float env = Mathf.Abs(Mathf.Sin(mode * Mathf.PI * x01)) * 0.34f;
                _pts.Add(new Vector3(x, upper ? env : -env, 0f));
            }
            _string.Set(_pts);
            _string.Mat.SetColor("_Tint", col);
            _coreMat.SetColor("_Tint", col);

            for (int i = 0; i < _modeMarks.Length; i++)
                _modeMarks[i].localScale = Vector3.one * ((i < mode) ? 0.016f : 0.007f);

            _voice.SetFrequency(freq);
            // Quieter and half-present at rest, so a held-but-unoperated concept does not drone.
            _voice.SetTargetVolume((hand ? 1f : 0.2f) * BaseGain * Openness);
            _voice.Tick(dt);
        }
    }

    /// <summary>
    /// HARMONIC RATIO — drag the second tone and feel where it locks.
    ///
    /// A root tone is fixed; the free hand drags a second tone's pitch continuously. A small light
    /// between them pulses at the REAL beat frequency (SoundScale.NearestCoincidence — the same
    /// coincidence-of-harmonics calculation the full world gates its Discover stage on) and turns
    /// gold and steadies the instant the ratio is genuinely simple. Nothing here is a rule stated in
    /// words; it is a rule you can find with your hand and confirm with your ear.
    /// </summary>
    [ConceptDemoFor("harmonic-ratio")]
    public class HarmonicRatioDemo : ConceptDemo
    {
        const float MinRatio = 0.55f, MaxRatio = 2.4f;
        const float RestRatio = 1.5f;      // rests on the fifth: pleasant, and already a fact to notice
        const float VoiceGain = 0.10f;

        CurveView _link;
        Transform _root, _free, _confluence;
        Material _freeMat, _confluenceMat;
        SoundSynth.ToneVoice _rootVoice, _freeVoice;
        float _freeX;
        float _confluencePhase;
        bool _wasLocked;

        static float RatioForX(float x) => Mathf.Lerp(MinRatio, MaxRatio, Mathf.InverseLerp(-0.85f, 0.85f, x));
        static float XForRatio(float r) => Mathf.Lerp(-0.85f, 0.85f, Mathf.InverseLerp(MinRatio, MaxRatio, r));

        public override void Build()
        {
            _link = Curve(PrismPalette.Warm, 0.005f);

            var rootMat = PrismMaterials.CeramicBody(Tint, 0.65f);
            _root = Body(PrismMesh.Icosphere(2), 0.06f, rootMat, "root");
            _root.localPosition = new Vector3(-0.45f, 0f, 0f);

            _freeMat = PrismMaterials.CeramicBody(PrismPalette.Cyan, 0.65f);
            _free = Body(PrismMesh.Icosphere(2), 0.05f, _freeMat, "free");

            _confluenceMat = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.4f);
            _confluence = Body(PrismMesh.Icosphere(1), 0.03f, _confluenceMat, "confluence");

            var clip = SoundSynth.LoopingTone("HarmonicRatioTone", SoundScale.ReferenceFrequency,
                                              new[] { 0.70f, 0.25f });
            _rootVoice = new SoundSynth.ToneVoice(_root.gameObject, clip, SoundScale.ReferenceFrequency);
            _freeVoice = new SoundSynth.ToneVoice(_free.gameObject, clip, SoundScale.ReferenceFrequency);

            _freeX = XForRatio(RestRatio);
        }

        protected override void OnTick(float dt)
        {
            bool hand = TryFreeLocal(out var h);
            float targetX = hand ? Mathf.Clamp(h.x, -0.85f, 0.85f) : XForRatio(RestRatio);
            _freeX = Mathf.Lerp(_freeX, targetX, dt * (hand ? 10f : 3f));
            _free.localPosition = new Vector3(_freeX, 0f, 0f);

            float ratio = RatioForX(_freeX);
            const float rootFreq = SoundScale.ReferenceFrequency;
            float freeFreq = rootFreq * ratio;

            var c = SoundScale.NearestCoincidence(rootFreq, freeFreq);
            bool locked = SoundScale.IsLocked(c.BeatHz);

            _freeMat.SetColor("_Tint", PrismPalette.Spectral(SoundScale.PitchClass(freeFreq)));
            Segment(_link, _root.localPosition, _free.localPosition);

            _confluence.localPosition = (_root.localPosition + _free.localPosition) * 0.5f + Vector3.up * 0.14f;

            float beatHz = Mathf.Min(c.BeatHz, 10f);
            _confluencePhase = Mathf.Repeat(_confluencePhase + beatHz * dt * Mathf.PI * 2f, Mathf.PI * 2f);
            float pulse = 0.5f + 0.5f * Mathf.Cos(_confluencePhase);
            _confluence.localScale = Vector3.one * Mathf.Lerp(0.022f, 0.045f, pulse);
            _confluenceMat.SetColor("_Tint", locked ? PrismPalette.Gold : PrismPalette.Warm);
            _confluenceMat.SetFloat("_Luminance", Mathf.Lerp(0.3f, 0.8f, pulse));

            // A discrete confirmation on the moment of arrival, layered over the continuous real
            // tones rather than replacing them — the same vocabulary SymmetryDemo's snap uses.
            if (locked && !_wasLocked) Voice?.Consonance(transform.position, 0.4f, 1f);
            _wasLocked = locked;

            _rootVoice.SetFrequency(rootFreq);
            _rootVoice.SetTargetVolume(VoiceGain * Openness);
            _rootVoice.Tick(dt);

            _freeVoice.SetFrequency(freeFreq);
            _freeVoice.SetTargetVolume(VoiceGain * Openness);
            _freeVoice.Tick(dt);
        }
    }

    /// <summary>
    /// TIMBRE — reach toward an overtone and pull it into the sound.
    ///
    /// One pitch, six real partials (harmonics 1-6 of the same root, each its own AudioSource
    /// resampling one shared pure tone — see SoundSynth). The free hand's proximity to each
    /// coloured mote sets that harmonic's own volume, continuously and independently, so drawing
    /// the hand across the six is drawing a hand across the sound's actual spectrum. The pitch
    /// (harmonic 1) never fully vanishes; everything else is entirely the learner's to add or take
    /// away. This is the fact most people never get to notice: that "what instrument is this" and
    /// "what note is this" are answered by completely different information in the same sound.
    /// </summary>
    [ConceptDemoFor("timbre")]
    public class TimbreDemo : ConceptDemo
    {
        const int N = 6;
        const float InfluenceRadius = 0.22f;
        const float BaseGain = 0.09f;
        const float RootFreq = 220f;

        Transform _root;
        Material _rootMat;
        Transform[] _motes;
        Vector3[] _motePos;
        float[] _gain;
        SoundSynth.ToneVoice[] _voices;

        public override void Build()
        {
            _rootMat = PrismMaterials.CeramicBody(Tint, 0.55f);
            _root = Body(PrismMesh.Icosphere(2), 0.075f, _rootMat, "root");

            // One pure sine, one partial, one clip — every voice below is this SAME clip resampled
            // to an exact integer multiple of it, which is what keeps six independently-volumed
            // AudioSources genuinely a harmonic series rather than six unrelated pitches.
            var clip = SoundSynth.LoopingTone("TimbrePartial", RootFreq, new[] { 1f });

            _motes = new Transform[N];
            _motePos = new Vector3[N];
            _gain = new float[N];
            _voices = new SoundSynth.ToneVoice[N];

            for (int k = 0; k < N; k++)
            {
                var mat = PrismMaterials.CeramicBody(PrismPalette.Spectral(k / (float)(N - 1)), 0.5f);
                var mote = Body(PrismMesh.Icosphere(1), 0.03f, mat, "harmonic" + (k + 1));
                _motePos[k] = new Vector3(0.55f, Mathf.Lerp(-0.6f, 0.6f, k / (float)(N - 1)), 0f);
                mote.localPosition = _motePos[k];
                _motes[k] = mote;
                _voices[k] = new SoundSynth.ToneVoice(mote.gameObject, clip, RootFreq);
            }
        }

        protected override void OnTick(float dt)
        {
            bool hand = TryFreeLocal(out var h);
            float total = 0f;

            for (int k = 0; k < N; k++)
            {
                float proximity = 0f;
                if (hand)
                {
                    float d = Vector3.Distance(h, _motePos[k]);
                    float t = Mathf.Clamp01(d / InfluenceRadius);
                    proximity = 1f - t * t;
                }
                // Higher harmonics cap lower even at full reach, the way a real instrument's
                // overtones roll off — so the sixth harmonic can colour the tone but never own it.
                float target = proximity / (k + 1);
                if (k == 0) target = Mathf.Max(target, 0.55f);   // the pitch itself never disappears

                _gain[k] = Mathf.Lerp(_gain[k], target, dt * 8f);
                total += _gain[k];

                _voices[k].SetFrequency(RootFreq * (k + 1));
                _voices[k].SetTargetVolume(_gain[k] * BaseGain * Openness);
                _voices[k].Tick(dt);

                _motes[k].localScale = Vector3.one * Mathf.Lerp(0.024f, 0.05f, Mathf.Clamp01(_gain[k]));
            }

            float richness = Mathf.Clamp01(total / 2.2f);
            _root.localScale = Vector3.one * Mathf.Lerp(0.07f, 0.10f, richness);
            _rootMat.SetFloat("_Luminance", Mathf.Lerp(0.45f, 0.8f, richness));
        }
    }

    /// <summary>
    /// OCTAVE EQUIVALENCE — climb a spiral, and watch it collapse onto a wheel.
    ///
    /// The free hand's height slides a bead up a rising helix spanning three octaves; the SAME
    /// bead's position is also drawn projected straight down onto a flat ring below. Height on the
    /// helix keeps climbing, but the ring position — and the bead's own colour, its pitch class —
    /// repeats exactly once per turn, because a turn of the helix IS an octave. The tone plays
    /// together with its real octave above throughout, so the "same note, twice the frequency"
    /// claim is heard at the same moment its geometry is seen collapsing onto one point.
    /// </summary>
    [ConceptDemoFor("octave-equivalence")]
    public class OctaveEquivalenceDemo : ConceptDemo
    {
        const float BaseFreq = 110f;     // A2
        const float OctaveSpan = 3f;     // three full turns of climb
        const float Radius = 0.42f;
        const float BaseGain = 0.095f;

        CurveView _helix, _ring, _drop;
        Transform _helixBead, _ringBead;
        Material _helixBeadMat, _ringBeadMat;
        SoundSynth.ToneVoice _voiceLow, _voiceHigh;
        float _h = 1f;                   // continuous height in octaves, 0..OctaveSpan

        public override void Build()
        {
            _helix = Curve(PrismPalette.Warm, 0.006f);
            _helix.Set(t =>
            {
                float h = t * OctaveSpan;
                float a = Mathf.Repeat(h, 1f) * Mathf.PI * 2f;
                return new Vector3(Mathf.Cos(a) * Radius, Mathf.Lerp(-0.6f, 0.6f, t), Mathf.Sin(a) * Radius);
            }, 180);

            _ring = Curve(PrismPalette.Warm, 0.004f);
            _ring.Set(t =>
            {
                float a = t * Mathf.PI * 2f;
                return new Vector3(Mathf.Cos(a) * Radius, -0.62f, Mathf.Sin(a) * Radius);
            }, 64);

            _drop = Curve(PrismPalette.Warm, 0.003f);

            _helixBeadMat = PrismMaterials.CeramicBody(Tint, 0.7f);
            _helixBead = Body(PrismMesh.Icosphere(2), 0.045f, _helixBeadMat, "helixBead");

            _ringBeadMat = PrismMaterials.CeramicBody(Tint, 0.7f);
            _ringBead = Body(PrismMesh.Icosphere(2), 0.05f, _ringBeadMat, "ringBead");

            var clip = SoundSynth.LoopingTone("OctaveTone", SoundScale.ReferenceFrequency, new[] { 1f, 0.30f });
            _voiceLow = new SoundSynth.ToneVoice(_helixBead.gameObject, clip, SoundScale.ReferenceFrequency);
            _voiceHigh = new SoundSynth.ToneVoice(_ringBead.gameObject, clip, SoundScale.ReferenceFrequency);
        }

        protected override void OnTick(float dt)
        {
            bool hand = TryFreeLocal(out var hpos);
            float target = hand
                ? Mathf.Clamp(Mathf.InverseLerp(-0.65f, 0.65f, hpos.y) * OctaveSpan, 0f, OctaveSpan)
                : 1f;
            _h = Mathf.Lerp(_h, target, dt * (hand ? 8f : 2f));

            float freq = BaseFreq * Mathf.Pow(2f, _h);
            float angle = Mathf.Repeat(_h, 1f) * Mathf.PI * 2f;
            Color col = PrismPalette.Spectral(SoundScale.PitchClass(freq));

            Vector3 onHelix = new Vector3(Mathf.Cos(angle) * Radius, Mathf.Lerp(-0.6f, 0.6f, _h / OctaveSpan),
                                          Mathf.Sin(angle) * Radius);
            Vector3 onRing = new Vector3(Mathf.Cos(angle) * Radius, -0.62f, Mathf.Sin(angle) * Radius);

            _helixBead.localPosition = onHelix;
            _ringBead.localPosition = onRing;
            _helixBeadMat.SetColor("_Tint", col);
            _ringBeadMat.SetColor("_Tint", col);
            Segment(_drop, onHelix, onRing);

            _voiceLow.SetFrequency(freq);
            _voiceLow.SetTargetVolume(BaseGain * Openness);
            _voiceLow.Tick(dt);

            _voiceHigh.SetFrequency(freq * 2f);
            _voiceHigh.SetTargetVolume(BaseGain * Openness);
            _voiceHigh.Tick(dt);
        }
    }
}
