using UnityEngine;

namespace Prism.Worlds.LivingCell
{
    /// <summary>
    /// The deterministic chemistry behind the Living Cell world.
    ///
    /// Plain C#, no MonoBehaviour. Same reasoning as OrbitalSim: the integrator choice is testable
    /// without a headset, and frame-rate independence matters more here than almost anywhere else in
    /// PRISM, because a metabolic transient that races ahead on a fast frame and crawls on a slow one
    /// would teach a genuinely wrong lesson about rates.
    ///
    /// -----------------------------------------------------------------------------------------
    /// THE MODEL
    /// -----------------------------------------------------------------------------------------
    /// A cell is not a diagram, it is an open system with matter and energy flowing through it. This
    /// class tracks that flow as coupled ODEs over a handful of well-mixed compartments:
    ///
    ///   Out    the exterior bath (large, but NOT infinite — it can be measurably drawn down)
    ///   Shell  a chain of <see cref="ShellCount"/> thin compartments the oxygen crosses in transit,
    ///          giving oxygen a REAL spatial gradient rather than a single lumped number
    ///   In     the cytoplasm (well mixed — real cells are small enough that this is a fair
    ///          approximation; see NOTES.md)
    ///
    /// Every state variable is an AMOUNT (mole-like units), not a concentration, and every
    /// compartment has a fixed volume. Concentration is always amount/volume, computed on demand.
    /// This is what makes conservation exact rather than approximate: moving mass between two
    /// compartments is always "subtract from one field, add the same number to the other," so total
    /// mass cannot drift regardless of how the rate constants are tuned. See the conservation laws
    /// documented above <see cref="Step"/>.
    ///
    /// Two genuinely different transport mechanisms are modelled, on purpose, because the contrast
    /// is the point of the membrane-transport concept:
    ///
    ///   GLUCOSE   crosses only through a carrier (a Michaelis-Menten-saturating transporter, both
    ///             directions). No carrier, no glucose — which is exactly what makes "block the
    ///             channel with your hand" a meaningful, legible action: there is exactly one door.
    ///   OXYGEN    dissolves straight through the lipid bilayer with no protein needed. That is real
    ///             biology, not a simplification, and it is why oxygen gets the diffusion-chain
    ///             treatment instead: it is the honest example of Fick's law acting alone.
    ///
    /// Reaction kinetics are Michaelis-Menten throughout, never a bare linear or exponential rate:
    /// every enzyme-mediated step saturates because the enzyme itself is a finite, reusable resource.
    /// Catabolism additionally saturates in ADP — which is real ("respiratory control": mitochondria
    /// idle when there is no ADP left to phosphorylate) and is what makes the metabolism-feedback
    /// link in LivingCellWorld.Concepts more than a wordplay: the negative feedback loop (more ATP ->
    /// less ADP -> less substrate for making more ATP; more ATP -> faster spending -> less ATP) is
    /// mechanistically present in these equations, not asserted on top of them.
    /// </summary>
    public class CellSim
    {
        // -----------------------------------------------------------------
        // compartment geometry
        // -----------------------------------------------------------------
        public const int ShellCount = 5;

        /// <summary>Volume of the exterior bath. Large relative to the cell, not infinite.</summary>
        public const float V_Out = 260f;
        /// <summary>Volume of one oxygen shell. Small: the membrane crossing is thin.</summary>
        public const float V_Shell = 5f;
        /// <summary>Volume of the cytoplasm.</summary>
        public const float V_In = 30f;

        /// <summary>Total adenylate pool, ATP + ADP. Conserved exactly, forever. See Step().</summary>
        public const float ApoolTotal = 120f;

        /// <summary>
        /// Fixed step, seconds. The oxygen chain is an explicit finite-difference diffusion, which
        /// is only stable while D*dt stays comfortably under 0.5 (the classic FTCS bound with unit
        /// shell spacing). At D_O2 ~ 1 and dt = 1/60, D*dt ~ 0.017 — more than an order of magnitude
        /// of headroom, so the chain is smooth even before the reaction terms are considered.
        /// </summary>
        public const float FixedStep = 1f / 60f;

        // -----------------------------------------------------------------
        // rate constants — every dial the model has, gathered in one place on purpose.
        //
        // These were sized by order-of-magnitude reasoning (see the comment above Reset()) so the
        // system is stable and responsive on a legible timescale, NOT by empirical tuning against a
        // running build — there is no compiler in this loop, so nobody has watched this simulation
        // run yet. Whoever first does should expect to nudge these, and everything needed to do that
        // safely lives right here with a note on which direction does what.
        // -----------------------------------------------------------------

        /// <summary>Glucose carrier turnover cap, amount/s, at gate fully open.</summary>
        public float VmaxChannel = 55f;
        /// <summary>Glucose carrier half-saturation concentration. Lower = saturates at a smaller gradient.</summary>
        public float KmChannel = 2.2f;
        /// <summary>How much a membrane puncture (pinch) adds to glucose's leak, on top of the carrier.</summary>
        public float LeakGainGlucose = 40f;

        /// <summary>Oxygen diffusion rate constant across one shell boundary, amount/s per unit concentration.</summary>
        public float D_O2 = 0.9f;
        /// <summary>How much a puncture multiplies the oxygen diffusion rate.</summary>
        public float LeakGainO2 = 5f;

        /// <summary>Waste efflux permeability. Always open — a cell cannot gate what it excretes.</summary>
        public float PermeabilityWaste = 1.2f;

        /// <summary>Catabolism turnover cap, amount/s. Raising this raises the ceiling on how fast the cell CAN make ATP.</summary>
        public float VmaxCatabolism = 10f;
        public float KmGlucose = 1.5f;
        public float KmOxygen = 1.5f;
        /// <summary>ADP half-saturation. This is respiratory control: low ADP throttles catabolism even with fuel to spare.</summary>
        public float KmADP = 3.0f;
        /// <summary>ATP made per unit of catabolism turnover. Simplified stoichiometry — see NOTES.md.</summary>
        public float AtpYield = 3f;

        /// <summary>Baseline desired ATP draw, amount/s, before any demand multiplier.</summary>
        public float DemandBaseline = 6f;
        public float KmWork = 2.0f;

        // -----------------------------------------------------------------
        // state — every quantity here is an AMOUNT, not a concentration.
        // -----------------------------------------------------------------
        public float Gout, Gin;
        public readonly float[] O2Shell = new float[ShellCount];
        public float O2out, O2in;
        public float Win, Wout;
        public float Atp, Adp;

        // -----------------------------------------------------------------
        // learner- and world-driven inputs
        // -----------------------------------------------------------------
        /// <summary>0 = glucose channel fully blocked, 1 = fully open. Driven by hand proximity.</summary>
        public float ChannelOpen = 1f;
        /// <summary>0..1, decays on its own. A pinched membrane briefly leaks everything faster.</summary>
        public float Leak;
        /// <summary>Multiplies DemandBaseline. 1 = resting. Driven by CellChallenge for Apply/Create.</summary>
        public float DemandMultiplier = 1f;
        /// <summary>Multiplies VmaxCatabolism. The learner's own tuning dial, set at Create.</summary>
        public float CatabolismTuning = 1f;

        public float LeakDecayPerSecond = 0.35f;

        // -----------------------------------------------------------------
        // last computed rates, published for the world to read when drawing flux currents.
        // Not part of the state — recomputed every step, read-only from outside.
        // -----------------------------------------------------------------
        public float LastGlucoseFlux { get; private set; }      // amount/s, positive = into the cell
        public float LastOxygenInnerFlux { get; private set; }  // amount/s, shell[last] -> cytoplasm
        public float LastCatabolismRate { get; private set; }   // amount/s of turnover
        public float LastWorkRate { get; private set; }         // amount/s of ATP spent

        public double SimTime { get; private set; }

        readonly float[] _shellFlux = new float[ShellCount + 1]; // boundary i: between (i-1) and i
        float _accumulator;

        public CellSim() { Reset(); }

        /// <summary>
        /// Starting point: comfortably in the middle of everything, on purpose. A learner who does
        /// nothing should watch a cell that is plainly ALIVE and busy but not already at an extreme —
        /// shortage and surplus have to be things the learner causes, not things the cell starts at.
        /// </summary>
        public void Reset()
        {
            Gout = 6.0f * V_Out;
            Gin  = 1.0f * V_In;

            O2out = 8.0f * V_Out;
            O2in  = 2.0f * V_In;
            for (int i = 0; i < ShellCount; i++)
            {
                float t = (i + 0.5f) / ShellCount;              // interpolate Out(8) -> In(2)
                O2Shell[i] = Mathf.Lerp(8.0f, 2.0f, t) * V_Shell;
            }

            Win = 0.5f * V_In;
            Wout = 0f;

            Atp = 0.55f * ApoolTotal;
            Adp = ApoolTotal - Atp;

            ChannelOpen = 1f;
            Leak = 0f;
            DemandMultiplier = 1f;
            CatabolismTuning = 1f;

            LastGlucoseFlux = LastOxygenInnerFlux = LastCatabolismRate = LastWorkRate = 0f;
            SimTime = 0;
            _accumulator = 0f;
        }

        // -----------------------------------------------------------------
        // public readouts (concentrations, derived)
        // -----------------------------------------------------------------
        public float AtpFraction => Atp / ApoolTotal;
        public float GlucoseOutC => Gout / V_Out;
        public float GlucoseInC  => Gin / V_In;
        public float OxygenOutC  => O2out / V_Out;
        public float OxygenInC   => O2in / V_In;
        public float OxygenShellC(int i) => O2Shell[Mathf.Clamp(i, 0, ShellCount - 1)] / V_Shell;
        public float WasteInC  => Win / V_In;
        public float WasteOutC => Wout / V_Out;

        // -----------------------------------------------------------------
        // stepping
        // -----------------------------------------------------------------

        /// <summary>Advance by real elapsed time, accumulating leftovers so the sim never drifts against the clock.</summary>
        public void Advance(float realDeltaSeconds)
        {
            _accumulator += Mathf.Max(0f, realDeltaSeconds);

            const int maxStepsPerFrame = 10;
            int budget = maxStepsPerFrame;
            while (_accumulator >= FixedStep && budget-- > 0)
            {
                Step(FixedStep);
                _accumulator -= FixedStep;
            }
            if (budget <= 0) _accumulator = 0f;   // a hitch must not spend the rest of the session catching up
        }

        /// <summary>Michaelis-Menten saturation, c/(km+c). Clamped so a stray negative concentration cannot invert the curve.</summary>
        static float Sat(float c, float km)
        {
            c = Mathf.Max(0f, c);
            km = Mathf.Max(km, 1e-4f);
            return c / (km + c);
        }

        /// <summary>
        /// Keep a proposed flux from removing more than is actually there this step. A safety net,
        /// not part of the chemistry — with the chosen constants it should rarely if ever bind.
        ///   flux &gt; 0 moves amount FROM 'fromPositive' TO the other side.
        ///   flux &lt; 0 moves amount FROM 'fromNegative' the other way.
        /// </summary>
        static float ClampFlux(float flux, float fromPositive, float fromNegative, float dt)
        {
            if (dt <= 0f) return 0f;
            if (flux > 0f)
            {
                float maxAmt = Mathf.Max(0f, fromPositive) * 0.95f / dt;
                return Mathf.Min(flux, maxAmt);
            }
            if (flux < 0f)
            {
                float maxAmt = Mathf.Max(0f, fromNegative) * 0.95f / dt;
                return Mathf.Max(flux, -maxAmt);
            }
            return 0f;
        }

        /// <summary>
        /// One fixed step of explicit Euler.
        ///
        /// Not symplectic, and deliberately so: symplectic integrators matter for motion that ought
        /// to conserve energy over unbounded time (orbits, oscillators) — see OrbitalSim. This system
        /// is relaxational, not conservative: left alone it settles, it does not orbit anything, and
        /// nothing here claims to conserve total chemical energy over time (energy is exactly what
        /// the cell is spending). The quantities that MUST be conserved — the adenylate pool, and
        /// mass across every transport step — are conserved instead by construction: every update
        /// below removes an amount from one field and adds the SAME amount to another. That property
        /// holds regardless of integration order, so plain Euler does not undermine it.
        ///
        /// All rates are computed from the state at the START of the step and applied together at
        /// the end, so the order fields happen to be declared in cannot bias the result.
        /// </summary>
        void Step(float dt)
        {
            // ---- snapshot concentrations ----------------------------------------------------
            float cGout = GlucoseOutC, cGin = GlucoseInC;
            float cO2out = OxygenOutC, cO2in = OxygenInC;
            float cWin = WasteInC, cWout = WasteOutC;
            float cAtp = Atp / V_In, cAdp = Adp / V_In;

            float leakMul = 1f + Leak;

            // ---- 1. glucose transport: saturating carrier, both directions, plus puncture leak ----
            float influx = ChannelOpen * VmaxChannel * Sat(cGout, KmChannel);
            float efflux = ChannelOpen * VmaxChannel * Sat(cGin, KmChannel);
            float leakG = Leak * LeakGainGlucose * (cGout - cGin);
            float netG = ClampFlux(influx - efflux + leakG, Gout, Gin, dt);

            // ---- 2. oxygen diffusion chain: Out - Shell[0..N-1] - In, explicit finite difference ----
            float dEff = D_O2 * leakMul;
            _shellFlux[0] = ClampFlux(dEff * (cO2out - OxygenShellC(0)), O2out, O2Shell[0], dt);
            for (int i = 0; i < ShellCount - 1; i++)
                _shellFlux[i + 1] = ClampFlux(dEff * (OxygenShellC(i) - OxygenShellC(i + 1)), O2Shell[i], O2Shell[i + 1], dt);
            _shellFlux[ShellCount] = ClampFlux(dEff * (OxygenShellC(ShellCount - 1) - cO2in), O2Shell[ShellCount - 1], O2in, dt);

            // ---- 3. waste efflux: simple Fickian, always open, boosted by a puncture ----
            float wasteFlux = ClampFlux(PermeabilityWaste * leakMul * (cWin - cWout), Win, Wout, dt);

            // ---- 4. catabolism: glucose + oxygen + ADP -> waste + ATP, Michaelis-Menten in all three ----
            float rRaw = VmaxCatabolism * CatabolismTuning * Sat(cGin, KmGlucose) * Sat(cO2in, KmOxygen) * Sat(cAdp, KmADP);
            float rMax = Mathf.Min(Mathf.Min(Gin, O2in) * 0.95f / dt, Adp * 0.95f / (Mathf.Max(AtpYield, 1e-4f) * dt));
            float r = Mathf.Max(0f, Mathf.Min(rRaw, rMax));

            // ---- 5. work: demand draws ATP down, saturating in how much ATP is actually there ----
            float demandNow = Mathf.Max(0f, DemandBaseline * DemandMultiplier);
            float workRaw = demandNow * Sat(cAtp, KmWork);
            float workMax = Atp * 0.95f / dt;
            float workRate = Mathf.Max(0f, Mathf.Min(workRaw, workMax));

            // ---- apply every update. Each pair below is the entire conservation law. -------------
            Gout -= netG * dt;              Gin += netG * dt;

            O2out -= _shellFlux[0] * dt;    O2Shell[0] += _shellFlux[0] * dt;
            for (int i = 0; i < ShellCount - 1; i++)
            {
                O2Shell[i]     -= _shellFlux[i + 1] * dt;
                O2Shell[i + 1] += _shellFlux[i + 1] * dt;
            }
            O2Shell[ShellCount - 1] -= _shellFlux[ShellCount] * dt;
            O2in                    += _shellFlux[ShellCount] * dt;

            Win -= wasteFlux * dt;          Wout += wasteFlux * dt;

            // Catabolism: consumes fuel and oxidant at rate r, produces waste at 2r (one "spent"
            // unit per input consumed — see NOTES.md for exactly what this conserves and what it
            // does not), and turns ADP into ATP at rate AtpYield*r.
            Gin  -= r * dt;
            O2in -= r * dt;
            Win  += 2f * r * dt;
            Atp  += AtpYield * r * dt;
            Adp  -= AtpYield * r * dt;

            // Work: spends ATP back into ADP. Together with the line above, ATP+ADP never moves.
            Atp -= workRate * dt;
            Adp += workRate * dt;

            // ---- numerical safety net: floor every pool at zero. Should not normally bind — see
            // ClampFlux above — but guarantees the sim cannot go numerically unstable regardless of
            // how the rate constants above end up being tuned. See the class comment on Step(). ----
            Gout = Mathf.Max(0f, Gout);   Gin = Mathf.Max(0f, Gin);
            O2out = Mathf.Max(0f, O2out); O2in = Mathf.Max(0f, O2in);
            for (int i = 0; i < ShellCount; i++) O2Shell[i] = Mathf.Max(0f, O2Shell[i]);
            Win = Mathf.Max(0f, Win);     Wout = Mathf.Max(0f, Wout);
            Atp = Mathf.Clamp(Atp, 0f, ApoolTotal);
            Adp = ApoolTotal - Atp;        // re-derive rather than clamp separately: keeps the pool exact

            // Exponential-ish decay (Euler-integrated first-order relaxation back to a sealed membrane).
            Leak *= Mathf.Max(0f, 1f - LeakDecayPerSecond * dt);

            LastGlucoseFlux = netG;
            LastOxygenInnerFlux = _shellFlux[ShellCount];
            LastCatabolismRate = r;
            LastWorkRate = workRate;

            SimTime += dt;
        }

        /// <summary>Trigger a puncture: every permeability spikes briefly, then decays back to normal.</summary>
        public void Puncture(float strength = 1f) => Leak = Mathf.Clamp01(Mathf.Max(Leak, strength));

        /// <summary>
        /// Manually deposit glucose directly into the cytoplasm, bypassing the carrier entirely —
        /// what happens when the learner physically carries a glucose mote through the membrane by
        /// hand. Honest in spirit (a molecule that gets past the bilayer by brute force does arrive
        /// in the cytoplasm) even though no real transporter works this way.
        /// </summary>
        public void DepositGlucose(float amount) => Gin += Mathf.Max(0f, amount);
    }
}
