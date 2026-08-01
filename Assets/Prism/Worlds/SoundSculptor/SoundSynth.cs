using UnityEngine;

namespace Prism.Worlds.SoundSculptor
{
    /// <summary>
    /// Procedural audio for this world, built the same way Companion.PrismVoice builds its clips:
    /// real additive synthesis, real partials, AudioClip.Create, no imported sound anywhere.
    ///
    /// The difference is that a string here needs a CONTINUOUSLY variable pitch while the learner's
    /// hand is moving it, and baking a fresh AudioClip every frame would both allocate constantly
    /// (forbidden) and be far too slow. So one small clip is built ONCE, containing a fundamental
    /// plus a couple of harmonics at a fixed reference frequency, engineered to loop with exactly
    /// zero phase discontinuity, and every string retunes the SAME clip by changing
    /// AudioSource.pitch. That is real resampling of a real waveform — the same physical operation
    /// as playing a record at the wrong speed — not an approximation of a pitch shift, and because
    /// every partial in the clip is an exact integer multiple of the base frequency, scaling
    /// playback speed keeps them in that exact ratio at any pitch. Two such voices playing at once
    /// therefore genuinely sum in the mix, and any beating between them is real interference, not
    /// an effect layered on top afterwards.
    /// </summary>
    public static class SoundSynth
    {
        public const int SampleRate = 48000;

        /// <summary>
        /// A steady, seamlessly-looping tone at <paramref name="baseFreq"/> made of the given
        /// harmonic gains (index 0 = fundamental, 1 = 2nd harmonic, ...). No attack/release is
        /// baked in on purpose: this clip loops forever, and a fade baked into a loop would repeat
        /// audibly every cycle. Attack and release instead live on AudioSource.volume at runtime —
        /// see <see cref="ToneVoice"/> — which is the loop-compatible equivalent of the
        /// raised-cosine envelopes PrismVoice bakes into its one-shot clips.
        /// </summary>
        public static AudioClip LoopingTone(string name, float baseFreq, float[] harmonicGains, int cycles = 55)
        {
            // `cycles` whole periods of the fundamental. Every harmonic is an exact integer
            // multiple of the fundamental, so it too completes a whole number of its own cycles in
            // this span — sample[n-1] flows into sample[0] with continuous phase AND slope, so the
            // loop point is silent to the ear. At baseFreq = 220 Hz and cycles = 55 this is exactly
            // 0.25 s = 12000 samples at 48 kHz, with no rounding at all.
            double duration = cycles / (double)Mathf.Max(baseFreq, 1f);
            int n = Mathf.Max(8, (int)System.Math.Round(duration * SampleRate));

            var data = new float[n];
            double twoPi = 2.0 * System.Math.PI;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SampleRate;
                float s = 0f;
                for (int k = 0; k < harmonicGains.Length; k++)
                {
                    double phase = twoPi * baseFreq * (k + 1) * t;
                    s += Mathf.Sin((float)phase) * harmonicGains[k];
                }
                data[i] = s;
            }

            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// One continuously-retunable voice. Wraps a looping AudioSource whose pitch multiplier
        /// maps the shared base clip onto whatever real frequency the simulation currently wants,
        /// and whose volume eases toward a target instead of jumping — nothing in this world has a
        /// sharp attack, including a tone that is only just now being summoned.
        /// </summary>
        public class ToneVoice
        {
            public readonly AudioSource Source;
            readonly float _baseFreq;
            float _targetVolume;

            public ToneVoice(GameObject host, AudioClip sharedClip, float baseFreq)
            {
                Source = host.AddComponent<AudioSource>();
                Source.clip = sharedClip;
                Source.loop = true;
                Source.playOnAwake = false;
                Source.spatialBlend = 1f;
                Source.rolloffMode = AudioRolloffMode.Linear;
                Source.minDistance = 0.25f;
                Source.maxDistance = 5f;
                Source.dopplerLevel = 0f;   // a string does not whoosh when it is stretched
                Source.volume = 0f;
                _baseFreq = Mathf.Max(baseFreq, 1e-3f);
            }

            /// <summary>Real frequency this voice should sound at right now.</summary>
            public void SetFrequency(float hz) => Source.pitch = Mathf.Clamp(hz / _baseFreq, 0.05f, 20f);

            /// <summary>Where the volume is easing to. 0 fades the voice out and stops it.</summary>
            public void SetTargetVolume(float v) => _targetVolume = Mathf.Max(0f, v);

            public float CurrentVolume => Source.volume;

            /// <summary>Ease the real volume toward the target, frame-rate independent, and
            /// start/stop the source so a fully faded-out voice is not silently still playing.</summary>
            public void Tick(float dt)
            {
                if (_targetVolume > 0.0005f && !Source.isPlaying) Source.Play();

                float rate = 1f - Mathf.Exp(-dt * 5f);
                Source.volume = Mathf.Lerp(Source.volume, _targetVolume, rate);

                if (_targetVolume <= 0.0005f && Source.volume < 0.001f && Source.isPlaying)
                    Source.Stop();
            }
        }
    }
}
