using UnityEngine;

namespace Prism.Scenery
{
    /// <summary>
    /// The sound of the place: a slow wind over the plateau.
    ///
    /// Added because silence is what made the landscape read as a picture rather than somewhere the
    /// learner is standing. A still image with no sound is a backdrop; the same image with moving
    /// air is a place. This is the cheapest large gain in presence available.
    ///
    /// Synthesised, like everything else here — no audio assets. Pink-ish noise (a one-pole filtered
    /// white source, which rolls off the harshness that makes white noise read as static) with two
    /// slow amplitude swells at incommensurate rates, so the loop never audibly repeats even though
    /// it is only a few seconds long.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class PrismAmbience : MonoBehaviour
    {
        [Range(0f, 1f)] public float Volume = 0.16f;

        [Tooltip("Seconds of generated audio. Longer costs memory; the swells hide the seam.")]
        public float Seconds = 11f;

        const int SampleRate = 24000;   // wind has no content up high; half rate halves the memory

        AudioSource _source;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.clip = Wind();
            _source.loop = true;
            _source.playOnAwake = true;
            _source.spatialBlend = 0f;      // the air is everywhere, not at a point
            _source.volume = Volume;
            _source.Play();
        }

        void OnValidate()
        {
            if (_source != null) _source.volume = Volume;
        }

        AudioClip Wind()
        {
            int n = Mathf.Max(1024, Mathf.CeilToInt(Seconds * SampleRate));
            var data = new float[n];

            // Two one-pole low passes in series: white -> pink-ish -> soft. The second pole is what
            // takes the hiss off and leaves something that reads as air rather than as noise.
            float a = 0f, b = 0f;
            var rng = new System.Random(9371);

            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                a += (white - a) * 0.045f;
                b += (a - b) * 0.10f;

                float t = i / (float)SampleRate;
                // Incommensurate swells, so the gust pattern never lines up with the loop point.
                float swell = 0.55f
                            + 0.30f * Mathf.Sin(t * 0.17f * Mathf.PI * 2f)
                            + 0.15f * Mathf.Sin(t * 0.043f * Mathf.PI * 2f + 1.7f);

                data[i] = b * swell * 3.2f;
            }

            // Crossfade the tail into the head so the loop has no click.
            int fade = Mathf.Min(n / 8, SampleRate * 2);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] = Mathf.Lerp(data[n - fade + i], data[i], k);
            }

            var clip = AudioClip.Create("PrismWind", n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
