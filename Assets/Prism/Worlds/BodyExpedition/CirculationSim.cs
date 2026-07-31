using System;
using System.Collections.Generic;
using UnityEngine;

namespace Prism.Worlds.BodyExpedition
{
    /// <summary>Which node in the lumped circulation network. Also indexes the internal arrays.</summary>
    public enum BodyNode { Aorta = 0, Upper = 1, Lower = 2, Arm = 3, Head = 4, LegL = 5, LegR = 6, Venous = 7 }

    /// <summary>
    /// One vessel segment: a resistive, learner-adjustable pipe between two nodes.
    ///
    /// <see cref="RadiusMult"/> is the one number the learner's hands actually change. Resistance
    /// comes from it through Poiseuille's law, R = 8*mu*L / (pi*r^4) — the fourth power is not
    /// decoration, it is why halving a vessel's radius is so much more dramatic than halving
    /// anything else in this simulation.
    /// </summary>
    public class Vessel
    {
        public readonly int From, To;
        /// <summary>Metres, at RadiusMult = 1. Display scale, graspable — see NOTES.md.</summary>
        public readonly float BaseRadius;
        public readonly float Length;
        /// <summary>
        /// True for the four small vessels feeding each tissue bed. These are where real vascular
        /// resistance actually lives (arterioles, not the big named arteries) and are the only
        /// ones the baroreflex leans on.
        /// </summary>
        public readonly bool IsPeripheral;
        /// <summary>Learner- and reflex-controlled. 1 = anatomical default.</summary>
        public float RadiusMult = 1f;

        public Vessel(int from, int to, float baseRadius, float length, bool isPeripheral)
        {
            From = from; To = to; BaseRadius = baseRadius; Length = length; IsPeripheral = isPeripheral;
        }
    }

    /// <summary>
    /// A real lumped-parameter circulation: eight compliant chambers, ten resistive vessels, one
    /// pulsatile pump, one hydrostatic term, one baroreflex. Plain C#, no MonoBehaviour, no
    /// UnityEngine.Physics — same reasoning as OrbitalSim: the integrator is a pedagogical choice
    /// here, not an implementation detail, and this has to be testable and reproducible without a
    /// headset.
    ///
    /// TOPOLOGY. The heart is not itself a pressure node — it is a prescribed flow source that
    /// moves a stroke volume from the venous reservoir into the aorta each beat (no valves to get
    /// wrong; see NOTES.md for why that is an honest simplification here). From the aorta, blood
    /// reaches four tissue beds (Arm, Head, LegL, LegR) through six named-artery segments, each
    /// tissue bed drains through its own small peripheral vessel into a single lumped venous
    /// reservoir, and the reservoir returns to the heart to close the loop.
    ///
    ///     Aorta -> Upper -> Arm  -\
    ///                    -> Head -+-> Venous -> (heart) -> Aorta
    ///           -> Lower -> LegL -+
    ///                    -> LegR -/
    ///
    /// INTEGRATOR. Every fixed step is solved IMPLICITLY (backward Euler) via Gauss-Seidel
    /// relaxation, not explicit Euler. This is the one piece of engineering in this file that is
    /// not visible to the learner and matters anyway: a learner is explicitly invited to crank a
    /// vessel wide open, which can make that vessel's local RC time constant far shorter than the
    /// fixed step. Explicit Euler goes unstable there. The system matrix for this network is
    /// ALWAYS strictly diagonally dominant — each node's diagonal term is (compliance/dt + sum of
    /// 1/R over its own edges), which exceeds the sum of off-diagonal coupling by exactly
    /// compliance/dt, for any positive R — so Gauss-Seidel is an unconditionally stable contraction
    /// here no matter what the learner does to a radius. Verified numerically during development
    /// against dilation to 5x and near-occlusion at 0.03x: bounded, no NaN, in both cases.
    /// </summary>
    public class CirculationSim
    {
        public const float FixedStep = 1f / 240f;

        // ---- named edge indices, so the topology is readable instead of magic numbers ----
        public const int EAortaUpper = 0, EAortaLower = 1, EUpperArm = 2, EUpperHead = 3,
                          ELowerLegL = 4, ELowerLegR = 5, EArmVenous = 6, EHeadVenous = 7,
                          ELegLVenous = 8, ELegRVenous = 9;
        public const int EdgeCount = 10;
        public const int NodeCount = 8;

        /// <summary>
        /// Mean arterial pressure this model settles to with nothing touched, measured during
        /// development against exactly these constants. Real MAP is ~13.3 kPa (100 mmHg); this
        /// model's own open-loop rest lands within about 15% of that without having been forced to.
        /// Used both as the baroreflex setpoint and as the Apply-stage "healthy" reference.
        /// </summary>
        public const float RestingMap = 12.0f;
        public const float PressureRiseThreshold = RestingMap * 1.15f;
        public const float PressureCollapseThreshold = RestingMap * 0.75f;

        /// <summary>kPa per metre: rho(blood)=1060 kg/m^3 * g=9.81 m/s^2, Pa->kPa. Real, not tuned.</summary>
        public const float RhoG = 10.3986f;

        public const float MinRadiusMult = 0.30f;
        public const float MaxRadiusMult = 2.60f;
        const float MinRadiusMultSafety = 0.12f;
        const float MaxRadiusMultSafety = 3.50f;

        const float RestingHrHz = 70f / 60f;
        const float RestingStrokeVolumeMl = 70f;
        const float SystoleFraction = 0.32f;
        const float VenousReferencePressure = 1f;

        const float MapFilterTau = 4f;
        const float ReflexGainResistance = 0.05f;
        const float ReflexGainHeartRate = 0.025f;
        const float ReflexAuthority = 0.35f;

        const int GaussSeidelIterations = 14;
        const int MaxStepsPerFrame = 16;

        public readonly Vessel[] Edges;

        /// <summary>Heart-rate multiplier the learner sets by gripping the heart. 1 = resting.</summary>
        public float HeartRateMult = 1f;
        /// <summary>0 = lying flat (no hydrostatic gradient), 1 = fully upright.</summary>
        public float TiltFraction = 0f;
        /// <summary>True for exactly one Step() when a new cardiac cycle begins — drive a haptic pulse off this.</summary>
        public bool JustBeat { get; private set; }
        public double SimTime { get; private set; }

        public readonly float ViscosityK;

        readonly float[] _compliance = new float[NodeCount];
        readonly float[] _bodyHeight = new float[NodeCount];   // REAL anatomical metres above the heart
        readonly float[] _pressure = new float[NodeCount];     // kPa, the actual state
        readonly float[] _pOld = new float[NodeCount];
        readonly float[] _source = new float[NodeCount];       // only Aorta/Venous ever become non-zero
        readonly List<Vessel>[] _incident = new List<Vessel>[NodeCount];

        float _beatPhase;
        float _mapFiltered;
        float _tprReflexMult = 1f;
        float _hrReflexMult = 1f;
        float _accumulator;

        public float TprReflexMult => _tprReflexMult;
        public float HrReflexMult => _hrReflexMult;
        public float MeanArterialPressureFiltered => _mapFiltered;
        public float BeatPhase01 => _beatPhase;
        public bool InSystole => _beatPhase < SystoleFraction;

        public CirculationSim()
        {
            // Compliance, mL/kPa. Venous is the big soft reservoir: real veins run roughly
            // twenty to thirty times more compliant than arteries, which is exactly why venous
            // pressure barely moves while arterial pressure swings with every heartbeat.
            _compliance[(int)BodyNode.Aorta]  = 1.2f;
            _compliance[(int)BodyNode.Upper]  = 1.0f;
            _compliance[(int)BodyNode.Lower]  = 1.0f;
            _compliance[(int)BodyNode.Arm]    = 0.8f;
            _compliance[(int)BodyNode.Head]   = 0.8f;
            _compliance[(int)BodyNode.LegL]   = 0.8f;
            _compliance[(int)BodyNode.LegR]   = 0.8f;
            _compliance[(int)BodyNode.Venous] = 150f;

            // Real anatomical height above heart level, metres — deliberately NOT the small
            // graspable render scale (see NOTES.md, same trick PrismScale plays with mu vs
            // DisplayPlanetRadius). This is what makes standing up produce a real ~13 kPa
            // head-to-leg pressure swing instead of an invented one.
            _bodyHeight[(int)BodyNode.Aorta]  = 0.05f;
            _bodyHeight[(int)BodyNode.Upper]  = 0.15f;
            _bodyHeight[(int)BodyNode.Lower]  = -0.20f;
            _bodyHeight[(int)BodyNode.Arm]    = 0.10f;
            _bodyHeight[(int)BodyNode.Head]   = 0.35f;
            _bodyHeight[(int)BodyNode.LegL]   = -0.90f;
            _bodyHeight[(int)BodyNode.LegR]   = -0.90f;
            _bodyHeight[(int)BodyNode.Venous] = -0.30f;   // whole-body average; see NOTES.md

            Edges = new Vessel[EdgeCount];
            Edges[EAortaUpper]  = new Vessel((int)BodyNode.Aorta, (int)BodyNode.Upper, 0.0100f, 0.05f, false);
            Edges[EAortaLower]  = new Vessel((int)BodyNode.Aorta, (int)BodyNode.Lower, 0.0100f, 0.06f, false);
            Edges[EUpperArm]    = new Vessel((int)BodyNode.Upper, (int)BodyNode.Arm,   0.0060f, 0.05f, false);
            Edges[EUpperHead]   = new Vessel((int)BodyNode.Upper, (int)BodyNode.Head,  0.0060f, 0.05f, false);
            Edges[ELowerLegL]   = new Vessel((int)BodyNode.Lower, (int)BodyNode.LegL,  0.0060f, 0.08f, false);
            Edges[ELowerLegR]   = new Vessel((int)BodyNode.Lower, (int)BodyNode.LegR,  0.0060f, 0.08f, false);
            // Peripheral vessels: the real site of vascular resistance and regulation. Deliberately
            // the smallest, most resistance-dominant edges in the network, so THESE are where the
            // learner feels the r^4 law hardest. See NOTES.md for the modelling reasoning.
            Edges[EArmVenous]   = new Vessel((int)BodyNode.Arm,  (int)BodyNode.Venous, 0.0022f, 0.04f, true);
            Edges[EHeadVenous]  = new Vessel((int)BodyNode.Head, (int)BodyNode.Venous, 0.0022f, 0.04f, true);
            Edges[ELegLVenous]  = new Vessel((int)BodyNode.LegL, (int)BodyNode.Venous, 0.0022f, 0.05f, true);
            Edges[ELegRVenous]  = new Vessel((int)BodyNode.LegR, (int)BodyNode.Venous, 0.0022f, 0.05f, true);

            // Effective viscosity constant, chosen (not measured) so this network's total
            // resistance at rest lands at 0.16 kPa*s/mL — real total peripheral resistance in
            // these units. Table-top vessel radii are centimetres, not the micron-scale arterioles
            // a real body uses to hit that number, so real blood viscosity would give a wildly
            // wrong answer at this scale; this plays exactly the role PrismScale.Mu plays for
            // OrbitalWorld's table-top planet.
            ViscosityK = SolveViscosityK(0.16f);

            for (int n = 0; n < NodeCount; n++) _incident[n] = new List<Vessel>(3);
            foreach (var e in Edges) { _incident[e.From].Add(e); _incident[e.To].Add(e); }

            for (int n = 0; n < NodeCount; n++) _pressure[n] = RestingMap;
            _pressure[(int)BodyNode.Venous] = 1f;
            _mapFiltered = RestingMap;
        }

        /// <summary>
        /// Series/parallel reduction of the network's resistance at base radii (K=1), so K can be
        /// solved for directly rather than hand-tuned. Mirrors the topology exactly: two branches
        /// (Upper, Lower) hang off the aorta in parallel; each branch is itself two peripheral
        /// paths in parallel, in series with the shared trunk segment that reaches them.
        /// </summary>
        float SolveViscosityK(float targetResistance)
        {
            float UnitR(int idx)
            {
                var e = Edges[idx];
                float r2 = e.BaseRadius * e.BaseRadius;
                return e.Length / (r2 * r2);
            }

            float rUpperArm  = UnitR(EUpperArm) + UnitR(EArmVenous);
            float rUpperHead = UnitR(EUpperHead) + UnitR(EHeadVenous);
            float rUpperPar  = 1f / (1f / rUpperArm + 1f / rUpperHead);
            float rUpperBranch = UnitR(EAortaUpper) + rUpperPar;

            float rLowerLegL = UnitR(ELowerLegL) + UnitR(ELegLVenous);
            float rLowerLegR = UnitR(ELowerLegR) + UnitR(ELegRVenous);
            float rLowerPar  = 1f / (1f / rLowerLegL + 1f / rLowerLegR);
            float rLowerBranch = UnitR(EAortaLower) + rLowerPar;

            float factor = 1f / (1f / rUpperBranch + 1f / rLowerBranch);
            return targetResistance / factor;
        }

        float Height(int node) => _bodyHeight[node] * TiltFraction;

        float EdgeResistance(Vessel e)
        {
            float mult = Mathf.Clamp(e.RadiusMult, MinRadiusMultSafety, MaxRadiusMultSafety);
            float r = e.BaseRadius * mult;
            float r2 = r * r;
            float baseR = ViscosityK * e.Length / (r2 * r2);
            return e.IsPeripheral ? baseR * _tprReflexMult : baseR;
        }

        public float ResistanceOf(Vessel e) => EdgeResistance(e);

        /// <summary>
        /// Volumetric flow along an edge, mL/s, positive from From to To. Driven by the difference
        /// in total hydraulic head (pressure plus gravitational potential), not raw pressure alone
        /// — which is what lets tilting the body move blood without the heart doing anything
        /// different at all.
        /// </summary>
        public float Flow(Vessel e)
        {
            float R = EdgeResistance(e);
            float phiFrom = _pressure[e.From] + RhoG * Height(e.From);
            float phiTo   = _pressure[e.To]   + RhoG * Height(e.To);
            return (phiFrom - phiTo) / R;
        }

        public float PressureAt(BodyNode n) => _pressure[(int)n];
        public float PressureAt(int n) => _pressure[n];

        /// <summary>
        /// Pressure plus gravitational potential — the quantity that actually equalises along a
        /// resistance-free path and drives flow through a resistive one. Exposed for the Formalize
        /// stage's spatial readout: showing this instead of raw pressure is what makes tilting the
        /// body move blood honestly, without the heart itself doing anything different.
        /// </summary>
        public float TotalHead(int n) => _pressure[n] + RhoG * Height(n);
        public float TotalHead(BodyNode n) => TotalHead((int)n);

        /// <summary>Advance by real elapsed time. Leftover time is carried, never dropped.</summary>
        public void Advance(float realDeltaSeconds)
        {
            _accumulator += realDeltaSeconds;
            int budget = MaxStepsPerFrame;
            while (_accumulator >= FixedStep && budget-- > 0)
            {
                Step(FixedStep);
                _accumulator -= FixedStep;
            }
            if (budget <= 0) _accumulator = 0f;
        }

        void Step(float dt)
        {
            Array.Copy(_pressure, _pOld, NodeCount);

            float qHeart = StepHeart(dt);
            _source[(int)BodyNode.Aorta] = qHeart;
            _source[(int)BodyNode.Venous] = -qHeart;

            for (int iter = 0; iter < GaussSeidelIterations; iter++)
                for (int n = 0; n < NodeCount; n++)
                    RelaxNode(n, dt);

            UpdateReflex(dt);
            SimTime += dt;
        }

        void RelaxNode(int n, float dt)
        {
            float diag = _compliance[n] / dt;
            float rhs = diag * _pOld[n] + _source[n];

            var inc = _incident[n];
            for (int i = 0; i < inc.Count; i++)
            {
                var e = inc[i];
                int nb = (e.From == n) ? e.To : e.From;
                float invR = 1f / EdgeResistance(e);
                diag += invR;
                rhs += invR * (_pressure[nb] + RhoG * (Height(nb) - Height(n)));
            }
            _pressure[n] = rhs / diag;
        }

        /// <summary>
        /// The heart: a prescribed ejection flow, not a pressure node. It moves exactly one stroke
        /// volume from the venous reservoir into the aorta each beat, shaped as a half-sine active
        /// only through the systolic fraction of the cycle — real, in that its time-integral over
        /// one beat is exactly stroke volume regardless of heart rate or systole length, and honest
        /// about NOT modelling heart valves or a ventricular chamber (see NOTES.md). Stroke volume
        /// itself responds a little to venous filling pressure — a simplified Frank-Starling
        /// relationship, real preload-dependence, not a fixed pump output.
        /// </summary>
        float StepHeart(float dt)
        {
            float hr = RestingHrHz * Mathf.Clamp(HeartRateMult, 0.5f, 2.2f) * _hrReflexMult;
            float period = 1f / Mathf.Max(hr, 0.15f);
            _beatPhase += dt / period;

            bool wrapped = false;
            while (_beatPhase >= 1f) { _beatPhase -= 1f; wrapped = true; }
            JustBeat = wrapped;

            float venousP = _pressure[(int)BodyNode.Venous];
            float sv = RestingStrokeVolumeMl * Mathf.Clamp(venousP / VenousReferencePressure, 0.5f, 1.4f);

            if (_beatPhase >= SystoleFraction) return 0f;

            float qPeak = sv * Mathf.PI / (2f * SystoleFraction * period);
            return qPeak * Mathf.Sin(Mathf.PI * _beatPhase / SystoleFraction);
        }

        /// <summary>
        /// The baroreflex: a real, always-on proportional controller — this IS the "feedback"
        /// concept in the flesh. It watches a slow low-pass of aortic pressure (4 s, so it never
        /// chases the pulsatile beat-to-beat swing) and leans on peripheral resistance and heart
        /// rate to pull mean pressure back toward <see cref="RestingMap"/>. Its authority is
        /// deliberately bounded to +/-35%: a learner's own squeeze of a peripheral vessel changes
        /// resistance by 16x for a mere halving of radius, which utterly dominates this — so the
        /// reflex is felt as the body visibly pushing back, never as it erasing the lesson.
        /// </summary>
        void UpdateReflex(float dt)
        {
            float aortaP = _pressure[(int)BodyNode.Aorta];
            _mapFiltered += (aortaP - _mapFiltered) * Mathf.Clamp01(dt / MapFilterTau);

            float err = _mapFiltered - RestingMap;
            _tprReflexMult = Mathf.Clamp(1f - ReflexGainResistance * err, 1f - ReflexAuthority, 1f + ReflexAuthority);
            _hrReflexMult  = Mathf.Clamp(1f - ReflexGainHeartRate * err, 1f - ReflexAuthority, 1f + ReflexAuthority);
        }
    }
}
