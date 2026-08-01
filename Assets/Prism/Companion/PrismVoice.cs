using UnityEngine;

namespace Prism.Companion
{
    /// <summary>
    /// PRISM's non-verbal audio. Every clip is synthesised at load; there are no audio assets.
    ///
    /// The brief asks for two specific things that ordinary UI sound cannot do: a correct
    /// connection should "resolve into musical consonance", and a contradiction should produce
    /// "spatial tension rather than harsh error sounds". Those are statements about intervals, so
    /// they are implemented as intervals:
    ///
    ///   consonance  a perfect fifth, 3:2. Two tones whose partials line up, which is why it
    ///               sounds settled rather than merely pleasant.
    ///   tension     a pair about 15 cents apart. The partials do not line up, and the result is
    ///               a slow audible beating — unease with no attack, so it never reads as a buzzer.
    ///   resonance   a single quiet partial, for distant concepts humming to themselves.
    ///
    /// Nothing here is loud, and nothing here has a sharp transient.
    /// </summary>
    public class PrismVoice : MonoBehaviour
    {
        public float Volume = 0.35f;

        const int SampleRate = 48000;

        AudioSource _source;
        AudioClip _consonance;
        AudioClip _tension;
        AudioClip _resonance;
        AudioClip _settle;

        void Awake()
        {
            _source = gameObject.GetComponent<AudioSource>();
            if (_source == null) _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;          // everything in PRISM has a place
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 0.3f;
            _source.maxDistance = 6f;

            // A perfect fifth on a low, soft root.
            _consonance = Tone("PrismConsonance", 1.6f,
                               new[] { 220f, 330f, 660f },
                               new[] { 0.55f, 0.40f, 0.10f }, attack: 0.12f, release: 1.1f);

            // Two tones 15 cents apart: ~1.9 Hz of beating at this root.
            _tension = Tone("PrismTension", 2.2f,
                            new[] { 220f, 221.9f, 293f },
                            new[] { 0.45f, 0.45f, 0.12f }, attack: 0.35f, release: 1.4f);

            _resonance = Tone("PrismResonance", 2.6f,
                              new[] { 440f, 880f },
                              new[] { 0.22f, 0.06f }, attack: 0.9f, release: 1.6f);

            // A single soft octave drop, for something coming to rest.
            _settle = Tone("PrismSettle", 1.1f,
                           new[] { 330f, 165f },
                           new[] { 0.30f, 0.45f }, attack: 0.05f, release: 0.85f);
        }

        /// <summary>
        /// Additive synthesis with a raised-cosine envelope. The envelope shape is the important
        /// part: an instant attack is what makes a sound feel like an alert, and nothing in PRISM
        /// should ever alert anybody.
        /// </summary>
        static AudioClip Tone(string name, float seconds, float[] partials, float[] gains,
                              float attack, float release)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[n];

            int attackSamples  = Mathf.Max(1, (int)(attack * SampleRate));
            int releaseSamples = Mathf.Max(1, (int)(release * SampleRate));

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float s = 0f;
                for (int k = 0; k < partials.Length; k++)
                    s += Mathf.Sin(2f * Mathf.PI * partials[k] * t) * gains[k];

                float env = 1f;
                if (i < attackSamples)
                    env = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / attackSamples);
                int fromEnd = n - 1 - i;
                if (fromEnd < releaseSamples)
                    env *= 0.5f - 0.5f * Mathf.Cos(Mathf.PI * fromEnd / releaseSamples);

                data[i] = s * env * 0.28f;
            }

            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void PlayAt(AudioClip clip, Vector3 where, float gain, float pitch)
        {
            if (clip == null) return;
            transform.position = where;
            _source.pitch = pitch;
            _source.PlayOneShot(clip, Mathf.Clamp01(Volume * gain));
        }

        /// <summary>A relationship the learner has correctly brought together.</summary>
        public void Consonance(Vector3 where, float gain = 1f, float pitch = 1f)
            => PlayAt(_consonance, where, gain, pitch);

        /// <summary>Two things that do not belong together. Unease, not error.</summary>
        public void Tension(Vector3 where, float gain = 0.8f)
            => PlayAt(_tension, where, gain, 1f);

        /// <summary>A distant concept, faintly audible.</summary>
        public void Resonance(Vector3 where, float gain = 0.35f, float pitch = 1f)
            => PlayAt(_resonance, where, gain, pitch);

        /// <summary>Something arriving at rest — an orbit closing, a moon seating on a ring.</summary>
        public void Settle(Vector3 where, float gain = 0.7f, float pitch = 1f)
            => PlayAt(_settle, where, gain, pitch);
    }
}
