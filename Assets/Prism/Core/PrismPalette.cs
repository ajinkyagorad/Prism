using UnityEngine;

namespace Prism.Core
{
    /// <summary>
    /// The palette law, mirrored from PrismOptics.hlsl. Colour in PRISM encodes meaning;
    /// it never decorates. Both halves of this pair must agree, so if a stop changes here
    /// it changes in the shader include too.
    ///
    /// The ramp is deliberately narrow — six restrained pastels against warm white. The
    /// discipline matters: once every hue means something, an arbitrary tint anywhere in
    /// the product is a lie the learner will eventually try to read.
    /// </summary>
    public static class PrismPalette
    {
        public static readonly Color Cyan     = new Color(0.612f, 0.878f, 0.906f);
        public static readonly Color Mint     = new Color(0.678f, 0.925f, 0.808f);
        public static readonly Color Lavender = new Color(0.780f, 0.749f, 0.937f);
        public static readonly Color Coral    = new Color(0.973f, 0.706f, 0.663f);
        public static readonly Color Gold     = new Color(0.961f, 0.859f, 0.639f);
        public static readonly Color Violet   = new Color(0.443f, 0.361f, 0.612f);

        /// <summary>Warm white. Never pure 1,1,1 — pure white has no light in it.</summary>
        public static readonly Color Warm = new Color(0.988f, 0.984f, 0.973f);

        static readonly Color[] Ramp = { Cyan, Mint, Lavender, Coral, Gold, Violet };

        /// <summary>Continuous spectral ramp over [0,1]. Matches Prism_Spectral in HLSL.</summary>
        public static Color Spectral(float t)
        {
            t = Mathf.Clamp01(t) * (Ramp.Length - 1);
            int i = Mathf.Min((int)t, Ramp.Length - 2);
            return Color.Lerp(Ramp[i], Ramp[i + 1], Mathf.SmoothStep(0f, 1f, t - i));
        }

        /// <summary>
        /// Each domain of knowledge owns a fixed position on the ramp, so a learner who has
        /// spent time in one field can recognise a neighbouring field's colour on sight.
        /// </summary>
        public static float DomainHue(ConceptDomain domain)
        {
            switch (domain)
            {
                case ConceptDomain.MatterAndEnergy:  return 0.72f;  // coral/gold
                case ConceptDomain.SpaceAndTime:     return 0.90f;  // violet
                case ConceptDomain.LifeAndEvolution: return 0.22f;  // mint
                case ConceptDomain.BodyAndMind:      return 0.62f;  // coral
                case ConceptDomain.Machines:         return 0.05f;  // cyan
                case ConceptDomain.EarthAndCiv:      return 0.30f;  // mint/lavender
                case ConceptDomain.Mathematics:      return 0.44f;  // lavender
                case ConceptDomain.Language:         return 0.52f;
                case ConceptDomain.ArtAndMusic:      return 0.80f;
                case ConceptDomain.Society:          return 0.36f;
                default:                             return 0.44f;
            }
        }

        public static Color DomainColour(ConceptDomain domain) => Spectral(DomainHue(domain));
    }
}
