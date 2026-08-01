using UnityEngine;

namespace Prism.Worlds.PlanetGuardian
{
    /// <summary>
    /// The zero-dimensional energy balance model behind Planet Guardian.
    ///
    /// Plain C#, no MonoBehaviour — the same shape as OrbitalSim: a fixed-step accumulator
    /// advances a deterministic model that has no idea it is being watched.
    ///
    /// THE PHYSICS, stated once, honestly:
    ///
    ///   absorbed  = (1 - albedo) * S / 4      solar flux intercepted by a disc of the planet's
    ///                                          radius, spread over its full sphere — the real
    ///                                          geometric factor between a circle and a sphere,
    ///                                          not a fudge
    ///   outgoing  = epsilon * sigma * T^4     Stefan-Boltzmann, the real constant, with an
    ///                                          effective emissivity epsilon &lt; 1 standing in
    ///                                          for the greenhouse effect (real Earth's
    ///                                          radiates like a ~0.61-emissivity grey body when
    ///                                          you compare surface blackbody output to what
    ///                                          actually leaves the top of the atmosphere)
    ///   C dT/dt   = absorbed - outgoing        the only equation in this file
    ///
    /// Every constant that CAN be Earth's real number, is. The solar constant is DERIVED so the
    /// default configuration balances exactly at Earth's real energy budget — 288 K, 239 W/m^2,
    /// a surface albedo of 0.30 plus the sliver of polar ice that is really there at 288 K — the
    /// same way PrismScale derives its gravitational parameter from a chosen orbit rather than
    /// picking one that merely looks right. CO2 forcing uses the actual IPCC AR5 coefficient,
    /// 5.35 * ln(C/C0), including the doubling-CO2 number (3.7 W/m^2) that falls out of it for free.
    ///
    /// WHAT IS COMPRESSED, and said so out loud: real ocean mixed-layer heat capacity is about
    /// 3x10^8 J/(m^2 K), which would take years to show a learner anything. <see cref="HeatCapacity"/>
    /// is chosen — one number, the same move PrismScale makes for mu — so the SAME physics
    /// settles in tens of seconds instead of years. That is a compression of a real time
    /// constant, not a different model.
    ///
    /// WHAT IS APPROXIMATED, and said so out loud: this has one temperature for the whole
    /// planet. Real poles run colder than the real equator at any given global mean; a 0-D
    /// model has no latitude and cannot represent that. The ice-line thresholds below are
    /// calibrated against the GLOBAL MEAN so the default 288 K state still shows real, modest
    /// polar ice, the way Earth is icy at the poles despite a 288 K planetary average. That is
    /// the standard, named limitation of a 0-D energy balance model — it is exactly why Budyko,
    /// Sellers and North built latitude-resolved models next, and exactly why this one is
    /// honest about not being one.
    /// </summary>
    public class PlanetGuardianSim
    {
        // ---- real constants ---------------------------------------------------
        public const float Sigma = 5.670374419e-8f;   // W/(m^2 K^4) — Stefan-Boltzmann, exact
        public const float TRef = 288f;               // K — Earth's real global mean surface temperature
        public const float OlrRef = 239f;             // W/m^2 — Earth's real outgoing longwave radiation (TOA)
        public const float AlbedoDefault = 0.30f;     // Earth's real bond albedo, ballpark (surface only —
                                                       // the TOTAL default albedo also carries a sliver of
                                                       // ice, see DefaultIceFraction below)
        public const float AlphaCo2 = 5.35f;          // W/m^2 — real IPCC AR5 CO2 forcing coefficient
        public const float Co2Ref = 280f;             // ppm — real pre-industrial reference concentration

        // ---- ice-albedo feedback -----------------------------------------------
        // Calibrated to the GLOBAL MEAN, not the literal 273.15 K freezing point — see the
        // class comment on why. TIceFree sits only 2 K above TRef and TIceFull is 32 K below
        // it: TRef therefore sits near the WARM edge of the band, where the smoothstep slope is
        // gentle, on purpose — see the stability check below. The band's CENTRE (~274 K),
        // reachable by pushing any control, is where the slope is steepest and genuinely
        // unstable; the untouched default is not. That split is deliberate: the planet must
        // hold still on its own, and only go unstable because the learner pushed it there.
        public const float TIceFree = 290f;           // K — global mean above which ice is gone
        public const float TIceFull = 258f;           // K — global mean below which fully iced over
        public const float IceAlbedoMax = 0.5f;       // additional reflectivity at full ice cover

        /// <summary>Ice cover at the untouched default temperature — a thin polar sliver
        /// (about 1 percent of the ice-fraction scale, which the sphere-intersection geometry
        /// renders as a small but genuinely visible cap of several degrees of latitude), not
        /// the ~5 percent an earlier pass at this file used before the stability check below
        /// caught that it put the resting state inside the unstable band.</summary>
        public static readonly float DefaultIceFraction = IceFractionAt(TRef);

        /// <summary>Effective emissivity at the reference CO2 level. Derived, not tuned: the
        /// real ratio of Earth's actual outgoing radiation to a blackbody at its actual mean
        /// temperature (sigma*288^4 = 390 W/m^2 vs an observed 239 W/m^2 leaving the top of
        /// the atmosphere — the gap IS the greenhouse effect).</summary>
        public static readonly float Epsilon0 = OlrRef / (Sigma * TRef * TRef * TRef * TRef);

        /// <summary>
        /// Solar constant, derived so the default configuration is in EXACT steady state —
        /// (1 - manual albedo - ice contribution at TRef) * S/4 equals OlrRef exactly. Earlier
        /// this balanced against AlbedoDefault alone and quietly left a ~9 W/m^2 unbalanced
        /// residual once the (correct, always-on) ice term was added in — a planet that would
        /// have started drifting into its own unprompted tipping cascade before a learner ever
        /// touched anything, silently violating "a temperature that is holding steady". Folding
        /// DefaultIceFraction into this derivation is what makes Wonder actually wonder-shaped.
        /// Lands close to Earth's real solar constant (~1361-1366 W/m^2 depending on citation).
        /// </summary>
        public static readonly float SolarConstantRef =
            4f * OlrRef / (1f - (AlbedoDefault + IceAlbedoMax * DefaultIceFraction));

        // ---- learner-controlled ranges ------------------------------------------
        public const float AlbedoManualMin = 0.10f;   // dark rock/ocean world
        public const float AlbedoManualMax = 0.55f;   // bright desert/cloud-decked world
        public const float AlbedoTotalMax = 0.85f;    // manual+ice clamp — nothing is a perfect mirror
        public const float GhgPpmMin = 50f;
        public const float GhgPpmMax = 6000f;
        public const float SunMultMin = 0.75f;        // a faint young star
        public const float SunMultMax = 1.30f;        // a notably brighter one

        // ---- thermal inertia -----------------------------------------------------
        // Real ocean mixed-layer heat capacity is ~3x10^8 J/(m^2 K); this compresses that by
        // roughly six orders of magnitude so the SAME physics settles in tens of seconds. See
        // the class comment — PrismScale makes exactly this move for orbital gravity.
        public const float HeatCapacity = 60f;        // J/(m^2 K), effective

        public const float FixedStep = 1f / 30f;

        // ---- learner controls, written by the world each frame ------------------
        public float GhgPpm = Co2Ref;
        public float AlbedoManual = AlbedoDefault;
        public float SunMultiplier = 1f;

        /// <summary>A transient additive forcing, W/m^2, subtracted from absorbed. Positive
        /// cools the planet, the way a volcano's aerosols do. Set from outside; the sim only
        /// ever applies whatever value is currently here.</summary>
        public float DisturbanceForcing;

        // ---- state ---------------------------------------------------------------
        public float Temperature { get; private set; } = TRef;
        public float Absorbed { get; private set; }
        public float Outgoing { get; private set; }
        public float Imbalance => Absorbed - Outgoing;
        /// <summary>K/s, the instantaneous rate from the most recent substep.</summary>
        public float DTdt { get; private set; }
        public float IceFraction { get; private set; }
        public float Albedo { get; private set; }
        public float EffectiveEmissivity { get; private set; }

        float _accumulator;

        public PlanetGuardianSim() { Temperature = TRef; }

        /// <summary>Ice cover as a function of temperature: 1 at/below TIceFull, 0 at/above TIceFree.</summary>
        public static float IceFractionAt(float t) => 1f - Mathf.SmoothStep(TIceFull, TIceFree, t);

        /// <summary>
        /// Effective emissivity from CO2 concentration: the real forcing formula, linearised
        /// about the reference climate — which is how forcing numbers are conventionally
        /// derived in the first place, not a simplification invented for this world.
        /// </summary>
        public static float EmissivityAt(float ghgPpm)
        {
            float forcing = AlphaCo2 * Mathf.Log(Mathf.Max(ghgPpm, 1f) / Co2Ref);
            float eps = Epsilon0 - forcing / (Sigma * TRef * TRef * TRef * TRef);
            return Mathf.Clamp(eps, 0.30f, 0.90f);
        }

        /// <summary>Advance by real elapsed time. Leftover time carries to the next call.</summary>
        public void Advance(float realDeltaSeconds)
        {
            _accumulator += realDeltaSeconds;
            int budget = 8;               // a hitch must not spend an unbounded burst catching up
            while (_accumulator >= FixedStep && budget-- > 0)
            {
                Step(FixedStep);
                _accumulator -= FixedStep;
            }
            if (budget <= 0) _accumulator = 0f;
        }

        /// <summary>
        /// One semi-implicit step. The radiative term (epsilon*sigma*T^4) is linearised about
        /// the CURRENT temperature and folded into the denominator — a standard trick for
        /// stepping a stiff radiative relaxation without tiny timesteps, used in real radiation
        /// schemes. It changes nothing about the physics, only how it is stepped, and it is
        /// unconditionally stable: HeatCapacity/dt (1800 at these numbers) dwarfs both the
        /// Planck response (~2-3 W/m^2/K) and the ice-albedo slope (~7-9 W/m^2/K at its
        /// steepest), so the model integrates smoothly through the unstable zone across many
        /// real substeps rather than overshooting numerically. See NOTES.md for the numbers.
        /// </summary>
        void Step(float dt)
        {
            float ghg = Mathf.Clamp(GhgPpm, GhgPpmMin, GhgPpmMax);
            float manualAlbedo = Mathf.Clamp(AlbedoManual, AlbedoManualMin, AlbedoManualMax);
            float sun = Mathf.Clamp(SunMultiplier, SunMultMin, SunMultMax);

            IceFraction = IceFractionAt(Temperature);
            Albedo = Mathf.Min(manualAlbedo + IceAlbedoMax * IceFraction, AlbedoTotalMax);
            EffectiveEmissivity = EmissivityAt(ghg);

            float s = SolarConstantRef * sun;
            Absorbed = (1f - Albedo) * s * 0.25f - DisturbanceForcing;

            float t3 = Temperature * Temperature * Temperature;
            Outgoing = EffectiveEmissivity * Sigma * t3 * Temperature;

            float dOutdT = 4f * EffectiveEmissivity * Sigma * t3;
            float denom = HeatCapacity / dt + dOutdT;
            float deltaT = (Absorbed - Outgoing) / Mathf.Max(denom, 1e-6f);

            Temperature = Mathf.Clamp(Temperature + deltaT, 120f, 500f);
            DTdt = deltaT / dt;
        }

        /// <summary>Editor/challenge use only — snaps state rather than integrating to it.</summary>
        public void ResetTo(float temperature)
        {
            Temperature = Mathf.Clamp(temperature, 120f, 500f);
            _accumulator = 0f;
        }
    }
}
