using UnityEngine;

namespace Prism.Core
{
    /// <summary>
    /// How PRISM shrinks a solar system onto a table without lying about it.
    ///
    /// The temptation is to fake gravity with a spring or a tuned attractor so that thrown
    /// objects "look orbital". That would be a lie the learner eventually catches: the orbits
    /// would not close, the periods would not follow Kepler, and the intuition built at table
    /// scale would not transfer to the real thing.
    ///
    /// Instead we keep Newton exactly and choose one number. Pick the standard gravitational
    /// parameter mu = GM of the table-top planet so that an orbit at a comfortable arm's-length
    /// radius has a period a learner can watch:
    ///
    ///     T = 2*pi*sqrt(r^3 / mu)   =>   mu = 4*pi^2*r^3 / T^2
    ///
    /// For r = 0.20 m and T = 6.0 s that gives mu = 8.773e-3 m^3/s^2. Everything else follows
    /// from real mechanics with no further fudging, and the numbers land somewhere remarkable:
    ///
    ///     circular speed at 20 cm    v = sqrt(mu/r)   = 0.209 m/s
    ///     escape speed at 20 cm      v = sqrt(2mu/r)  = 0.296 m/s
    ///
    /// Both of those are ordinary hand speeds. The difference between putting a moon into orbit
    /// and throwing it away forever is about nine centimetres per second of wrist — which means
    /// the learner can FEEL the boundary between bound and unbound motion in their own arm,
    /// long before anyone writes down an energy equation. That is the entire reason this
    /// constant has the value it has.
    /// </summary>
    public static class PrismScale
    {
        /// <summary>Reference orbit radius used to derive mu, metres.</summary>
        public const float ReferenceRadius = 0.20f;

        /// <summary>Period of a circular orbit at <see cref="ReferenceRadius"/>, seconds.</summary>
        public const float ReferencePeriod = 6.0f;

        /// <summary>
        /// Standard gravitational parameter of the table-top planet, m^3/s^2.
        /// Derived, not chosen: 4*pi^2*r^3/T^2.
        /// </summary>
        public static readonly float Mu =
            4f * Mathf.PI * Mathf.PI * ReferenceRadius * ReferenceRadius * ReferenceRadius
            / (ReferencePeriod * ReferencePeriod);

        /// <summary>Speed of a circular orbit at radius r.</summary>
        public static float CircularSpeed(float r) => Mathf.Sqrt(Mu / Mathf.Max(r, 1e-5f));

        /// <summary>Escape speed at radius r.</summary>
        public static float EscapeSpeed(float r) => Mathf.Sqrt(2f * Mu / Mathf.Max(r, 1e-5f));

        /// <summary>Period of a circular orbit at radius r.</summary>
        public static float CircularPeriod(float r) =>
            2f * Mathf.PI * Mathf.Sqrt(r * r * r / Mu);

        /// <summary>
        /// Period from semi-major axis — Kepler's third law. Only meaningful for a bound orbit.
        /// </summary>
        public static float PeriodFromSemiMajor(float a) =>
            a <= 0f ? float.PositiveInfinity : 2f * Mathf.PI * Mathf.Sqrt(a * a * a / Mu);

        /// <summary>
        /// The equivalent real-world scale factor, for the moment a learner asks "how big is
        /// this really?" — Earth's radius is 6371 km, and the display planet is 4 cm.
        /// </summary>
        public const float DisplayPlanetRadius = 0.04f;
        public const float EarthRadiusMetres   = 6371000f;
        public static float LengthScale => DisplayPlanetRadius / EarthRadiusMetres;
    }
}
