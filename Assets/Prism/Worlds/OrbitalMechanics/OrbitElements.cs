using UnityEngine;

namespace Prism.Worlds.Orbital
{
    public enum ConicKind { Circle, Ellipse, Parabola, Hyperbola }

    /// <summary>
    /// The classical orbital elements, recovered from a position and velocity.
    ///
    /// This is the machinery behind the Formalize stage. The learner has already thrown a
    /// dozen moons and watched some come back and some leave; this struct is what lets the
    /// world put the right NAME on the shape they have just drawn, at the moment they draw
    /// it — and, crucially, what lets it name a hyperbola without ever having told them in
    /// advance that hyperbolae were a thing they might make.
    /// </summary>
    public struct OrbitElements
    {
        /// <summary>Specific orbital energy, J/kg. Negative is bound; this single sign is the whole lesson.</summary>
        public float SpecificEnergy;
        /// <summary>Specific angular momentum vector, m^2/s. Constant along the path — Kepler's second law.</summary>
        public Vector3 AngularMomentum;
        public float Eccentricity;
        /// <summary>Semi-major axis, m. Negative for a hyperbolic path.</summary>
        public float SemiMajorAxis;
        public float PeriapsisRadius;
        /// <summary>Apoapsis radius, m. Infinite for an unbound path.</summary>
        public float ApoapsisRadius;
        /// <summary>Orbital period, s. Infinite for an unbound path.</summary>
        public float Period;
        public ConicKind Kind;
        /// <summary>Unit normal of the orbital plane.</summary>
        public Vector3 PlaneNormal;

        public bool IsBound => SpecificEnergy < 0f;

        /// <summary>
        /// Below this eccentricity we call it a circle. Chosen for the learner, not for the
        /// mathematician: 0.02 is roughly the point at which the difference between periapsis
        /// and apoapsis stops being visible at table scale, so calling it an ellipse would be
        /// technically right and pedagogically useless.
        /// </summary>
        public const float CircleTolerance = 0.02f;

        public static OrbitElements From(Vector3 rVec, Vector3 vVec, float mu)
        {
            var e = new OrbitElements();

            float r = rVec.magnitude;
            float v2 = vVec.sqrMagnitude;
            if (r < 1e-6f || mu <= 0f)
            {
                e.Kind = ConicKind.Ellipse;
                e.Period = float.PositiveInfinity;
                e.ApoapsisRadius = float.PositiveInfinity;
                return e;
            }

            e.SpecificEnergy  = 0.5f * v2 - mu / r;             // vis-viva, rearranged
            e.AngularMomentum = Vector3.Cross(rVec, vVec);
            e.PlaneNormal     = e.AngularMomentum.sqrMagnitude > 1e-12f
                              ? e.AngularMomentum.normalized : Vector3.up;

            // Eccentricity vector: points at periapsis, length is the eccentricity.
            Vector3 eVec = ((v2 - mu / r) * rVec - Vector3.Dot(rVec, vVec) * vVec) / mu;
            e.Eccentricity = eVec.magnitude;

            // a = -mu / 2*energy. Comes out negative for a hyperbola, which is correct and
            // is why PeriapsisRadius below uses a*(1-ecc) unconditionally.
            e.SemiMajorAxis = Mathf.Abs(e.SpecificEnergy) < 1e-9f
                            ? float.PositiveInfinity
                            : -mu / (2f * e.SpecificEnergy);

            if (e.Eccentricity < CircleTolerance)          e.Kind = ConicKind.Circle;
            else if (e.Eccentricity < 1f - 1e-3f)          e.Kind = ConicKind.Ellipse;
            else if (e.Eccentricity <= 1f + 1e-3f)         e.Kind = ConicKind.Parabola;
            else                                           e.Kind = ConicKind.Hyperbola;

            if (e.Kind == ConicKind.Parabola)
            {
                // Semi-latus rectum over two; a is undefined for a true parabola.
                float h2 = e.AngularMomentum.sqrMagnitude;
                e.PeriapsisRadius = h2 / (2f * mu);
                e.ApoapsisRadius  = float.PositiveInfinity;
                e.Period          = float.PositiveInfinity;
            }
            else
            {
                e.PeriapsisRadius = e.SemiMajorAxis * (1f - e.Eccentricity);
                e.ApoapsisRadius  = e.IsBound ? e.SemiMajorAxis * (1f + e.Eccentricity)
                                              : float.PositiveInfinity;
                e.Period          = e.IsBound
                                  ? 2f * Mathf.PI * Mathf.Sqrt(Mathf.Pow(e.SemiMajorAxis, 3f) / mu)
                                  : float.PositiveInfinity;
            }

            return e;
        }

        /// <summary>
        /// The name the world says out loud at Formalize. Deliberately plain language first;
        /// the Greek arrives later or not at all.
        /// </summary>
        public string KindName
        {
            get
            {
                switch (Kind)
                {
                    case ConicKind.Circle:    return "circle";
                    case ConicKind.Ellipse:   return "ellipse";
                    case ConicKind.Parabola:  return "parabola";
                    default:                  return "hyperbola";
                }
            }
        }

        /// <summary>
        /// The vis-viva relation as two quantities that must balance, rather than as a formula
        /// to read. The Formalize stage draws these as two lengths the learner can watch move.
        ///     v^2  =  2mu/r  -  mu/a
        /// </summary>
        public void VisViva(float r, float mu, out float vSquared, out float fromRadius, out float fromOrbit)
        {
            fromRadius = 2f * mu / Mathf.Max(r, 1e-6f);
            fromOrbit  = float.IsInfinity(SemiMajorAxis) ? 0f : mu / SemiMajorAxis;
            vSquared   = fromRadius - fromOrbit;
        }

        public override string ToString() =>
            $"{KindName} e={Eccentricity:0.000} a={SemiMajorAxis:0.0000}m " +
            $"rp={PeriapsisRadius:0.0000}m T={(float.IsInfinity(Period) ? "inf" : Period.ToString("0.00") + "s")} " +
            $"eps={SpecificEnergy:0.00000}";
    }
}
